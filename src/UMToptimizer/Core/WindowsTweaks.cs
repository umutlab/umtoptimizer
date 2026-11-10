using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace UMToptimizer.Core;

public sealed record WindowsOptions(bool RestorePoint = true, bool AllowBitLocker = false, bool ReenableBitLocker = false);

/// <summary>Kayıtlı Windows yedeğinin özeti (dosya yoksa null).</summary>
public sealed record WindowsBackupInfo(DateTime Date, bool HadBitLocker);

/// <summary>Gömülü PowerShell betiğini yönetici olarak, gizli pencerede çalıştırır ve günlüğünü canlı izler.</summary>
public static class WindowsTweaks
{
    public static readonly IReadOnlyList<string> Modes = ["Standard", "Aggressive", "Restore"];

    private static readonly Lazy<string> ScriptText = new(() =>
    {
        using var res = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("UMToptimizer-Windows.ps1")
            ?? throw new InvalidOperationException("Gömülü Windows betiği bulunamadı."));
        return res.ReadToEnd();
    });

    /// <summary>Gömülü betik metni (BOM'suz).</summary>
    public static string Script => ScriptText.Value;

    /// <summary>Yedek dosyasını okur. Dosya yoksa/okunamıyorsa null; asla istisna fırlatmaz.</summary>
    public static WindowsBackupInfo? ReadBackupInfo()
    {
        try
        {
            var file = new FileInfo(AppPaths.WindowsBackup);
            if (!file.Exists) return null;
            var hadBitLocker = false;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file.FullName));
                hadBitLocker = doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("BitLockerWasOn", out var b) && b.ValueKind == JsonValueKind.True;
            }
            catch (JsonException) { /* bozuk yedek: tarih yine gösterilir, betik kendi doğrulamasını yapar */ }
            return new(file.LastWriteTime, hadBitLocker);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    /// <param name="mode">Standard | Aggressive | Restore</param>
    public static async Task RunAsync(string mode, WindowsOptions o, Action<string>? log = null, CancellationToken ct = default)
    {
        if (!Modes.Contains(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(powershell)) throw new UserException("Windows PowerShell bulunamadı; Windows profilleri bu bilgisayarda çalıştırılamıyor.");

        string dir;
        try { dir = Directory.CreateTempSubdirectory("umt-").FullName; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new UserException("Geçici klasör oluşturulamadı.", e); }
        var script = Path.Combine(dir, "UMToptimizer-Windows.ps1");
        var logFile = Path.Combine(AppPaths.WindowsData, "logs", $"windows-{mode.ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        try
        {
            var text = Script;
            // Windows PowerShell 5.1 BOM'suz dosyayı ANSI okur → Türkçe karakterler bozulur. BOM ile yaz.
            await File.WriteAllTextAsync(script, text, new UTF8Encoding(true), ct).ConfigureAwait(false);

            var psi = new ProcessStartInfo(powershell)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                // GÜVENLİK: çalışma klasörü kullanıcının yazabildiği TEMP olmamalı; yükseltilmiş süreç oradan
                // yalın adla bir program başlatırsa (Start-Process) araya sokulmuş bir exe yönetici olarak çalışırdı.
                WorkingDirectory = Environment.SystemDirectory,
            };
            foreach (var a in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", ElevatedCommand(script, text, mode, logFile, o)])
                psi.ArgumentList.Add(a);

            Process p;
            try
            {
                // ShellExecuteEx, UAC penceresi yanıtlanana kadar dönmez; arayüz iş parçacığını kilitlemesin.
                p = await Task.Run(() => Process.Start(psi), ct).ConfigureAwait(false) ?? throw new UserException("PowerShell başlatılamadı.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: UAC'de Hayır
            {
                throw new OperationCanceledException("Yönetici izni verilmedi; hiçbir değişiklik yapılmadı.", ex);
            }
            catch (Win32Exception ex)
            {
                throw new UserException("Yönetici olarak çalıştırma başlatılamadı; hiçbir değişiklik yapılmadı.", ex);
            }

            // Buradan sonra betik çalışmış olabilir: hiçbir hata iletisi "değişiklik yapılmadı" DEMEZ.
            const string unknown = "Windows profilinin sonucu okunamadı. Ayrıntılar günlük klasöründe; yapılan değişiklikleri Geri yükleme'den geri alabilirsin.";
            int exit;
            using (p)
            {
                try
                {
                    await TailAsync(logFile, p, log, ct).ConfigureAwait(false);
                    exit = p.ExitCode;
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException) { throw new UserException(unknown, e); }
            }
            switch (exit)
            {
                case 0: break;
                case 2: throw new OperationCanceledException("İşlem iptal edildi.");
                case 3: throw new UserException("Güvenlik denetimi: betik dosyası çalıştırılmadan önce değiştirilmiş. Hiçbir değişiklik yapılmadı; tekrar dene.");
                default: throw new UserException("Windows profili tamamlanamadı. Ayrıntılar günlük klasöründe; yapılan değişiklikleri Geri yükleme'den geri alabilirsin.");
            }
            if (mode != "Restore") await UserSteps.RunAsync(log, ct).ConfigureAwait(false);
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* geçici klasör sonra temizlenir */ }
        }
    }

    /// <summary>
    /// GÜVENLİK (TOCTOU): betik kullanıcının yazabildiği %TEMP%'e yazılıyor. Dosyayı doğrudan "-File" ile çalıştırmak,
    /// UAC onayı ile çalışma arasında başka bir sürecin dosyayı değiştirmesine izin verirdi. Bunun yerine yükseltilmiş
    /// PowerShell metni BELLEĞE okur, SHA-256 özetini buraya gömülü özetle karşılaştırır ve yalnızca eşleşirse
    /// bellekteki metni çalıştırır. Kontrol ile çalıştırma arasında dosyaya bir daha bakılmaz.
    ///
    /// GÜVENLİK (enjeksiyon): yol ve diğer değişken değerler komut metnine tırnak içinde DEĞİL, Base64 olarak gömülür.
    /// PowerShell düz tırnağın yanında U+2018/2019/201A/201B karakterlerini de tırnak sayar; kullanıcı adı ya da TEMP
    /// yolu bunlardan birini içerirse tırnak kaçışlama yetmez ve özet denetiminden ÖNCE komut çalıştırılabilirdi.
    /// </summary>
    public static string ElevatedCommand(string scriptPath, string text, string mode, string logFile, WindowsOptions o)
    {
        static string B(string s) => $"([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{Convert.ToBase64String(Encoding.UTF8.GetBytes(s))}')))";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var args = $"-Mode {B(mode)} -Yes -LogPath {B(logFile)} -UserSid {B(WindowsIdentity.GetCurrent().User?.Value ?? "")}"
            + (o.RestorePoint ? " -RestorePoint" : "") + (o.AllowBitLocker ? " -AllowBitLocker" : "") + (o.ReenableBitLocker ? " -ReenableBitLocker" : "");
        return $"$c=[IO.File]::ReadAllText({B(scriptPath)});" +
               "$h=[BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($c))).Replace('-','');" +
               $"if($h -ne '{hash}'){{exit 3}};& ([scriptblock]::Create($c)) {args};exit $LASTEXITCODE";
    }

    /// <summary>Yükseltilmiş süreç stdout'u paylaşamaz; bunun yerine yazdığı günlük dosyası satır satır takip edilir.</summary>
    private static async Task TailAsync(string file, Process p, Action<string>? log, CancellationToken ct)
    {
        long pos = 0;
        var decoder = Encoding.UTF8.GetDecoder();
        var pending = new StringBuilder();
        var exit = p.WaitForExitAsync(ct);
        while (true)
        {
            var exited = exit.IsCompleted;
            try
            {
                if (File.Exists(file))
                {
                    await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (fs.Length > pos)
                    {
                        fs.Seek(pos, SeekOrigin.Begin);
                        var bytes = new byte[fs.Length - pos];
                        var read = await fs.ReadAsync(bytes, ct).ConfigureAwait(false);
                        pos += read;
                        var chars = new char[decoder.GetCharCount(bytes, 0, read)];
                        decoder.GetChars(bytes, 0, read, chars, 0);
                        pending.Append(chars).Replace("﻿", "").Replace("\r", "");
                        var text = pending.ToString();
                        var cut = text.LastIndexOf('\n');
                        if (cut >= 0)
                        {
                            foreach (var line in text[..cut].Split('\n')) log?.Invoke(line);
                            pending.Remove(0, cut + 1);
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* günlük o an kilitli; sonraki turda okunur */ }
            if (exited) break;
            await Task.WhenAny(exit, Task.Delay(250, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
        await exit.ConfigureAwait(false);
        if (pending.Length > 0) log?.Invoke(pending.ToString());
    }
}

/// <summary>
/// Windows profilinin kullanıcıya ait klasörlerdeki adımları. Yükseltilmiş betik yerine BURADA, kullanıcının kendi
/// yetkisiyle yapılır: yönetici bir süreç kullanıcının yazabildiği klasörde dosya silerse/izin değiştirirse, araya
/// konmuş bir bağlantı (junction) üzerinden başka klasörlere zarar verilebilir. Kendi yetkisiyle çalışan kod,
/// kullanıcının zaten yapabildiğinden fazlasını yapamaz. Bu adımların hiçbiri profili başarısız saymaz.
/// </summary>
public static class UserSteps
{
    public static async Task RunAsync(Action<string>? log, CancellationToken ct)
    {
        try
        {
            var n = CleanTemp(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"), TimeSpan.FromHours(24), ct);
            log?.Invoke($"✓ Geçici dosyalar temizlendi ({n:N0} dosya; son 24 saatte değişenler korunur)");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { CrashLog.Write("UserSteps.CleanTemp", e); }

        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var storeDb = Path.Combine(local, @"Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalState\store.db");
            var icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
            if (File.Exists(storeDb) && File.Exists(icacls))
            {
                var r = await Proc.RunAsync(icacls, [storeDb, "/deny", "*S-1-1-0:F"], ct: ct).ConfigureAwait(false);
                if (r.ExitCode == 0) log?.Invoke("✓ Mağaza önerilen arama veritabanı engellendi");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { CrashLog.Write("UserSteps.StoreDb", e); }
    }

    /// <summary>
    /// Klasördeki, <paramref name="olderThan"/> süresinden eski dosyaları siler; silinen dosya sayısını döner.
    /// Bağlantıların (junction/symlink) içine GİRMEZ ve onlara dokunmaz. Kök bir bağlantıysa ya da sürücü köküyse
    /// hiçbir şey yapmaz. Yeni dosyalar korunur: çalışan programların o an kullandığı geçici dosyalar bozulmasın.
    /// </summary>
    internal static int CleanTemp(string root, TimeSpan olderThan, CancellationToken ct = default)
    {
        var info = new DirectoryInfo(root);
        if (!info.Exists || info.Parent is null || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return 0;
        return Clean(info, DateTime.UtcNow - olderThan, ct);
    }

    private static int Clean(DirectoryInfo dir, DateTime cutoffUtc, CancellationToken ct)
    {
        var deleted = 0;
        FileSystemInfo[] entries;
        try { entries = dir.GetFileSystemInfos(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (entry is DirectoryInfo sub)
                {
                    deleted += Clean(sub, cutoffUtc, ct);
                    if (sub.LastWriteTimeUtc < cutoffUtc) sub.Delete(); // özyinelemesiz: boş değilse başarısız olur ve kalır
                }
                else if (entry.LastWriteTimeUtc < cutoffUtc)
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReadOnly)) entry.Attributes = FileAttributes.Normal;
                    entry.Delete();
                    deleted++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* kullanımda: atla */ }
        }
        return deleted;
    }
}
