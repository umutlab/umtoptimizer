using System.IO;

namespace UMToptimizer.Core;

/// <summary>Komut satırı: UMToptimizer.exe &lt;windows|android|profiles&gt; ...</summary>
public static class Cli
{
    private const string Help = """
        UMToptimizer 4 — Windows ve Android (ADB) optimizasyon kontrol merkezi

          UMToptimizer profiles
          UMToptimizer windows apply <standard|aggressive> --yes [--restore-point] [--allow-bitlocker]
          UMToptimizer windows restore --yes
          UMToptimizer android devices
          UMToptimizer android preview <profil>
          UMToptimizer android apply <profil> [--serial ID] --yes
          UMToptimizer android restore [--serial ID] --yes
          UMToptimizer android install-adb
        """;

    /// <summary>Değer alan seçenekler; değerleri konum argümanı sayılmaz.</summary>
    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase) { "--serial" };

    internal sealed record Parsed(string[] Positional, HashSet<string> Flags, Dictionary<string, string> Options);

    /// <summary>Saf fonksiyon: argümanları konum / bayrak / değerli seçenek olarak ayırır.</summary>
    internal static Parsed Parse(string[] args)
    {
        var positional = new List<string>();
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (ValueOptions.Contains(a))
            {
                if (i + 1 >= args.Length) throw new UserException($"{a} için bir değer gerekli.");
                options[a] = args[++i];
            }
            else if (a.StartsWith("--", StringComparison.Ordinal)) flags.Add(a);
            else positional.Add(a.ToLowerInvariant());
        }
        return new(positional.ToArray(), flags, options);
    }

    public static async Task<int> RunAsync(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;
        void Print(string s) => stdout.WriteLine(s);
        int Fail(string message) { stderr.WriteLine(message); return 1; }

        try
        {
            var (a, flags, options) = Parse(args);
            var yes = flags.Contains("--yes");

            switch (a)
            {
                case ["profiles"]:
                    foreach (var p in Profiles.All) Print($"{p.Platform,-8} {p.Id,-14} {p.Name,-14} {p.Badge}");
                    return 0;

                case ["windows", "apply", var id] when Profiles.Find(Platform.Windows, id) is { } p:
                    if (!yes) return Fail($"{p.Name}: {p.Description}\nUYARI: {p.Risk}\nOnaylamak için --yes ekleyin.");
                    await WindowsTweaks.RunAsync(p.Source, new(flags.Contains("--restore-point"), flags.Contains("--allow-bitlocker")), Print);
                    return 0;

                case ["windows", "restore"]:
                    if (!yes) return Fail("Windows yedeğini geri yüklemek için --yes ekleyin.");
                    await WindowsTweaks.RunAsync("Restore", new(RestorePoint: false), Print);
                    return 0;

                case ["android", "devices"]:
                    var devices = await Adb.DevicesAsync();
                    devices.ForEach(d => Print($"{d.Serial}\t{d.State}\t{d.Model}"));
                    return devices.Any(d => d.Ready) ? 0 : Fail("Hazır ADB cihazı yok. USB hata ayıklamayı açıp izni onaylayın.");

                case ["android", "install-adb"]:
                    await Adb.InstallAsync();
                    Print($"ADB kuruldu: {Adb.Path}");
                    return 0;

                case ["android", "preview", var id] when Profiles.Find(Platform.Android, id) is { } p:
                    Print($"{p.Name} — {p.Description}\nUYARI: {p.Risk}\n{p.Commands.Count} komut:\n");
                    foreach (var c in p.Commands) Print(c);
                    return 0;

                case ["android", "apply", var id] when Profiles.Find(Platform.Android, id) is { } p:
                    if (!yes) return Fail($"{p.Commands.Count} ADB komutu çalışacak. Onaylamak için --yes ekleyin.");
                    var serial = await SerialAsync(options.GetValueOrDefault("--serial"));
                    var r = await Android.ApplyAsync(p, serial, (i, n) => { if (i % 100 == 0 || i == n) Print($"[{i}/{n}]"); }, Print);
                    Print($"Tamamlandı: {r.Total - r.Failed.Count}/{r.Total} başarılı, {r.Undoable} ayar yedeklendi. Günlük: {r.LogPath}");
                    return r.Failed.Count == 0 ? 0 : 1;

                case ["android", "restore"]:
                    if (!yes) return Fail("Android yedeğini geri yüklemek için --yes ekleyin.");
                    return await Android.RestoreAsync(await SerialAsync(options.GetValueOrDefault("--serial")), Print) == 0 ? 0 : 1;

                default:
                    Print(Help);
                    return a.Length == 0 || flags.Contains("--help") ? 0 : 2;
            }
        }
        catch (Exception ex) when (ex is UserException or OperationCanceledException) { return Fail(ex.Message); }
        catch (Exception ex)
        {
            CrashLog.Write("Cli", ex);
            return Fail($"Beklenmeyen bir sorun oluştu. Ayrıntı: {CrashLog.File}");
        }
    }

    private static async Task<string> SerialAsync(string? serial)
    {
        if (serial is not null) return Adb.IsSafeSerial(serial) ? serial : throw new UserException("Geçersiz --serial değeri.");
        var ready = (await Adb.DevicesAsync()).Where(d => d.Ready).ToList();
        return ready.Count == 1 ? ready[0].Serial : throw new UserException("Birden fazla ya da hiç hazır cihaz yok; --serial ID belirtin.");
    }
}
