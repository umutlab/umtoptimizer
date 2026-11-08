using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace UMToptimizer.Core;

/// <summary>Uygulamanın tüm dosya yolları ve kalıcı verileri tek yerde.</summary>
public static class Store
{
    public static string Root { get; } = Dir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UMToptimizer");
    public static string Logs => Dir(Root, "logs");
    public static string AndroidSnapshots => Dir(Root, "android");
    public static string AdbPathFile => Path.Combine(Root, "adb-path.txt");
    public static string BundledAdbDir => Path.Combine(Root, "platform-tools");

    /// <summary>Windows betiğinin yazdığı yedek (yönetici olarak ProgramData'ya yazılır).</summary>
    public static string WindowsBackup => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UMToptimizer", "backup.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static T? Read<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default; }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return default; } // kilitli/bozuk dosya UI'ı düşürmesin
    }

    public static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));

    public static string NewLog(string name) => Path.Combine(Logs, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    public static void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    private static string Dir(params string[] parts) => Directory.CreateDirectory(Path.Combine(parts)).FullName;
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
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin is not null,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        var output = new StringBuilder();
        void Line(string? l) { if (l is null) return; lock (output) output.AppendLine(l); onLine?.Invoke(l); }
        p.OutputDataReceived += (_, e) => Line(e.Data);
        p.ErrorDataReceived += (_, e) => Line(e.Data);

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (stdin is not null)
        {
            // Cihaz kabuğu yalnızca \n bekler; BOM'suz UTF-8 yaz.
            try
            {
                await using var w = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false)) { NewLine = "\n" };
                await w.WriteAsync(stdin.AsMemory(), ct);
            }
            // Süreç erken çıktıysa (ör. "device offline") boru kapanır; gerçek nedeni çıktıdan göstermek için yut.
            catch (IOException) { }
        }
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        return new ProcResult(p.ExitCode, output.ToString());
    }
}
