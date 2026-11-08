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

/// <summary>Gömülü PowerShell betiğini yönetici olarak, sessiz (gizli pencere) çalıştırır ve günlüğünü canlı izler.</summary>
public static class WindowsTweaks
{
    public static bool HasBackup => File.Exists(Store.WindowsBackup);
    public static DateTime? BackupDate => HasBackup ? File.GetLastWriteTime(Store.WindowsBackup) : null;

    public static bool BackupHadBitLocker
    {
        get
        {
            try { return HasBackup && JsonDocument.Parse(File.ReadAllText(Store.WindowsBackup)).RootElement.TryGetProperty("BitLockerWasOn", out var b) && b.GetBoolean(); }
            catch { return false; }
        }
    }

    /// <param name="mode">Standard | Aggressive | Restore</param>
    public static async Task RunAsync(string mode, WindowsOptions o, Action<string>? log = null, CancellationToken ct = default)
    {
        var dir = Directory.CreateTempSubdirectory("umt-").FullName;
        var script = Path.Combine(dir, "UMToptimizer-Windows.ps1");
        var logFile = Path.Combine(Path.GetDirectoryName(Store.WindowsBackup)!, "logs", $"windows-{mode.ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        try
        {
            var text = Script;
            // Windows PowerShell 5.1 BOM'suz dosyayı ANSI okur → Türkçe karakterler bozulur. BOM ile yaz.
            await File.WriteAllTextAsync(script, text, new UTF8Encoding(true), ct);

            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = dir,
            };
            foreach (var a in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", ElevatedCommand(script, text, mode, logFile, o)])
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell başlatılamadı.");
            await TailAsync(logFile, p, log, ct);
            if (p.ExitCode == 3) throw new InvalidOperationException("Betik bütünlük doğrulaması başarısız: geçici dosya çalıştırılmadan önce değiştirilmiş. Hiçbir değişiklik yapılmadı.");
            if (p.ExitCode == 2) throw new OperationCanceledException("İşlem iptal edildi.");
            if (p.ExitCode != 0) throw new InvalidOperationException($"Windows betiği {p.ExitCode} koduyla bitti. Günlük: {logFile}");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Yönetici izni verilmedi; hiçbir değişiklik yapılmadı.", ex);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>Gömülü betik metni (BOM'suz).</summary>
    public static string Script
    {
        get
        {
            using var res = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("UMToptimizer-Windows.ps1")!);
            return res.ReadToEnd();
        }
    }

    /// <summary>
    /// GÜVENLİK (TOCTOU): betik kullanıcının yazabildiği %TEMP%'e yazılıyor. Dosyayı doğrudan "-File" ile
    /// çalıştırmak, UAC onayı ile çalışma arasında başka bir sürecin dosyayı değiştirmesine izin verirdi.
    /// Bunun yerine yükseltilmiş PowerShell metni BELLEĞE okur, SHA-256 özetini gömülü özetle karşılaştırır ve
    /// yalnızca eşleşirse bellekteki metni çalıştırır. Kontrol ile çalıştırma arasında dosyaya bir daha bakılmaz.
    /// </summary>
    public static string ElevatedCommand(string scriptPath, string text, string mode, string logFile, WindowsOptions o)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var args = $"-Mode {Q(mode)} -Yes -LogPath {Q(logFile)} -UserSid {Q(WindowsIdentity.GetCurrent().User?.Value ?? "")}"
            + (o.RestorePoint ? " -RestorePoint" : "") + (o.AllowBitLocker ? " -AllowBitLocker" : "") + (o.ReenableBitLocker ? " -ReenableBitLocker" : "");
        return $"$c=[IO.File]::ReadAllText({Q(scriptPath)});" +
               "$h=[BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($c))).Replace('-','');" +
               $"if($h -ne '{hash}'){{exit 3}};& ([scriptblock]::Create($c)) {args};exit $LASTEXITCODE";
    }

    /// <summary>Yükseltilmiş süreç stdout'u paylaşamaz; bunun yerine yazdığı günlük dosyasını satır satır takip ederiz.</summary>
    private static async Task TailAsync(string file, Process p, Action<string>? log, CancellationToken ct)
    {
        long pos = 0;
        var decoder = Encoding.UTF8.GetDecoder();
        var pending = new StringBuilder();
        while (true)
        {
            var exited = p.HasExited;
            if (File.Exists(file))
            {
                await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(pos, SeekOrigin.Begin);
                var bytes = new byte[fs.Length - pos];
                pos += await fs.ReadAsync(bytes, ct);
                var chars = new char[decoder.GetCharCount(bytes, 0, bytes.Length)];
                decoder.GetChars(bytes, 0, bytes.Length, chars, 0);
                pending.Append(chars).Replace("﻿", "").Replace("\r", "");
                var text = pending.ToString();
                var cut = text.LastIndexOf('\n');
                if (cut >= 0)
                {
                    foreach (var line in text[..cut].Split('\n')) log?.Invoke(line);
                    pending.Remove(0, cut + 1);
                }
            }
            if (exited) break;
            await Task.Delay(250, ct);
        }
        if (pending.Length > 0) log?.Invoke(pending.ToString());
    }
}
