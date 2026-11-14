using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UMToptimizer.Core;

/// <summary>UAD listesindeki kaldırma güvenliği. Unlisted: paket cihazda var ama listede yok (ne olduğu bilinmiyor).</summary>
public enum Removal { Recommended, Advanced, Expert, Unsafe, Unlisted }

/// <summary>Universal Android Debloater listesindeki bir paketin açıklaması.</summary>
public sealed record UadEntry(string List, string Description, Removal Removal, IReadOnlyList<string> Dependencies, IReadOnlyList<string> NeededBy);

/// <summary>Cihazdaki bir sistem paketi. Removed: kullanıcı 0 için kaldırılmış ama APK'sı duruyor (geri getirilebilir).</summary>
public sealed record DevicePackage(string Name, bool Removed);

public sealed record DebloatResult(int Total, IReadOnlyList<string> Done, IReadOnlyList<string> Failed, string LogPath);

/// <summary>Bu uygulamayla kaldırılan paketlerin cihaz başına kaydı (geri getirme listesinde öne çıkarmak için).</summary>
public sealed class DebloatHistory
{
    public string Serial { get; set; } = "";
    public Dictionary<string, DateTimeOffset> Removed { get; set; } = [];
}

/// <summary>
/// Sistem uygulamalarını root'suz kaldırır ve geri getirir. "pm uninstall --user 0" paketi yalnızca ana kullanıcıdan
/// kaldırır; APK sistem bölümünde kalır, bu yüzden "install-existing" ile her zaman geri gelir.
/// Yalnızca sistem paketleri listelenir: kullanıcının kendi kurduğu uygulamalar kaldırılınca APK da silinir ve geri getirilemez.
/// </summary>
public static partial class Debloat
{
    [GeneratedRegex(@"^[A-Za-z0-9_.]{1,255}$")] private static partial Regex PackageRx();

    /// <summary>Paket adı cihaz kabuğuna tırnaksız gider; yalnızca harf, rakam, nokta ve alt çizgiye izin verilir.</summary>
    public static bool IsSafePackage(string? name) => name is not null && PackageRx().IsMatch(name);

    // ---------- UAD kataloğu ----------

