using System.Diagnostics;
using System.Globalization;
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
    public string Name => Model.Length > 0 ? Model.Replace('_', ' ') : Serial;
    public string Display => $"{Name}  ·  {State switch
    {
        "device" => "hazır",
        "unauthorized" => "telefonda USB hata ayıklama iznini onayla",
        "offline" => "çevrimdışı",
        _ => State,
    }}";

    internal static List<AdbDevice> ParseAll(string text) =>
        text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Parse).OfType<AdbDevice>().ToList();

    /// <summary>"serial  device product:x model:y ..." (boşluk) veya "serial\tdevice" (sekme) satırını çözer.</summary>
    public static AdbDevice? Parse(string line)
    {
        var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length < 2 || line.StartsWith("List of", StringComparison.Ordinal) || line.StartsWith('*') || !Adb.IsSafeSerial(t[0])) return null;
        return new(t[0], t[1], t.Skip(2).FirstOrDefault(x => x.StartsWith("model:", StringComparison.Ordinal))?[6..] ?? "");
    }
}

/// <summary>adb.exe'yi bulma, kurma, cihazları izleme ve komut çalıştırma.</summary>
public static partial class Adb
{
    public const string PlatformToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
    private const long MaxDownloadBytes = 200L * 1024 * 1024;

    private static volatile string? _path;

    /// <summary>Bulunan adb.exe (önbellekli). Bulunamadıysa her erişimde yeniden aranır; arayüz bunu arka planda çağırır.</summary>
    public static string? Path
    {
        get
        {
            var path = _path;
            if (path is not null && !File.Exists(path)) path = null; // adb.exe silinmiş/taşınmış: eski yol kullanılmaz
            return _path = path ?? Locate();
        }
    }

    /// <summary>Önbelleği atıp yeniden arar (ADB kurulduktan/seçildikten sonra).</summary>
    public static string? Refresh() => _path = Locate();

    private static string Exe => Path ?? throw new UserException("ADB bulunamadı. Android sayfasındaki \"ADB'yi indir\" düğmesini kullan.");

    [GeneratedRegex(@"^[A-Za-z0-9._:\[\]%\-]{1,128}$")] private static partial Regex SerialRx();

    /// <summary>Seri numarası adb'ye argüman olarak gider; '-' ile başlayan veya garip karakterli değerler reddedilir.</summary>
    public static bool IsSafeSerial(string? serial) => serial is { Length: > 0 } && serial[0] != '-' && SerialRx().IsMatch(serial);

    private static string Checked(string serial) =>
        IsSafeSerial(serial) ? serial : throw new UserException("Geçersiz cihaz seri numarası.");

    // ---------- Bulma / kurma ----------

    private static string? Locate()
    {
        static string Tools(string? root) => string.IsNullOrWhiteSpace(root) ? "" : IOPath.Combine(root, "platform-tools", "adb.exe");
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string saved;
            try { saved = File.Exists(AppPaths.AdbPathFile) ? File.ReadAllText(AppPaths.AdbPathFile).Trim() : ""; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { saved = ""; }

            string?[] candidates =
            [
                Environment.GetEnvironmentVariable("UMTOPT_ADB"),
                saved,
                IOPath.Combine(AppPaths.AdbDir, "adb.exe"),
                Tools(AppContext.BaseDirectory),
                Tools(Environment.GetEnvironmentVariable("ANDROID_HOME")),
                Tools(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")),
                Tools(IOPath.Combine(local, "Android", "Sdk")),
                .. (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => d.Trim().Trim('"'))
                    .Where(d => d.Length > 0 && d.IndexOfAny(IOPath.GetInvalidPathChars()) < 0)
                    .Select(d => IOPath.Combine(d, "adb.exe")),
            ];
            // Göreli yollar çalışma klasörüne göre çözülür (başkasının bıraktığı adb.exe çalışabilir); yalnızca tam yollar kabul edilir.
            return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && IOPath.IsPathFullyQualified(c) && File.Exists(c));
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public static void SavePath(string file)
    {
        if (!File.Exists(file) || !IOPath.GetFileName(file).Equals("adb.exe", StringComparison.OrdinalIgnoreCase))
            throw new UserException("Seçilen dosya adb.exe değil.");
        var full = IOPath.GetFullPath(file);
        try { File.WriteAllText(AppPaths.AdbPathFile, full); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new UserException("ADB konumu kaydedilemedi.", e); }
        _path = full;
    }

    /// <summary>
    /// Resmi Google Platform Tools paketini indirir. Yeni sürüm ayrı bir klasöre açılır ve ancak
    /// adb.exe doğrulandıktan sonra eskisinin yerine taşınır; yarıda kalan indirme mevcut ADB'yi bozmaz.
    /// </summary>
    public static async Task InstallAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var work = IOPath.Combine(AppPaths.Root, ".adb-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var zip = IOPath.Combine(work, "platform-tools.zip");
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                using var response = await http.GetAsync(PlatformToolsUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) throw new HttpRequestException("HTTPS dışı yönlendirme");
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(zip);
                var buffer = new byte[81920];
                long done = 0;
                for (int n; (n = await input.ReadAsync(buffer, ct)) > 0;)
                {
                    if ((done += n) > MaxDownloadBytes) throw new InvalidDataException("Paket beklenenden büyük.");
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    if (total > 0) progress?.Report(Math.Min(1, done / (double)total));
                }
            }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException
                                      || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                throw new UserException("ADB indirilemedi. İnternet bağlantını kontrol edip tekrar dene.", e);
            }

            var fresh = IOPath.Combine(work, "platform-tools"); // arşiv "platform-tools/" klasörü içerir
            try { ZipFile.ExtractToDirectory(zip, work); }
            catch (Exception e) when (e is InvalidDataException or IOException) { throw new UserException("İndirilen ADB paketi bozuk çıktı. Tekrar dene.", e); }
            if (!File.Exists(IOPath.Combine(fresh, "adb.exe"))) throw new UserException("İndirilen pakette adb.exe yok. Tekrar dene.");

            try
            {
                if (Directory.Exists(AppPaths.AdbDir))
                {
                    await StopServerAsync(IOPath.Combine(AppPaths.AdbDir, "adb.exe"));
                    var old = AppPaths.AdbDir + ".old-" + Guid.NewGuid().ToString("N");
                    Directory.Move(AppPaths.AdbDir, old);
                    try { Directory.Move(fresh, AppPaths.AdbDir); }
                    catch { Directory.Move(old, AppPaths.AdbDir); throw; }
                    TryDelete(old);
                }
                else Directory.Move(fresh, AppPaths.AdbDir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new UserException("ADB güncellenemedi; adb.exe başka bir program tarafından kullanılıyor olabilir. Mevcut sürüm korunuyor.", e);
            }
            Refresh();
        }
        finally { TryDelete(work); }
    }

