using System.IO;
using System.Diagnostics;
using System.Text;

namespace UMToptimizer.Services;

public sealed record ProcessResult(int ExitCode, string Output, string Error);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string?>? environment = null,
        IProgress<string>? progress = null)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (key, value) in environment) start.Environment[key] = value;

        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception ex) { throw new InvalidOperationException($"'{fileName}' başlatılamadı: {ex.Message}", ex); }

        var stdout = ReadLinesAsync(process.StandardOutput, null, progress, cancellationToken);
        var stderr = ReadLinesAsync(process.StandardError, "[stderr] ", progress, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdout, stderr);
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static async Task<string> ReadLinesAsync(StreamReader reader, string? prefix, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            output.AppendLine(line);
            progress?.Report(prefix is null ? line : prefix + line);
        }
        return output.ToString();
    }
}