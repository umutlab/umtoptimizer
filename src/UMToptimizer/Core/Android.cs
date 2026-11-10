using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace UMToptimizer.Core;

/// <summary>Cihazdaki bir ayarın uygulama öncesi değeri. Value=null → ayar önceden yoktu.</summary>
public sealed record Setting(string Kind, string Ns, string Key, string? Value)
{
    [JsonIgnore] public string Id => $"{Kind}|{Ns}|{Key}";
}

/// <summary>Cihaz başına ilk (orijinal) değerleri tutar; art arda profil uygulansa da ilk değer korunur.</summary>
public sealed class AndroidSnapshot
{
    public string Serial { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public List<string> Profiles { get; set; } = [];
    public Dictionary<string, Setting> Values { get; set; } = [];

    [JsonIgnore]
    public string Summary => $"{string.Join(", ", Profiles)}  ·  {Values.Count:N0} ayar yedekli  ·  {Created.LocalDateTime:g}";
}

public sealed record ApplyResult(int Total, IReadOnlyList<string> Failed, int Undoable, string LogPath);

/// <summary>Android profillerini tek ADB oturumunda uygular, ilk değerleri yedekler ve geri yükler.</summary>
public static partial class Android
{
    [GeneratedRegex(@"^settings put (global|system|secure) (\S+)")] private static partial Regex SettingRx();
    [GeneratedRegex(@"^setprop (\S+)")] private static partial Regex PropRx();
    [GeneratedRegex(@"^(?:cmd )?device_config put (\S+) (\S+)")] private static partial Regex ConfigRx();
    [GeneratedRegex(@"^[A-Za-z0-9_.:/\-]+$")] private static partial Regex SafeToken();

    /// <summary>Komutun değiştirdiği ayarı tanır (geri alınabilir komutlar); tanımıyorsa null.</summary>
    internal static Setting? Target(string cmd) =>
        SettingRx().Match(cmd) is { Success: true } s ? new("settings", s.Groups[1].Value, s.Groups[2].Value, null) :
        PropRx().Match(cmd) is { Success: true } p ? new("prop", "", p.Groups[1].Value, null) :
        ConfigRx().Match(cmd) is { Success: true } c ? new("config", c.Groups[1].Value, c.Groups[2].Value, null) : null;

    // ---------- Yedek dosyaları ----------

