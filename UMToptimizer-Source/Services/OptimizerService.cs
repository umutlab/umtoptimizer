using System.Text.RegularExpressions;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;
using System.Reflection;
using UMToptimizer.Models;

namespace UMToptimizer.Services;

public sealed class OptimizerService
{
    private readonly UndoStore _undo = new();
    private static readonly Regex SchemeGuid = new(@"Power Scheme GUID:\s*([0-9a-fA-F-]{36})", RegexOptions.Compiled);

    public async Task<IReadOnlyList<AndroidDevice>> GetAndroidDevicesAsync()
    {
        var result = await AdbAsync(["devices", "-l"]);
        EnsureSuccess(result, "ADB cihaz listesi alınamadı.");
        return result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(line => line.Trim())
            .Where(line => line.Contains('\t')).Select(line =>
            {
                var parts = line.Split('\t', 2);
                var state = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "unknown";
                return new AndroidDevice(parts[0].Trim(), state, parts[1].Trim());
            }).ToList();
    }

    public async Task ApplyAsync(OptimizationProfile profile, string? serial = null)
    {
        if (profile.Platform == PlatformKind.Windows) await ApplyWindowsAsync(profile);
        else if (profile.Commands.Count > 0) await ApplyAndroidScriptAsync(profile, serial ?? throw new InvalidOperationException("Android cihaz seri numarası seçilmedi."));
        else await ApplyAndroidAsync(profile, serial ?? throw new InvalidOperationException("Android cihaz seri numarası seçilmedi."));
    }

    private async Task ApplyAndroidScriptAsync(OptimizationProfile profile, string serial)
    {
        var devices = await GetAndroidDevicesAsync();
        if (!devices.Any(d => d.Serial.Equals(serial, StringComparison.Ordinal) && d.State == "device"))
            throw new InvalidOperationException("Seçilen ADB cihazı bağlı ve yetkilendirilmiş değil. Hiçbir profil komutu çalıştırılmadı.");

        var record = new UndoRecord
        {
            Platform = "Android", Profile = profile.Name, CreatedAt = DateTimeOffset.Now,
            AndroidDeviceSerial = serial, CanUndo = false,
            Notes = "Bu profil, kaynak betikteki tüm ADB komutlarını çalıştırdı. Bazı ayarlar/derleme işlemleri otomatik olarak geri alınamaz."
        };
        await _undo.SaveAsync(record);

        var workingDirectory = Path.Combine(Path.GetTempPath(), "UMToptimizer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var scriptPath = Path.Combine(workingDirectory, "profile.bat");
        var lines = new List<string> { "@echo off", "set \"UMTOPT_ERRORS=0\"" };
        for (var i = 0; i < profile.Commands.Count; i++)
        {
            var command = profile.Commands[i];
            lines.Add(command);
            lines.Add($"if errorlevel 1 echo UMTOPT_FAILED [{i + 1}/{profile.Commands.Count}]: {command}");
            lines.Add("if errorlevel 1 set /a UMTOPT_ERRORS+=1");
        }
        lines.Add("echo UMTOPT_RESULT: %UMTOPT_ERRORS% command(s) failed");
        lines.Add("exit /b %UMTOPT_ERRORS%");

        try
        {
            await File.WriteAllLinesAsync(scriptPath, lines, new UTF8Encoding(false));
            var adbPath = FindAdb();
            var env = new Dictionary<string, string?> { ["ANDROID_SERIAL"] = serial };
            if (Path.IsPathRooted(adbPath) && !string.IsNullOrWhiteSpace(Path.GetDirectoryName(adbPath)))
            {
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                env["PATH"] = Path.GetDirectoryName(adbPath) + Path.PathSeparator + currentPath;
            }
            var result = await ProcessRunner.RunAsync("cmd.exe", ["/d", "/c", $"call \"{scriptPath}\""], environment: env);
            if (result.ExitCode != 0)
            {
                var failures = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.StartsWith("UMTOPT_FAILED", StringComparison.OrdinalIgnoreCase)).Take(12);
                var details = string.Join(Environment.NewLine, failures);
                throw new InvalidOperationException($"Profil çalıştı; bir veya daha fazla ADB komutu hata verdi. Betik tüm satırları tamamlamaya çalıştı.\n{details}\n\nGeri Yükleme bölümünde bu profilin neden otomatik geri alınamadığı açıklanır.");
            }
        }
        finally
        {
            try { Directory.Delete(workingDirectory, recursive: true); } catch { }
        }
    }

