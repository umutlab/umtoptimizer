using System.Diagnostics;

namespace UMToptimizer.Services;

public sealed record ProcessResult(int ExitCode, string Output, string Error);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var start = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception ex) { throw new InvalidOperationException($"'{fileName}' başlatılamadı: {ex.Message}", ex); }
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }
}
