using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using IOPath = System.IO.Path;

namespace UMToptimizer.Core;

public sealed record AdbDevice(string Serial, string State, string Model)
{
    public bool Ready => State == "device";
    public string Display => $"{(Model.Length > 0 ? Model.Replace('_', ' ') : Serial)}  ·  {State switch
    {
        "device" => "hazır",
        "unauthorized" => "telefonda USB hata ayıklama iznini onayla",
        "offline" => "çevrimdışı",
        _ => State
    }}";

    /// <summary>"serial  device product:x model:y ..." veya "serial\tdevice" satırını çözer.</summary>
    internal static List<AdbDevice> ParseAll(string text) =>
        text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Parse).OfType<AdbDevice>().ToList();

    public static AdbDevice? Parse(string line)
    {
        var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length < 2 || line.StartsWith("List of") || line.StartsWith('*')) return null;
        return new(t[0], t[1], t.Skip(2).FirstOrDefault(x => x.StartsWith("model:"))?[6..] ?? "");
    }
}

/// <summary>Cihazdaki bir ayarın uygulama öncesi değeri. Value=null → ayar önceden yoktu.</summary>
public sealed record Setting(string Kind, string Ns, string Key, string? Value)
{
    public string Id => $"{Kind}|{Ns}|{Key}";
}

/// <summary>Cihaz başına ilk (orijinal) değerleri tutar; art arda profil uygulansa da ilk değer korunur.</summary>
public sealed class AndroidSnapshot
{
    public string Serial { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public List<string> Profiles { get; set; } = [];
    public Dictionary<string, Setting> Values { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => $"{string.Join(", ", Profiles)}  ·  {Values.Count:N0} ayar yedekli  ·  {Created.LocalDateTime:g}";
}

public sealed record ApplyResult(int Total, IReadOnlyList<string> Failed, int Undoable, string LogPath);

public static partial class Adb
{
    public const string PlatformToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

    private static string? _path;
    public static string? Path => _path ??= Locate();
    private static string Exe => Path ?? throw new InvalidOperationException("ADB bulunamadı. 'ADB'yi indir' düğmesini kullanın veya adb.exe konumunu seçin.");

    // ---------- ADB'yi bulma / kurma ----------

    private static string? Locate()
    {
        static string Tools(string? root) => string.IsNullOrWhiteSpace(root) ? "" : IOPath.Combine(root, "platform-tools", "adb.exe");
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("UMTOPT_ADB"),
            File.Exists(Store.AdbPathFile) ? File.ReadAllText(Store.AdbPathFile).Trim() : null,
            IOPath.Combine(Store.BundledAdbDir, "adb.exe"),
            Tools(AppContext.BaseDirectory),
            Tools(Environment.GetEnvironmentVariable("ANDROID_HOME")),
            Tools(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")),
            Tools(IOPath.Combine(local, "Android", "Sdk")),
            .. (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => IOPath.Combine(d.Trim('"'), "adb.exe")),
        ];
        return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c)) is { } hit ? IOPath.GetFullPath(hit) : null;
    }

    public static void SavePath(string file)
    {
        if (!File.Exists(file) || !IOPath.GetFileName(file).Equals("adb.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Seçilen dosya adb.exe değil.", file);
        File.WriteAllText(Store.AdbPathFile, IOPath.GetFullPath(file));
        _path = IOPath.GetFullPath(file);
    }

    /// <summary>Resmi Google Platform Tools paketini %LOCALAPPDATA%\UMToptimizer altına indirir.</summary>
    public static async Task InstallPlatformToolsAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var zip = IOPath.Combine(IOPath.GetTempPath(), $"umt-platform-tools-{Guid.NewGuid():N}.zip");
        try
        {
            using (var http = new HttpClient())
            using (var response = await http.GetAsync(PlatformToolsUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(zip);
                var buffer = new byte[81920];
                long done = 0;
                for (int n; (n = await input.ReadAsync(buffer, ct)) > 0;)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    if (total > 0) progress?.Report((done += n) / (double)total);
                }
            }
            if (Directory.Exists(Store.BundledAdbDir)) Directory.Delete(Store.BundledAdbDir, true);
            ZipFile.ExtractToDirectory(zip, Store.Root); // arşiv "platform-tools/" klasörü içerir
            _path = null;
        }
        finally { File.Delete(zip); }
    }