    private async Task ApplyWindowsAsync(OptimizationProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.WindowsScriptMode))
        {
            var windowsRecord = new UndoRecord
            {
                Platform = "Windows", Profile = profile.Name, CreatedAt = DateTimeOffset.Now,
                WindowsScriptMode = profile.WindowsScriptMode,
                Notes = "Geri alma betiği, yedeklenmiş kayıt defteri ve hizmet ayarlarını geri yükler. Silinen uygulamalar/dosyalar ve hosts ekleri tam geri alınamayabilir."
            };
            await _undo.SaveAsync(windowsRecord);
            try { await RunWindowsScriptAsync(profile.WindowsScriptMode); }
            catch (OperationCanceledException) { await _undo.ClearAsync(); throw; }
            return;
        }

        var active = await ProcessRunner.RunAsync("powercfg", ["/getactivescheme"]);
        EnsureSuccess(active, "Windows güç planı okunamadı.");
        var match = SchemeGuid.Match(active.Output);
        if (!match.Success) throw new InvalidOperationException("Etkin güç planının GUID değeri okunamadı; hiçbir ayar değiştirilmedi.");
        var record = new UndoRecord { Platform = "Windows", Profile = profile.Name, CreatedAt = DateTimeOffset.Now, PreviousPowerPlan = match.Groups[1].Value };
        await _undo.SaveAsync(record);
        var target = profile.Actions.First(a => a.Kind == ProfileActionKind.PowerPlan).Key;
        var result = await ProcessRunner.RunAsync("powercfg", ["/setactive", target]);
        if (result.ExitCode != 0)
        {
            var rollback = await ProcessRunner.RunAsync("powercfg", ["/setactive", record.PreviousPowerPlan]);
            if (rollback.ExitCode != 0)
                throw new InvalidOperationException($"Yeni güç planı etkinleştirilemedi ve önceki plan geri yüklenemedi. Geri yükleme kaydı korundu.\nDeğişiklik hatası: {DescribeFailure(result)}\nGeri alma hatası: {DescribeFailure(rollback)}");
            await _undo.ClearAsync();
            EnsureSuccess(result, "Güç planı değiştirilemedi.");
        }
    }

    private async Task ApplyAndroidAsync(OptimizationProfile profile, string serial)
    {
        var record = new UndoRecord { Platform = "Android", Profile = profile.Name, CreatedAt = DateTimeOffset.Now, AndroidDeviceSerial = serial };
        foreach (var action in profile.Actions)
        {
            var result = await AdbAsync(["-s", serial, "shell", "settings", "get", "global", action.Key]);
            EnsureSuccess(result, $"'{action.Key}' değeri okunamadı; ayar uygulanmadı.");
            var previous = result.Output.Trim();
            record.AndroidSettings.Add(new AndroidSettingSnapshot("global", action.Key, previous.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : previous));
        }

        await _undo.SaveAsync(record);
        foreach (var action in profile.Actions)
        {
            var result = await AdbAsync(["-s", serial, "shell", "settings", "put", "global", action.Key, action.Value]);
            if (result.ExitCode != 0)
            {
                await RestoreAndroidAsync(record, serial);
                await _undo.ClearAsync();
                EnsureSuccess(result, $"'{action.Key}' ayarı uygulanamadı; değiştirilenler geri alındı.");
            }
        }
    }

    public async Task UndoAsync(string? serial = null, string? platform = null)
    {
        var record = await _undo.LoadAsync() ?? throw new InvalidOperationException("Geri alınacak bir işlem kaydı bulunamadı.");
        if (!record.CanUndo) throw new InvalidOperationException(record.Notes ?? "Son profil otomatik olarak geri alınamıyor.");
        if (!string.IsNullOrWhiteSpace(platform) && !record.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Son kayıt {record.Platform} platformuna ait. İstenen platform: {platform}.");
        if (record.Platform == "Windows")
        {
            if (!string.IsNullOrWhiteSpace(record.WindowsScriptMode))
            {
                await RunWindowsScriptAsync("Restore");
                await _undo.ClearAsync();
                return;
            }
            var result = await ProcessRunner.RunAsync("powercfg", ["/setactive", record.PreviousPowerPlan ?? throw new InvalidOperationException("Önceki güç planı kaydı eksik.")]);
            EnsureSuccess(result, "Önceki Windows güç planı geri yüklenemedi.");
        }
        else
        {
            var target = serial ?? record.AndroidDeviceSerial ?? throw new InvalidOperationException("Android geri yükleme için cihaz seri numarası gerekli.");
            if (!string.IsNullOrWhiteSpace(record.AndroidDeviceSerial) && !record.AndroidDeviceSerial.Equals(target, StringComparison.Ordinal))
                throw new InvalidOperationException($"Geri yükleme kaydı {record.AndroidDeviceSerial} cihazına ait. Başka cihazın ayarları değiştirilmedi.");
            await RestoreAndroidAsync(record, target);
        }
        await _undo.ClearAsync();
    }

    private async Task RestoreAndroidAsync(UndoRecord record, string serial)
    {
        foreach (var item in record.AndroidSettings.AsEnumerable().Reverse())
        {
            var args = item.PreviousValue is null
                ? new[] { "-s", serial, "shell", "settings", "delete", item.Namespace, item.Key }
                : new[] { "-s", serial, "shell", "settings", "put", item.Namespace, item.Key, item.PreviousValue };
            var result = await AdbAsync(args);
            EnsureSuccess(result, $"'{item.Key}' geri yüklenemedi.");
        }
    }

    private static async Task RunWindowsScriptAsync(string mode)
    {
        var resourceName = "UMToptimizer.Resources.Windows.UMToptimizer-Windows.ps1";
        await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Windows profil betiği uygulama kaynağında bulunamadı: {resourceName}");
        var workingDirectory = Path.Combine(Path.GetTempPath(), "UMToptimizer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var scriptPath = Path.Combine(workingDirectory, "UMToptimizer-Windows.ps1");
        try
        {
            await using (var output = File.Create(scriptPath)) await resource.CopyToAsync(output);
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell 5.1 bulunamadı.", powershell);
            var start = new ProcessStartInfo(powershell)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = workingDirectory
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(scriptPath);
            start.ArgumentList.Add("-Mode");
            start.ArgumentList.Add(mode);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Yükseltilmiş PowerShell işlemi başlatılamadı.");
            await process.WaitForExitAsync();
            if (process.ExitCode == 2) throw new OperationCanceledException("PowerShell betiğinde işlem iptal edildi.");
            if (process.ExitCode != 0) throw new InvalidOperationException($"Windows {mode} işlemi PowerShell'de hata kodu {process.ExitCode} ile sonlandı. Betiğin günlüğünü ve Geri yükleme bölümünü kontrol edin.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Yönetici izni verilmedi; Windows profili uygulanmadı.", ex);
        }
        finally
        {
            try { Directory.Delete(workingDirectory, recursive: true); } catch { }
        }
    }

    private static async Task<ProcessResult> AdbAsync(IEnumerable<string> arguments) => await ProcessRunner.RunAsync(FindAdb(), arguments);
    public static string? FindAdbPath()
    {
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable("UMTOPT_ADB");
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Path.Combine(localAppData, "UMToptimizer");
        var savedPath = Path.Combine(appData, "adb-path.txt");
        try { if (File.Exists(savedPath)) candidates.Add(File.ReadAllText(savedPath).Trim()); } catch { }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "adb.exe"));
        foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var sdk = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(sdk)) candidates.Add(Path.Combine(sdk, "platform-tools", "adb.exe"));
        }
        if (!string.IsNullOrWhiteSpace(localAppData)) candidates.Add(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe"));
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            candidates.Add(Path.Combine(userProfile, "AppData", "Local", "Android", "Sdk", "platform-tools", "adb.exe"));
            candidates.Add(Path.Combine(userProfile, "Android", "Sdk", "platform-tools", "adb.exe"));
        }
        candidates.Add(Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\", "Android", "platform-tools", "adb.exe"));
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(folder => Path.Combine(folder.Trim('"'), "adb.exe")));

        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return Path.GetFullPath(candidate);
        return null;
    }

    public static void SaveAdbPath(string path)
    {
        if (!File.Exists(path) || !Path.GetFileName(path).Equals("adb.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Seçilen dosya adb.exe değil veya bulunamadı.", path);
        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UMToptimizer");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "adb-path.txt"), Path.GetFullPath(path));
    }

    private static string FindAdb() => FindAdbPath() ?? throw new InvalidOperationException("ADB bulunamadı. Android Platform Tools'u kurun veya UMTOPT_ADB ortam değişkenini adb.exe dosyasına ayarlayın.");
    private static string DescribeFailure(ProcessResult result) => string.Join(" ", new[] { result.Error.Trim(), result.Output.Trim() }.Where(value => value.Length > 0));
    private static void EnsureSuccess(ProcessResult result, string message)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException($"{message} {result.Error.Trim()} {result.Output.Trim()}".Trim());
    }
}
