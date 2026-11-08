namespace UMToptimizer.Core;

/// <summary>Komut satırı: UMToptimizer.exe &lt;windows|android|profiles&gt; ...</summary>
public static class Cli
{
    private const string Help = """
        UMToptimizer 3 — Windows ve Android (ADB) optimizasyon kontrol merkezi

          UMToptimizer profiles
          UMToptimizer windows apply <standard|aggressive> --yes [--restore-point] [--allow-bitlocker]
          UMToptimizer windows restore --yes
          UMToptimizer android devices
          UMToptimizer android preview <profil>
          UMToptimizer android apply <profil> [--serial ID] --yes
          UMToptimizer android restore [--serial ID]
          UMToptimizer android install-adb
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        bool Has(string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);
        string? Opt(string key) => args.SkipWhile(a => !a.Equals(key, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var a = args.Where(x => !x.StartsWith("--")).Select(x => x.ToLowerInvariant()).ToArray();
        void Print(string s) => Console.WriteLine(s);

        try
        {
            switch (a)
            {
                case ["profiles"]:
                    foreach (var p in Profiles.All) Print($"{p.Platform,-8} {p.Id,-14} {p.Name,-14} {p.Badge}");
                    return 0;

                case ["windows", "apply", var id] when Profiles.Find(Platform.Windows, id) is { } p:
                    if (!Has("--yes")) return Fail($"{p.Name}: {p.Description}\nUYARI: {p.Risk}\nOnaylamak için --yes ekleyin.");
                    await WindowsTweaks.RunAsync(p.Source, new(Has("--restore-point"), Has("--allow-bitlocker")), Print);
                    return 0;

                case ["windows", "restore"]:
                    if (!Has("--yes")) return Fail("Windows yedeğini geri yüklemek için --yes ekleyin.");
                    await WindowsTweaks.RunAsync("Restore", new(), Print);
                    return 0;

                case ["android", "devices"]:
                    var devices = await Adb.DevicesAsync();
                    devices.ForEach(d => Print($"{d.Serial}\t{d.State}\t{d.Model}"));
                    return devices.Any(d => d.Ready) ? 0 : Fail("Hazır ADB cihazı yok. USB hata ayıklamayı açıp izni onaylayın.");

                case ["android", "install-adb"]:
                    await Adb.InstallPlatformToolsAsync();
                    Print($"ADB kuruldu: {Adb.Path}");
                    return 0;

                case ["android", "preview", var id] when Profiles.Find(Platform.Android, id) is { } p:
                    Print($"{p.Name} — {p.Description}\nUYARI: {p.Risk}\n{p.Commands.Count} komut:\n");
                    foreach (var c in p.Commands) Print(c);
                    return 0;

                case ["android", "apply", var id] when Profiles.Find(Platform.Android, id) is { } p:
                    if (!Has("--yes")) return Fail($"{p.Commands.Count} ADB komutu çalışacak. Onaylamak için --yes ekleyin.");
                    var serial = await SerialAsync(Opt("--serial"));
                    var r = await Adb.ApplyAsync(p, serial, (i, n) => { if (i % 100 == 0 || i == n) Print($"[{i}/{n}]"); }, Print);
                    Print($"Tamamlandı: {r.Total - r.Failed.Count}/{r.Total} başarılı, {r.Undoable} ayar yedeklendi. Günlük: {r.LogPath}");
                    return r.Failed.Count == 0 ? 0 : 1;

                case ["android", "restore"]:
                    var failed = await Adb.RestoreAsync(await SerialAsync(Opt("--serial")), Print);
                    return failed == 0 ? 0 : 1;

                default:
                    Print(Help);
                    return a.Length == 0 || Has("--help") ? 0 : 2;
            }
        }
        catch (Exception ex) { return Fail("Hata: " + ex.Message); }
    }

    private static async Task<string> SerialAsync(string? serial)
    {
        if (serial is not null) return serial;
        var ready = (await Adb.DevicesAsync()).Where(d => d.Ready).ToList();
        return ready.Count == 1 ? ready[0].Serial : throw new InvalidOperationException("Birden fazla/hiç hazır cihaz yok; --serial ID belirtin.");
    }

    private static int Fail(string message) { Console.Error.WriteLine(message); return 1; }
}
