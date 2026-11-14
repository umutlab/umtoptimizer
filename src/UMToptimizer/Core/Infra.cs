using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace UMToptimizer.Core;

/// <summary>
/// Kullanıcıya olduğu gibi gösterilebilecek, beklenen bir durum (cihaz yok, internet yok, izin reddedildi...).
/// Bunun dışındaki her istisna "beklenmeyen" sayılır: ayrıntısı günlüğe yazılır, kullanıcıya sade bir satır gösterilir.
/// </summary>
public sealed class UserException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Uygulamanın tüm dosya yolları tek yerde. Klasörler ilk erişimde oluşturulur.</summary>
public static class AppPaths
{
    /// <summary>Testler geçici bir klasöre yönlendirebilir.</summary>
    public static string Root { get; internal set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UMToptimizer");

    public static string Logs => Dir(Root, "logs");
    public static string Snapshots => Dir(Root, "android");
    public static string Debloat => Dir(Root, "debloat");
    public static string AdbPathFile => Path.Combine(Dir(Root), "adb-path.txt");
    public static string AdbDir => Path.Combine(Root, "platform-tools");

    /// <summary>Windows betiğinin yönetici olarak yazdığı klasör (yalnızca yöneticiler yazabilir).</summary>
    public static string WindowsData { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UMToptimizer");
    public static string WindowsBackup => Path.Combine(WindowsData, "backup.json");

    public static string NewLog(string name) => Path.Combine(Logs, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    private static string Dir(params string[] parts) => Directory.CreateDirectory(Path.Combine(parts)).FullName;
}

public static class Json
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Dosya yoksa, kilitliyse veya bozuksa default döner; çağıran asla istisna görmez.</summary>
    public static T? Read<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default; }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return default; }
    }

    /// <summary>Atomik yazım: önce geçici dosyaya yazar, sonra yerine taşır. Çökme anında yedek yarım kalmaz.</summary>
    public static void Write<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Beklenmeyen hataların yazıldığı günlük. Kendisi asla istisna fırlatmaz.</summary>
public static class CrashLog
{
    private static readonly object Gate = new();
    public static string File => Path.Combine(AppPaths.Logs, "errors.log");

    public static void Write(string where, Exception ex)
    {
        try
        {
            lock (Gate) System.IO.File.AppendAllText(File, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n{ex}\n\n");
        }
        catch { /* günlük yazılamıyorsa yapacak bir şey yok; uygulamayı düşürme */ }
    }
}

public sealed record ProcResult(int ExitCode, string Output);

public static class Proc
{
    /// <summary>Gizli pencerede süreç çalıştırır; stdout+stderr satırlarını canlı bildirir, isteğe bağlı stdin yazar.</summary>
    public static async Task<ProcResult> RunAsync(string file, IEnumerable<string> args, string? stdin = null,
        Action<string>? onLine = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        var output = new StringBuilder();
        void Line(string? l)
        {
            if (l is null) return;
            lock (output) output.AppendLine(l);
            onLine?.Invoke(l);
        }
        p.OutputDataReceived += (_, e) => Line(e.Data);
        p.ErrorDataReceived += (_, e) => Line(e.Data);

        try { p.Start(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new UserException($"{Path.GetFileName(file)} başlatılamadı.", e);
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            try
            {
                // Cihaz kabuğu yalnızca \n bekler; BOM'suz UTF-8 yaz. stdin her durumda kapatılır ki süreç girdi beklemesin.
                await using var w = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false)) { NewLine = "\n" };
                if (stdin is not null) await w.WriteAsync(stdin.AsMemory(), ct).ConfigureAwait(false);
            }
            catch (IOException) { /* süreç erken çıktı (ör. "device offline"); gerçek neden çıktıda */ }

            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // İptal hangi aşamada gelirse gelsin (stdin yazımı dahil) süreç geride bırakılmaz.
            try { p.Kill(entireProcessTree: true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { /* zaten çıkmış */ }
            throw;
        }
        lock (output) return new ProcResult(p.ExitCode, output.ToString());
    }
}
