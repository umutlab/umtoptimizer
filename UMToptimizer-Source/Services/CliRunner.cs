using UMToptimizer.Models;

namespace UMToptimizer.Services;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var service = new OptimizerService();
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help") { PrintHelp(); return 0; }
            var verb = args[0].ToLowerInvariant();
            if (verb == "android" && args.Length >= 2 && args[1] == "devices")
            {
                var devices = await service.GetAndroidDevicesAsync();
                if (devices.Count == 0) { Console.WriteLine("ADB cihazı bulunamadı. USB hata ayıklamayı açıp cihazı bağlayın."); return 1; }
                foreach (var device in devices) Console.WriteLine($"{device.Serial}\t{device.State}\t{device.Details}");
                return devices.Any(d => d.State == "device") ? 0 : 1;
            }
            if (verb == "restore")
            {
                var platform = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
                await service.UndoAsync(Option(args, "--serial"), platform);
                Console.WriteLine("Son kaydedilen değişiklik geri yüklendi.");
                return 0;
            }
            if (verb is not ("windows" or "android") || args.Length < 2) { PrintHelp(); return 2; }
            var platformKind = verb == "windows" ? PlatformKind.Windows : PlatformKind.Android;
            var subcommand = args[1].ToLowerInvariant();
            if (subcommand is not ("preview" or "apply")) { PrintHelp(); return 2; }
            if (args.Length < 3) { Console.Error.WriteLine("Profil adı gerekli. Örn: balanced"); return 2; }
            var profile = ProfileCatalog.Find(platformKind, args[2]);
            if (profile is null) { Console.Error.WriteLine($"Profil bulunamadı: {args[2]}"); return 2; }
            Console.WriteLine($"{profile.Name} — {profile.Subtitle}\n{profile.Description}\n");
            if (profile.Commands.Count > 0)
                Console.WriteLine($"ÖNEMLİ: {profile.Commands.Count:N0} ADB komutu. {profile.RiskNotice}\n");
            else if (!string.IsNullOrWhiteSpace(profile.RiskNotice))
                Console.WriteLine($"UYARI: {profile.RiskNotice}\n");
            foreach (var action in profile.Actions)
                Console.WriteLine($"• {action.Title}: {action.Detail}" + (string.IsNullOrWhiteSpace(action.Value) ? "" : $" (değer: {action.Value})"));
            if (profile.Commands.Count > 0)
            {
                Console.WriteLine($"\nTam komut önizlemesi ({profile.Commands.Count:N0} komut):");
                foreach (var command in profile.Commands) Console.WriteLine(command);
            }
            var serial = Option(args, "--serial");
            if (subcommand == "preview")
            {
                if (platformKind == PlatformKind.Android)
                {
                    var devices = await service.GetAndroidDevicesAsync();
                    foreach (var device in devices) Console.WriteLine($"ADB: {device.Serial} — {device.State}");
                    if (string.IsNullOrWhiteSpace(serial) && devices.Count(d => d.State == "device") == 1) Console.WriteLine("Tek etkin cihaz bulundu; uygulamada --serial ile sabitleyin.");
                }
                Console.WriteLine("Önizleme tamamlandı; ayar uygulanmadı.");
                return 0;
            }
            if (!args.Contains("--yes", StringComparer.OrdinalIgnoreCase)) { Console.Error.WriteLine("Güvenlik için apply komutu açık onay ister: --yes ekleyin."); return 2; }
            if (platformKind == PlatformKind.Android && string.IsNullOrWhiteSpace(serial))
            {
                var devices = await service.GetAndroidDevicesAsync();
                var ready = devices.Where(d => d.State == "device").ToList();
                if (ready.Count != 1) { Console.Error.WriteLine("Bir cihaz seçmek için --serial DEVICE_ID gereklidir."); return 2; }
                serial = ready[0].Serial;
            }
            await service.ApplyAsync(profile, serial);
            Console.WriteLine(profile.Commands.Count > 0
                ? "Profil komutları tamamlandı. Bu kaynak betiğin tüm değişiklikleri otomatik geri alınamaz; sonuçları Geri Yükleme bölümünde inceleyin."
                : "Profil uygulandı. Geri almak için: UMToptimizer.exe restore" + (platformKind == PlatformKind.Android ? $" android --serial {serial}" : " windows"));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Hata: {ex.Message}"); return 1; }
    }

    private static string? Option(string[] args, string key)
    {
        var index = Array.FindIndex(args, a => a.Equals(key, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    private static void PrintHelp()
    {
        Console.WriteLine("UMToptimizer — Windows ve Android ayar kontrol merkezi\n");
        Console.WriteLine("Kullanım:");
        Console.WriteLine("  UMToptimizer.exe windows preview <balanced|performance>");
        Console.WriteLine("  UMToptimizer.exe windows apply <balanced|performance> --yes");
        Console.WriteLine("  UMToptimizer.exe android devices");
        Console.WriteLine("  UMToptimizer.exe android preview <balanced|extra-boost|hardware|lite-ps|power-saving|qualcom-only|vulkan|compile>");
        Console.WriteLine("  UMToptimizer.exe android apply <profile> --serial DEVICE_ID --yes");
        Console.WriteLine("  UMToptimizer.exe restore [windows|android] [--serial DEVICE_ID]");
        Console.WriteLine("  UMToptimizer.exe --help");
    }
}