    /// <summary>
    /// Yalnızca harf/rakamdan oluşan seriler için "<seri>.json" (eski sürümlerle aynı). Başka karakter içerenlerde
    /// ("192.168.1.5:5555", "emulator-5554") ada serinin özeti eklenir; yoksa "a:b" ile "a_b" aynı dosyaya düşerdi.
    /// </summary>
    internal static string SnapshotFile(string serial)
    {
        var safe = string.Concat(serial.Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_'));
        if (safe != serial) safe += "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serial)), 0, 4).ToLowerInvariant();
        return Path.Combine(AppPaths.Snapshots, safe + ".json");
    }

    /// <summary>Eski sürümün özetsiz dosya adı (yalnızca içindeki seri eşleşiyorsa kullanılır).</summary>
    private static string LegacyFile(string serial) =>
        Path.Combine(AppPaths.Snapshots, string.Concat(serial.Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_')) + ".json");

    /// <summary>Bu cihazın yedek dosyası: yeni ad, yoksa serisi eşleşen eski ad.</summary>
    private static string FindFile(string serial)
    {
        var file = SnapshotFile(serial);
        var legacy = LegacyFile(serial);
        return !File.Exists(file) && legacy != file && Clean(Json.Read<AndroidSnapshot>(legacy))?.Serial == serial ? legacy : file;
    }

    /// <summary>Dosyadaki seri istenen cihazla eşleşmiyorsa yedek kullanılmaz (başka cihazın yedeği uygulanmasın).</summary>
    public static AndroidSnapshot? LoadSnapshot(string serial) =>
        Clean(Json.Read<AndroidSnapshot>(FindFile(serial))) is { } s && s.Serial == serial ? s : null;

    public static List<AndroidSnapshot> AllSnapshots()
    {
        try
        {
            return Directory.EnumerateFiles(AppPaths.Snapshots, "*.json")
                .Select(f => Clean(Json.Read<AndroidSnapshot>(f))).OfType<AndroidSnapshot>()
                .OrderBy(s => s.Serial, StringComparer.Ordinal).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Elle düzenlenmiş/bozuk yedek dosyası null alanlar içerebilir; kullanılamaz girdileri ayıklar.</summary>
    private static AndroidSnapshot? Clean(AndroidSnapshot? s)
    {
        if (s is null || !Adb.IsSafeSerial(s.Serial)) return null;
        s.Profiles = (s.Profiles ?? []).Where(p => p is not null).ToList();
        s.Values = (s.Values ?? []).Where(kv => kv.Value is { Kind: not null, Ns: not null, Key: not null })
            .ToDictionary(kv => kv.Value.Id, kv => kv.Value);
        return s;
    }

    private static void Save(AndroidSnapshot snap)
    {
        try
        {
            var legacy = FindFile(snap.Serial);
            Json.Write(SnapshotFile(snap.Serial), snap);
            if (legacy != SnapshotFile(snap.Serial)) File.Delete(legacy); // eski adlı dosya yeni ada taşındı
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new UserException("Yedek dosyası yazılamadı (disk dolu veya dosya kilitli olabilir).", e); }
    }

    // ---------- Uygulama ----------

    /// <summary>
    /// Önce mevcut değerler tek oturumda okunup yedeklenir, sonra tüm profil TEK "adb shell" oturumunda çalışır
    /// (eski sürümler her satır için ayrı adb.exe açıyordu: Balanced ≈ 1.500 süreç).
    /// </summary>
    public static async Task<ApplyResult> ApplyAsync(Profile profile, string serial, Action<int, int>? progress = null,
        Action<string>? log = null, CancellationToken ct = default)
    {
        var cmds = profile.Commands;
        if (cmds.Count == 0) throw new UserException("Bu profilde çalıştırılacak komut yok.");
        var (undoable, added) = await SnapshotAsync(serial, profile, cmds, ct);

        // Rastgele işaret: bir komutun kendi çıktısı ilerleme/başarısızlık satırı gibi görünemez.
        var mark = "@@" + Random.Shared.Next().ToString("x8");
        var script = new StringBuilder();
        for (var i = 0; i < cmds.Count; i++)
            script.Append($"echo {mark}:{i}\n{cmds[i]} </dev/null 2>&1 || echo {mark}:F{i}\n"); // </dev/null: komut betiğin kalanını yutmasın
        script.Append($"echo {mark}:END\nexit\n");

        var failed = new List<string>();
        var finished = false;
        var gate = new object();
        var r = await Adb.ShellAsync(serial, script.ToString(), line =>
        {
            if (!line.StartsWith(mark, StringComparison.Ordinal)) { if (line.Length > 0) log?.Invoke("  " + line); return; }
            var tag = line.AsSpan(mark.Length + 1);
            if (tag is "END") finished = true;
            else if (tag.StartsWith("F") && int.TryParse(tag[1..], out var f) && (uint)f < (uint)cmds.Count)
            {
                lock (gate) failed.Add(cmds[f]);
                log?.Invoke("✗ " + cmds[f]);
            }
            else if (int.TryParse(tag, out var i)) progress?.Invoke(i + 1, cmds.Count);
        }, ct);

        var logPath = "";
        try
        {
            logPath = AppPaths.NewLog($"android-{profile.Id}");
            await File.WriteAllTextAsync(logPath,
                $"Profil: {profile.Name}\nCihaz: {serial}\nÇıkış kodu: {r.ExitCode}\nBaşarısız: {failed.Count}/{cmds.Count}\n\n" +
                $"{string.Join('\n', failed.Select(f => "FAILED: " + f))}\n\n--- ÇIKTI ---\n{r.Output}", ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* günlük yazılamadı diye işlem başarısız sayılmaz */ }

        if (!finished)
            throw new UserException("Cihazla bağlantı yarıda kesildi (kablo çıkmış olabilir). O ana kadarki ayarların yedeği duruyor; Geri yükleme'den dönebilirsin.");

        // Cihazın reddettiği komutlar hiçbir şeyi değiştirmedi; yedeğini tutmak geri yüklemede boşuna hata üretir.
        // Aynı ayarı hedefleyen birden fazla komut olabilir: biri bile başarılıysa ayar değişmiştir ve yedeği KALMALIDIR.
        var failedSet = failed.ToHashSet(StringComparer.Ordinal);
        var succeededIds = cmds.Where(c => !failedSet.Contains(c)).Select(Target).OfType<Setting>().Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var rejected = failed.Select(Target).OfType<Setting>().Select(t => t.Id).Where(id => added.Contains(id) && !succeededIds.Contains(id)).ToHashSet();
        if (rejected.Count > 0 && LoadSnapshot(serial) is { } snap)
        {
            foreach (var id in rejected) snap.Values.Remove(id);
            Save(snap);
        }
        return new(cmds.Count, failed, undoable - rejected.Count, logPath);
    }

    /// <summary>Saf fonksiyon: "@@bölüm" işaretli settings/getprop/device_config çıktısını "tür|ad alanı|anahtar" → değer sözlüğüne çevirir.</summary>
    internal static Dictionary<string, string> ParseCurrent(string output)
    {
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        var section = "";
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("@@", StringComparison.Ordinal)) { section = line[2..]; continue; }
            if (section == "prop" && line.StartsWith('[') && line.EndsWith(']') && line.IndexOf("]: [", StringComparison.Ordinal) is var k and > 0)
                current[$"prop||{line[1..k]}"] = line[(k + 4)..^1]; // yalnızca son ']' atılır (değer ']' ile bitebilir)
            else if (section == "config" && line.IndexOf('/') is var s and > 0 && line.IndexOf('=') is var e and > 0 && e > s)
                current[$"config|{line[..s]}|{line[(s + 1)..e]}"] = line[(e + 1)..];
            else if (section is "global" or "system" or "secure" && line.IndexOf('=') is var eq and > 0)
                current[$"settings|{section}|{line[..eq]}"] = line[(eq + 1)..];
        }
        return current;
    }

    /// <summary>
    /// Okuma eksikse (bir bölüm hata verdi ya da boş döndü) var olan ayarlar "önceden yoktu" diye kaydedilir ve
    /// geri yükleme onları SİLERDİ. Her Android cihazda bu üç bölümde en az bir giriş bulunur; biri bile boşsa okuma güvenilmezdir.
    /// (device_config eski Android sürümlerinde hiç olmayabilir; o yüzden zorunlu tutulmaz.)
    /// </summary>
    internal static bool IsCompleteRead(Dictionary<string, string> current) =>
        current.Keys.Any(k => k.StartsWith("settings|global|", StringComparison.Ordinal)) &&
        current.Keys.Any(k => k.StartsWith("settings|system|", StringComparison.Ordinal)) &&
        current.Keys.Any(k => k.StartsWith("settings|secure|", StringComparison.Ordinal)) &&
        current.Keys.Any(k => k.StartsWith("prop||", StringComparison.Ordinal));

    /// <summary>settings/getprop/device_config listelerini bir kerede okuyup değişecek anahtarların ilk değerini kaydeder.</summary>
    private static async Task<(int Undoable, HashSet<string> Added)> SnapshotAsync(string serial, Profile profile, IReadOnlyList<string> cmds, CancellationToken ct)
    {
        var snap = LoadSnapshot(serial);
        if (snap is null)
        {
            // Dosya var ama okunamıyor (bozuk ya da o an kilitli): üzerine yazmak, zaten değiştirilmiş değerleri
            // "orijinal" diye kaydetmek olurdu. Bozuksa kenara al; taşınamıyorsa (kilitli) hiçbir şey yapma.
            var file = FindFile(serial);
            if (File.Exists(file))
            {
                try { File.Move(file, $"{file}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}"); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new UserException("Bu cihazın yedek dosyası şu an okunamıyor (başka bir program kullanıyor olabilir). Hiçbir değişiklik yapılmadı; biraz sonra tekrar dene.", e);
                }
            }
            snap = new AndroidSnapshot { Serial = serial };
        }
        var targets = cmds.Select(Target).OfType<Setting>().ToList();
        var r = await Adb.ShellAsync(serial,
            "echo @@global; settings list global\necho @@system; settings list system\necho @@secure; settings list secure\n" +
            "echo @@prop; getprop\necho @@config; device_config list\necho @@done\nexit\n", ct: ct);
        if (!r.Output.Contains("@@done", StringComparison.Ordinal))
            throw new UserException("Cihaz ayarları okunamadı; hiçbir değişiklik yapılmadı. Kabloyu ve telefondaki USB hata ayıklama iznini kontrol et.");

        var current = ParseCurrent(r.Output);
        if (!IsCompleteRead(current))
            throw new UserException("Cihazın mevcut ayarları tam okunamadı; yedek alınamayacağı için hiçbir değişiklik yapılmadı. Kabloyu çıkarıp takıp tekrar dene.");
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in targets)
            if (!snap.Values.ContainsKey(t.Id) && added.Add(t.Id))
                // Listede yoksa (veya settings "null" diyorsa) ayar önceden yoktu → geri yüklemede silinir. Boş değer "" olarak korunur.
                snap.Values[t.Id] = t with { Value = current.TryGetValue(t.Id, out var v) && !(t.Kind != "prop" && v == "null") ? v : null };
        if (!snap.Profiles.Contains(profile.Name)) snap.Profiles.Add(profile.Name);
        Save(snap);
        return (targets.Count, added);
    }

    // ---------- Geri yükleme ----------

    /// <summary>Saf fonksiyon: yedekteki bir değeri geri yazan kabuk komutu. Güvensiz/bozuk girdi → null.</summary>
    public static string? RestoreCommand(Setting v)
    {
        // GÜVENLİK: anahtar/ad alanı yedek JSON'undan gelir (kullanıcının yazabildiği dosya) ve cihaz kabuğuna tırnaksız gider.
        // Yalnızca güvenli karakterlere izin verilir; değerler her zaman tek tırnakla kaçışlanır.
        if (v.Kind is null || v.Ns is null || v.Key is null) return null;
        if (!SafeToken().IsMatch(v.Key) || (v.Ns.Length > 0 && !SafeToken().IsMatch(v.Ns))) return null;
        if ((v.Kind == "prop") != (v.Ns.Length == 0)) return null; // prop'un ad alanı olmaz; diğerlerinde zorunlu
        if (v.Value is { } value && value.AsSpan().IndexOfAny('\n', '\r', '\0') >= 0) return null; // çok satırlı değer betiği böler
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

    /// <summary>Kaydedilmiş ilk değerleri geri yazar (önceden olmayan ayarları siler). Geri yüklenemeyen ayar sayısını döner.</summary>
    public static async Task<int> RestoreAsync(string serial, Action<string>? log = null, CancellationToken ct = default)
    {
        var snap = LoadSnapshot(serial) ?? throw new UserException("Bu cihaz için kayıtlı yedek yok.");
        var all = snap.Values.Values.Select(v => (Setting: v, Cmd: RestoreCommand(v))).ToList();
        var items = all.Where(x => x.Cmd is not null).ToList();
        var skipped = all.Where(x => x.Cmd is null).Select(x => x.Setting).ToList();
        if (skipped.Count > 0) log?.Invoke($"{skipped.Count} yedek girdisi güvenli biçimde geri yazılamadığı için atlandı (yedekte saklanıyor).");

        var mark = "@@" + Random.Shared.Next().ToString("x8");
        var failed = new HashSet<int>();
        var finished = false;
        var script = string.Concat(items.Select((x, i) => $"{x.Cmd} </dev/null >/dev/null 2>&1 || echo {mark}:F{i}\n")) + $"echo {mark}:END\nexit\n";
        await Adb.ShellAsync(serial, script, line =>
        {
            if (!line.StartsWith(mark, StringComparison.Ordinal)) return;
            var tag = line.AsSpan(mark.Length + 1);
            if (tag is "END") finished = true;
            else if (tag.StartsWith("F") && int.TryParse(tag[1..], out var i)) lock (failed) failed.Add(i);
        }, ct);

        // Yedek YALNIZCA oturum sonuna kadar tamamlandıysa güncellenir; kablo çıkarsa hiçbir değer kaybolmaz.
        if (!finished) throw new UserException("Cihazla bağlantı yarıda kesildi; yedek korunuyor, tekrar deneyebilirsin.");
        log?.Invoke($"{items.Count - failed.Count:N0}/{items.Count:N0} ayar geri yüklendi.");
        if (failed.Count == 0 && skipped.Count == 0)
        {
            try { File.Delete(FindFile(serial)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* bir sonraki geri yüklemede yeniden denenir */ }
        }
        else
        {
            // Geri yüklenenleri çıkar; başarısız VE atlanan girdiler saklanır (orijinal değerler asla sessizce silinmez).
            snap.Values = items.Where((_, i) => failed.Contains(i)).Select(x => x.Setting).Concat(skipped).ToDictionary(s => s.Id);
            Save(snap);
        }
        return failed.Count + skipped.Count;
    }
}