    // ---------- Cihazlar ----------

    public static Task<ProcResult> RunAsync(IEnumerable<string> args, string? stdin = null, Action<string>? onLine = null, CancellationToken ct = default)
        => Proc.RunAsync(Exe, args, stdin, onLine, ct);

    public static async Task<List<AdbDevice>> DevicesAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["devices", "-l"], ct: ct);
        if (r.ExitCode != 0) throw new InvalidOperationException("ADB cihaz listesi alınamadı: " + r.Output.Trim());
        return ParseDevices(r.Output);
    }

    private static List<AdbDevice> ParseDevices(string text) => AdbDevice.ParseAll(text);

    /// <summary>
    /// Sürekli yoklama yerine "adb track-devices" akışını dinler: ADB her değişiklikte 4 haneli hex uzunluk + liste gönderir.
    /// Akış koparsa (ADB yok, sunucu yeniden başladı) birkaç saniye sonra tekrar bağlanır.
    /// </summary>
    public static async Task WatchAsync(Action<List<AdbDevice>> onChange, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Path is { } adb)
            {
                try
                {
                    onChange(await DevicesAsync(ct)); // ilk durum (track-devices desteklemeyen eski ADB için de yedek)
                    // stderr yönlendirilmez: okunmayan bir boru dolarsa adb yazarken kilitlenir.
                    var psi = new ProcessStartInfo(adb, "track-devices -l") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    using var p = Process.Start(psi)!;
                    using var _ = ct.Register(() => { try { p.Kill(); } catch { } });
                    var head = new char[4];
                    while (await ReadExactAsync(p.StandardOutput, head, ct))
                    {
                        var body = new char[Convert.ToInt32(new string(head), 16)];
                        if (!await ReadExactAsync(p.StandardOutput, body, ct)) break;
                        onChange(ParseDevices(new string(body)));
                    }
                }
                catch (OperationCanceledException) { return; }
                catch { /* geçici USB/sunucu hataları: yeniden dene */ }
            }
            try { await Task.Delay(3000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private static async Task<bool> ReadExactAsync(StreamReader r, char[] buffer, CancellationToken ct)
    {
        for (int read = 0, n; read < buffer.Length; read += n)
            if ((n = await r.ReadAsync(buffer.AsMemory(read), ct)) == 0) return false;
        return true;
    }

    public static async Task RebootAsync(string serial)
    {
        var r = await RunAsync(["-s", serial, "reboot"]);
        if (r.ExitCode != 0) throw new InvalidOperationException("Cihaz yeniden başlatılamadı: " + r.Output.Trim());
    }

    // ---------- Profil uygulama (tek kabuk oturumu) ----------

    [GeneratedRegex(@"^settings put (global|system|secure) (\S+)")] private static partial Regex SettingRx();
    [GeneratedRegex(@"^setprop (\S+)")] private static partial Regex PropRx();
    [GeneratedRegex(@"^(?:cmd )?device_config put (\S+) (\S+)")] private static partial Regex ConfigRx();

    /// <summary>Komutun değiştirdiği ayarı tanır (geri alınabilir komutlar).</summary>
    internal static Setting? Target(string cmd) =>
        SettingRx().Match(cmd) is { Success: true } s ? new("settings", s.Groups[1].Value, s.Groups[2].Value, null) :
        PropRx().Match(cmd) is { Success: true } p ? new("prop", "", p.Groups[1].Value, null) :
        ConfigRx().Match(cmd) is { Success: true } c ? new("config", c.Groups[1].Value, c.Groups[2].Value, null) : null;

    private static string SnapshotFile(string serial) =>
        IOPath.Combine(Store.AndroidSnapshots, string.Concat(serial.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')) + ".json");

    public static AndroidSnapshot? LoadSnapshot(string serial) => Store.Read<AndroidSnapshot>(SnapshotFile(serial));

    public static IEnumerable<AndroidSnapshot> AllSnapshots() =>
        Directory.EnumerateFiles(Store.AndroidSnapshots, "*.json").Select(Store.Read<AndroidSnapshot>).OfType<AndroidSnapshot>();

    /// <summary>
    /// Eski sürüm her satır için ayrı adb.exe süreci açıyordu (Balanced = ~1.500 süreç).
    /// Burada önce mevcut değerler 5 komutla yedeklenir, sonra tüm profil TEK "adb shell" oturumunda çalışır.
    /// </summary>
    public static async Task<ApplyResult> ApplyAsync(Profile profile, string serial, Action<int, int>? progress = null,
        Action<string>? log = null, CancellationToken ct = default)
    {
        var cmds = profile.Commands;
        var (undoable, added) = await SnapshotAsync(serial, profile, cmds, ct);

        var script = new StringBuilder();
        for (var i = 0; i < cmds.Count; i++)
            script.Append($"echo @@{i}\n{cmds[i]} </dev/null 2>&1 || echo @@F{i}\n"); // </dev/null: komut betiğin kalanını yutmasın
        script.Append("echo @@END\nexit\n");

        var failed = new List<string>();
        var r = await RunAsync(["-s", serial, "shell"], script.ToString(), line =>
        {
            if (line.StartsWith("@@F") && int.TryParse(line[3..], out var f)) { failed.Add(cmds[f]); log?.Invoke("✗ " + cmds[f]); }
            else if (line.StartsWith("@@") && int.TryParse(line[2..], out var i)) progress?.Invoke(i + 1, cmds.Count);
            else if (!line.StartsWith("@@") && line.Length > 0) log?.Invoke("  " + line);
        }, ct);

        var logPath = Store.NewLog($"android-{profile.Id}");
        await File.WriteAllTextAsync(logPath, $"Profil: {profile.Name}\nCihaz: {serial}\nÇıkış kodu: {r.ExitCode}\nBaşarısız: {failed.Count}/{cmds.Count}\n\n{string.Join('\n', failed.Select(f => "FAILED: " + f))}\n\n--- ÇIKTI ---\n{r.Output}", ct);
        if (!r.Output.Contains("@@END")) throw new InvalidOperationException($"ADB oturumu yarıda kesildi (kablo çıkmış olabilir). Günlük: {logPath}");

        // Cihazın reddettiği komutlar hiçbir şeyi değiştirmedi; onların yedeğini tutmak geri yüklemede boşuna hata üretir.
        var rejected = failed.Select(Target).OfType<Setting>().Select(t => t.Id).Where(added.Contains).ToHashSet();
        if (rejected.Count > 0 && LoadSnapshot(serial) is { } snap)
        {
            foreach (var id in rejected) snap.Values.Remove(id);
            Store.Write(SnapshotFile(serial), snap);
        }
        return new(cmds.Count, failed, undoable - rejected.Count, logPath);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.:/\-]+$")] private static partial Regex SafeToken();

    /// <summary>Saf fonksiyon (test edilebilir): "@@bölüm" işaretli settings/getprop/device_config çıktısını "tür|ad alanı|anahtar" → değer sözlüğüne çevirir.</summary>
    internal static Dictionary<string, string> ParseCurrent(string output)
    {
        var current = new Dictionary<string, string>();
        var section = "";
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("@@")) { section = line[2..]; continue; }
            if (section == "prop" && line.StartsWith('[') && line.EndsWith(']') && line.IndexOf("]: [", StringComparison.Ordinal) is var k and > 0)
                current[$"prop||{line[1..k]}"] = line[(k + 4)..^1]; // yalnızca son ']' atılır (değer ']' ile bitebilir)
            else if (section == "config" && line.IndexOf('/') is var s and > 0 && line.IndexOf('=') is var e and > 0 && e > s)
                current[$"config|{line[..s]}|{line[(s + 1)..e]}"] = line[(e + 1)..];
            else if (section is "global" or "system" or "secure" && line.IndexOf('=') is var eq and > 0)
                current[$"settings|{section}|{line[..eq]}"] = line[(eq + 1)..];
        }
        return current;
    }

    /// <summary>settings/getprop/device_config listelerini bir kerede okuyup değişecek anahtarların ilk değerini kaydeder.</summary>
    private static async Task<(int Undoable, HashSet<string> Added)> SnapshotAsync(string serial, Profile profile, IReadOnlyList<string> cmds, CancellationToken ct)
    {
        var snap = LoadSnapshot(serial) ?? new AndroidSnapshot { Serial = serial };
        var targets = cmds.Select(Target).ToList();
        var r = await RunAsync(["-s", serial, "shell"],
            "echo @@global; settings list global\necho @@system; settings list system\necho @@secure; settings list secure\n" +
            "echo @@prop; getprop\necho @@config; device_config list\nexit\n", ct: ct);
        if (r.ExitCode != 0 && !r.Output.Contains("@@config")) throw new InvalidOperationException("Cihaz ayarları okunamadı; hiçbir değişiklik yapılmadı.\n" + r.Output.Trim());

        var current = ParseCurrent(r.Output);
        var added = new HashSet<string>();
        foreach (var t in targets.OfType<Setting>())
            if (!snap.Values.ContainsKey(t.Id) && added.Add(t.Id))
                // Listede yoksa (veya settings "null" diyorsa) ayar önceden yoktu → geri yüklemede silinir. Boş değer "" olarak korunur.
                snap.Values[t.Id] = t with { Value = current.TryGetValue(t.Id, out var v) && !(t.Kind != "prop" && v == "null") ? v : null };
        if (!snap.Profiles.Contains(profile.Name)) snap.Profiles.Add(profile.Name);
        Store.Write(SnapshotFile(serial), snap);
        return (targets.Count(t => t is not null), added);
    }

    /// <summary>Saf fonksiyon (test edilebilir): yedekteki bir değeri geri yazan kabuk komutu. Güvensiz anahtar → null.</summary>
    public static string? RestoreCommand(Setting v)
    {
        // GÜVENLİK: anahtar/ad alanı yedek JSON'undan gelir (kullanıcının yazabildiği dosya) ve cihaz kabuğuna tırnaksız gider.
        // Yalnızca güvenli karakterlere izin ver; değerler her zaman tek tırnakla kaçışlanır.
        if (!SafeToken().IsMatch(v.Key) || (v.Ns.Length > 0 && !SafeToken().IsMatch(v.Ns))) return null;
        static string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";
        return (v.Kind, v.Value) switch
        {
            ("settings", null) => $"settings delete {v.Ns} {v.Key}",
            ("settings", _) => $"settings put {v.Ns} {v.Key} {Q(v.Value!)}",
            ("config", null) => $"device_config delete {v.Ns} {v.Key}",
            ("config", _) => $"device_config put {v.Ns} {v.Key} {Q(v.Value!)}",
            ("prop", _) => $"setprop {v.Key} {Q(v.Value ?? "")}",
            _ => null,
        };
    }

    /// <summary>Kaydedilmiş ilk değerleri geri yazar (önceden olmayan ayarları siler). Başarısız komut sayısını döner.</summary>
    public static async Task<int> RestoreAsync(string serial, Action<string>? log = null, CancellationToken ct = default)
    {
        var snap = LoadSnapshot(serial) ?? throw new InvalidOperationException("Bu cihaz için kayıtlı yedek yok.");
        var items = snap.Values.Values.Select(v => (Setting: v, Cmd: RestoreCommand(v))).ToList();
        var unsafeCount = items.Count(x => x.Cmd is null);
        if (unsafeCount > 0) log?.Invoke($"⚠ {unsafeCount} güvensiz/bozuk yedek girdisi atlandı.");
        items.RemoveAll(x => x.Cmd is null);

        var failed = new HashSet<int>();
        var script = string.Concat(items.Select((x, i) => $"{x.Cmd} </dev/null >/dev/null 2>&1 || echo @@F{i}\n")) + "echo @@END\nexit\n";
        var r = await RunAsync(["-s", serial, "shell"], script, line => { if (line.StartsWith("@@F") && int.TryParse(line[3..], out var i)) lock (failed) failed.Add(i); }, ct);

        // Yedek, YALNIZCA oturum sonuna kadar tamamlandıysa güncellenir; kablo çıkarsa hiçbir değer kaybolmaz.
        if (!r.Output.Contains("@@END"))
            throw new InvalidOperationException("ADB oturumu yarıda kesildi; yedek korunuyor, tekrar deneyin.\n" + r.Output.Trim());
        log?.Invoke($"{items.Count - failed.Count}/{items.Count} ayar geri yüklendi.");
        if (failed.Count == 0) File.Delete(SnapshotFile(serial));
        else
        {
            // Başarılı olanları çıkar, yalnızca başarısızları sakla (tekrar denenebilir)
            snap.Values = items.Where((_, i) => failed.Contains(i)).ToDictionary(x => x.Setting.Id, x => x.Setting);
            Store.Write(SnapshotFile(serial), snap);
        }
        return failed.Count;
    }
}