    private static async Task StopServerAsync(string adb)
    {
        if (!File.Exists(adb)) return;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Proc.RunAsync(adb, ["kill-server"], ct: limit.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or UserException) { /* en iyi çaba */ }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* artık klasör zararsız */ }
    }

    // ---------- Komut / cihazlar ----------

    public static Task<ProcResult> RunAsync(IEnumerable<string> args, string? stdin = null, Action<string>? onLine = null, CancellationToken ct = default)
        => Proc.RunAsync(Exe, args, stdin, onLine, ct);

    /// <summary>Belirli cihazın kabuğunda stdin'den betik çalıştırır.</summary>
    public static Task<ProcResult> ShellAsync(string serial, string script, Action<string>? onLine = null, CancellationToken ct = default)
        => RunAsync(["-s", Checked(serial), "shell"], script, onLine, ct);

    public static async Task<List<AdbDevice>> DevicesAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["devices", "-l"], ct: ct);
        if (r.ExitCode != 0) throw new UserException("ADB cihaz listesi alınamadı. Kabloyu çıkarıp takmayı dene.");
        return AdbDevice.ParseAll(r.Output);
    }

    public static async Task RebootAsync(string serial, CancellationToken ct = default)
    {
        var r = await RunAsync(["-s", Checked(serial), "reboot"], ct: ct);
        if (r.ExitCode != 0) throw new UserException("Cihaz yeniden başlatılamadı. Bağlantıyı kontrol et.");
    }

    /// <summary>
    /// Sürekli yoklama yerine "adb track-devices" akışını dinler: ADB her değişiklikte 4 haneli hex uzunluk + liste gönderir.
    /// Uzunluk BAYT cinsindendir; bu yüzden akış bayt olarak okunur (model adında ASCII dışı karakter çerçeveyi bozmasın).
    /// Akış koparsa birkaç saniye sonra yeniden bağlanır. Asla istisna fırlatmaz; iptal edilince döner.
    /// </summary>
    public static async Task WatchAsync(Action<IReadOnlyList<AdbDevice>> onChange, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Path is { } adb)
                {
                    // İlk durum; track-devices desteklemeyen eski ADB için de yoklama görevi görür.
                    // Süre sınırı: sunucu süreci boruları açık tutarsa izleyici sonsuza kadar takılmasın.
                    using (var limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        limit.CancelAfter(TimeSpan.FromSeconds(20));
                        try { onChange(await DevicesAsync(limit.Token).ConfigureAwait(false)); }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new UserException("ADB yanıt vermedi."); }
                    }
                    // stderr yönlendirilmez: okunmayan bir boru dolarsa adb yazarken kilitlenir.
                    var psi = new ProcessStartInfo(adb) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    psi.ArgumentList.Add("track-devices");
                    psi.ArgumentList.Add("-l");
                    using var p = Process.Start(psi);
                    if (p is not null)
                    {
                        await using var _ = ct.Register(() => Kill(p));
                        try
                        {
                            var stream = p.StandardOutput.BaseStream;
                            var head = new byte[4];
                            while (await ReadExactAsync(stream, head, ct))
                            {
                                if (!int.TryParse(Encoding.ASCII.GetString(head), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var length)) break;
                                var body = new byte[length];
                                if (!await ReadExactAsync(stream, body, ct)) break;
                                onChange(AdbDevice.ParseAll(Encoding.UTF8.GetString(body)));
                            }
                        }
                        finally { Kill(p); }
                    }
                }
                else onChange([]);
                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                // Geçici USB/sunucu hataları: kullanıcıya gösterilmez, yeniden denenir. Eski cihaz listesi ekranda kalmasın.
                if (e is not UserException) CrashLog.Write("Adb.WatchAsync", e);
                try { Refresh(); onChange([]); } catch (Exception inner) { CrashLog.Write("Adb.WatchAsync.onChange", inner); }
                try { await Task.Delay(3000, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private static void Kill(Process p)
    {
        try { if (!p.HasExited) p.Kill(); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buffer, CancellationToken ct)
    {
        for (int read = 0, n; read < buffer.Length; read += n)
            if ((n = await s.ReadAsync(buffer.AsMemory(read), ct)) == 0) return false;
        return true;
    }
}