    private static readonly Lazy<IReadOnlyDictionary<string, UadEntry>> LazyCatalog = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("uad_lists.json")
            ?? throw new InvalidOperationException("Gömülü UAD listesi bulunamadı.");
        return ParseCatalog(stream);
    });

    /// <summary>Paket adı → açıklama. İlk erişimde gömülü JSON'dan yüklenir (arka plan iş parçacığında çağrılmalı).</summary>
    public static IReadOnlyDictionary<string, UadEntry> Catalog => LazyCatalog.Value;

    internal static Dictionary<string, UadEntry> ParseCatalog(Stream json)
    {
        static string Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static string[] Names(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(IsSafePackage).ToArray()
                : [];

        using var doc = JsonDocument.Parse(json);
        var catalog = new Dictionary<string, UadEntry>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (!IsSafePackage(p.Name) || p.Value.ValueKind != JsonValueKind.Object) continue;
            // Tanınmayan güvenlik düzeyi en temkinli hâliyle (Güvensiz) kabul edilir.
            var removal = Enum.TryParse<Removal>(Text(p.Value, "removal"), ignoreCase: true, out var r) && r != Removal.Unlisted ? r : Removal.Unsafe;
            catalog[p.Name] = new(Text(p.Value, "list"), Text(p.Value, "description").Trim(), removal, Names(p.Value, "dependencies"), Names(p.Value, "neededBy"));
        }
        return catalog;
    }

    // ---------- Tarama ----------

    /// <summary>Cihazdaki sistem paketlerini ve hangilerinin kullanıcı 0 için kaldırılmış olduğunu okur.</summary>
    public static async Task<List<DevicePackage>> ScanAsync(string serial, CancellationToken ct = default)
    {
        var r = await Adb.ShellAsync(serial,
            "echo @@all; pm list packages -s -u --user 0 </dev/null\necho @@now; pm list packages -s --user 0 </dev/null\necho @@done\nexit\n", ct: ct);
        if (!r.Output.Contains("@@done", StringComparison.Ordinal))
            throw new UserException("Cihazdaki paketler okunamadı. Kabloyu ve telefondaki USB hata ayıklama iznini kontrol et.");
        var packages = ParseScan(r.Output);
        // Her Android cihazda kurulu sistem paketi vardır; hiç yoksa okuma güvenilmezdir (her şey "kaldırılmış" görünürdü).
        if (!packages.Any(p => !p.Removed))
            throw new UserException("Cihazdaki paket listesi tam okunamadı. Kabloyu çıkarıp takıp tekrar dene.");
        return packages;
    }

    /// <summary>Saf fonksiyon: "@@all" (kaldırılmışlar dahil) ve "@@now" (şu an kurulu) bölümlerinden paket listesini üretir.</summary>
    internal static List<DevicePackage> ParseScan(string output)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        var now = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string>? section = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("@@", StringComparison.Ordinal)) { section = line switch { "@@all" => all, "@@now" => now, _ => null }; continue; }
            if (section is null || !line.StartsWith("package:", StringComparison.Ordinal)) continue;
            var name = line[8..].Trim();
            if (IsSafePackage(name)) section.Add(name);
        }
        all.UnionWith(now); // "-u" desteklemeyen eski sürümlerde bile kurulu paketler listelenir
        return all.OrderBy(n => n, StringComparer.Ordinal).Select(n => new DevicePackage(n, !now.Contains(n))).ToList();
    }

    // ---------- Kaldırma / geri getirme ----------

    internal static string Command(string package, bool restore) =>
        restore ? $"cmd package install-existing --user 0 {package}" : $"pm uninstall --user 0 {package}";

    /// <summary>Saf fonksiyon: pm çıktısı başarı mı? (Eski Android sürümleri hata durumunda da çıkış kodu 0 döner; metne bakılır.)</summary>
    internal static bool IsSuccess(bool restore, string output) =>
        restore ? output.Contains("installed for user", StringComparison.OrdinalIgnoreCase) : output.Contains("Success", StringComparison.Ordinal);

    /// <summary>Paketleri tek ADB oturumunda kaldırır (restore=false) veya geri getirir (restore=true).</summary>
    public static async Task<DebloatResult> RunAsync(string serial, IReadOnlyList<string> packages, bool restore,
        Action<int, int>? progress = null, Action<string>? log = null, CancellationToken ct = default)
    {
        var list = packages.Distinct(StringComparer.Ordinal).ToList();
        if (list.Count == 0) throw new UserException("Hiç paket seçilmedi.");
        if (!list.All(IsSafePackage)) throw new UserException("Geçersiz paket adı; hiçbir değişiklik yapılmadı.");

        // Rastgele işaret: pm'in kendi çıktısı sonuç satırı gibi görünemez. Çıktı tek satıra indirilir (tr).
        var mark = "@@" + Random.Shared.Next().ToString("x8");
        var script = new StringBuilder();
        for (var i = 0; i < list.Count; i++)
            script.Append($"r=$({Command(list[i], restore)} </dev/null 2>&1 | tr '\\n' ' '); echo \"{mark}:{i}:$r\"\n");
        script.Append($"echo {mark}:END\nexit\n");

        var done = new List<string>();
        var failed = new List<string>();
        var finished = false;
        ProcResult result;
        try
        {
            result = await Adb.ShellAsync(serial, script.ToString(), line =>
            {
                if (!line.StartsWith(mark, StringComparison.Ordinal)) return;
                var tag = line[(mark.Length + 1)..];
                if (tag == "END") { finished = true; return; }
                var colon = tag.IndexOf(':');
                if (colon <= 0 || !int.TryParse(tag.AsSpan(0, colon), out var i) || (uint)i >= (uint)list.Count) return;
                var message = tag[(colon + 1)..].Trim();
                if (IsSuccess(restore, message)) { lock (done) done.Add(list[i]); log?.Invoke("✓ " + list[i]); }
                else { lock (done) failed.Add(list[i]); log?.Invoke($"✗ {list[i]}  —  {(message.Length > 0 ? message : "yanıt yok")}"); }
                progress?.Invoke(i + 1, list.Count);
            }, ct);
        }
        finally
        {
            // İptal veya kopma olsa bile o ana kadar kaldırılanlar kayda geçer.
            List<string> snapshot;
            lock (done) snapshot = [.. done];
            Record(serial, snapshot, restore);
        }

        var logPath = "";
        try
        {
            logPath = AppPaths.NewLog(restore ? "debloat-restore" : "debloat-remove");
            await File.WriteAllTextAsync(logPath,
                $"İşlem: {(restore ? "geri getirme" : "kaldırma")}\nCihaz: {serial}\nBaşarılı: {done.Count}/{list.Count}\n\n" +
                $"{string.Join('\n', done.Select(p => "OK: " + p))}\n{string.Join('\n', failed.Select(p => "FAILED: " + p))}\n\n--- ÇIKTI ---\n{result.Output}", ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* günlük yazılamadı diye işlem başarısız sayılmaz */ }

        if (!finished)
            throw new UserException("Cihazla bağlantı yarıda kesildi (kablo çıkmış olabilir). Paketleri yeniden tarayıp kalanları tekrar dene.");
        return new(list.Count, done, failed, logPath);
    }

    // ---------- Geçmiş ----------

    internal static string HistoryFile(string serial)
    {
        var safe = string.Concat(serial.Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_'));
        return Path.Combine(AppPaths.Debloat, $"{safe}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serial)), 0, 4).ToLowerInvariant()}.json");
    }

    /// <summary>Bu uygulamayla kaldırılmış paketler ve kaldırılma zamanları. Dosya yoksa/bozuksa boş döner.</summary>
    public static IReadOnlyDictionary<string, DateTimeOffset> History(string serial) =>
        Json.Read<DebloatHistory>(HistoryFile(serial)) is { Removed: { } removed } h && h.Serial == serial
            ? removed.Where(kv => IsSafePackage(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)
            : new Dictionary<string, DateTimeOffset>();

    private static void Record(string serial, List<string> packages, bool restore)
    {
        if (packages.Count == 0) return;
        try
        {
            var removed = new Dictionary<string, DateTimeOffset>(History(serial), StringComparer.Ordinal);
            foreach (var p in packages)
                if (restore) removed.Remove(p); else removed[p] = DateTimeOffset.Now;
            Json.Write(HistoryFile(serial), new DebloatHistory { Serial = serial, Removed = removed });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Kayıt yalnızca kolaylık: gerçek durum cihazdan okunur, paketler yine de "Kaldırılmış" listesinde görünür.
            CrashLog.Write("Debloat.Record", e);
        }
    }
}
