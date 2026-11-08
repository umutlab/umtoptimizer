using System.IO;
using System.Reflection;

namespace UMToptimizer.Core;

public enum Platform { Windows, Android }

/// <summary>Tek profil tipi: Windows profilleri PowerShell moduna, Android profilleri gömülü .bat dosyasına işaret eder.</summary>
public sealed record Profile(string Id, Platform Platform, string Name, string Subtitle, string Description, string Risk, string Source, params string[] Highlights)
{
    private IReadOnlyList<string>? _commands;

    /// <summary>Android komutları ("adb shell " öneki atılmış, tekrarlar ayıklanmış). İlk erişimde yüklenir.</summary>
    public IReadOnlyList<string> Commands => _commands ??= Platform == Platform.Android ? LoadCommands(Source) : [];

    public string Badge => Platform == Platform.Android ? $"{Commands.Count:N0} komut" : Source == "Aggressive" ? "İleri seviye" : "Önerilen";

    private static string[] LoadCommands(string file)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(file)
            ?? throw new InvalidOperationException($"Gömülü profil bulunamadı: {file}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n')
            .Select(Normalize)
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>"adb shell ..." satırını cihaz kabuğunda çalışacak komuta çevirir.</summary>
    internal static string Normalize(string line)
    {
        line = line.Trim();
        if (line.StartsWith("adb shell ", StringComparison.OrdinalIgnoreCase)) line = line[10..].Trim();
        // Komutun tamamını tek tırnakla saran satır ('settings put ...') kabukta tek kelime sayılıp hata veriyordu.
        if (line.Length > 1 && line[0] == '\'' && line[^1] == '\'' && line.Count(c => c == '\'') == 2) line = line[1..^1];
        return line;
    }
}

public static class Profiles
{
    public static IReadOnlyList<Profile> All { get; } =
    [
        new("standard", Platform.Windows, "Standart", "Güvenli temel ayarlar",
            "Telemetri, etkinlik geçmişi, konum ve tüketici özelliklerini kapatır; bazı arka plan hizmetlerini sınırlar, geçici dosyaları temizler ve hazırda bekletmeyi kapatır.",
            "Hazırda bekletme kapanır; geçici dosyalar ve Windows bileşen önbelleği silinir (geri getirilemez). BitLocker yalnızca ayrıca izin verirseniz kapatılır.",
            "Standard",
            "Gizlilik: telemetri, konum, etkinlik geçmişi kapatılır",
            "Hizmetler: DiagTrack, WerSvc, lfsvc vb. devre dışı",
            "Temizlik: TEMP, Disk Temizleme, DISM bileşen temizliği",
            "Yedek: kayıt defteri + hizmet yedeği, isteğe bağlı geri yükleme noktası"),
        new("aggressive", Platform.Windows, "Agresif", "Standart + ileri seviye",
            "Standart profilin tamamına ek olarak arayüz animasyonları, bildirimler, IPv6/Teredo, Edge/Brave politikaları, Windows AI bileşenleri ve isteğe bağlı uygulamalarda kapsamlı değişiklik yapar.",
            "Edge, OneDrive, Copilot ve isteğe bağlı uygulamalar kaldırılır (Edge profili silinebilir). IPv6 ve bildirim merkezi kapanır. Kaldırılan uygulamalar geri alınamaz; performans artışı garanti edilmez.",
            "Aggressive",
            "Arayüz: animasyon, gölge, Aero Peek kapatılır",
            "Ağ: IPv6 ve Teredo devre dışı",
            "Kaldırma: Edge, OneDrive, Copilot, Haberler, Hava Durumu vb.",
            "Hosts: Adobe engelleme listesi (geri yüklemede temizlenir)"),

        new("balanced", Platform.Android, "Balanced", "Günlük kullanım",
            "Pil, uygulama ve performans davranışını dengelemeyi hedefler; Android çalışma zamanı, bellek, ağ, güç ve üretici ayarlarına dokunur.",
            "Wi-Fi ülke kodunu JP, operatör kodlarını 9999 yapmaya çalışan satırlar içerir; bölgeye özgü ağ davranışı değişebilir.", "Balanced.bat"),
        new("extra-boost", Platform.Android, "Extra Boost", "Yüksek performans",
            "CPU/GPU, çizim ve ekran yenileme davranışını yükseltmeyi hedefleyen geniş profil.",
            "120 Hz ve frekans değerlerini zorlar; daha fazla pil tüketimi ve ısınma yapabilir.", "Extra_Boost.bat"),
        new("hardware", Platform.Android, "Hardware", "Donanım hızlandırma",
            "Donanım bileşimi, GPU çizimi ve video hızlandırma bayraklarını etkinleştirmeyi dener.",
            "Üreticiye bağlı bayraklar; desteklemeyen cihazlarda etkisiz kalabilir veya görüntüyü bozabilir.", "Hardware.bat"),
        new("lite-ps", Platform.Android, "Lite PS", "Hafif güç tasarrufu",
            "Performans kısıtlaması, arka plan uygulamaları ve düşük güç davranışını ayarlar.",
            "Arka plan uygulamalarını ve bildirimleri sınırlayabilir.", "Lite_ps.bat"),
        new("power-saving", Platform.Android, "Power Saving", "Güç tasarrufu",
            "Pil, boşta kalma, ağ ve sistem kaynaklarını güç tasarrufu yönünde değiştirir.",
            "Zorunlu idle ve ağ politikası komutları içerir; bildirimler ve bağlantı etkilenebilir.", "Power_Saving.bat"),
        new("qualcomm", Platform.Android, "Qualcomm only", "Qualcomm cihazlar",
            "Qualcomm/MDP görüntü birleştirme bayraklarını yazar.",
            "Yalnızca bazı Qualcomm cihazlarda anlamlıdır.", "Qualcom_only.bat"),
        new("vulkan", Platform.Android, "Vulkan", "Vulkan çizim yolu",
            "Arayüz çizimini Vulkan/Skia yoluna yönlendirmeyi dener.",
            "Vulkan desteği zayıf cihazlarda grafik hatası veya kararsızlık yapabilir.", "Vulkan.bat"),
        new("compile", Platform.Android, "Compile", "Uygulama derleme",
            "Tüm uygulamalar için 'everything' derlemesini ve arka plan dexopt işini başlatır.",
            "Uzun sürer; pil, ısı ve depolama kullanır. Derleme durumu otomatik geri alınamaz.", "Compile.bat"),
    ];

    public static IEnumerable<Profile> For(Platform p) => All.Where(x => x.Platform == p);

    /// <summary>Id, ad ("extraboost"/"Extra Boost") veya eski CLI adlarıyla (balanced/performance) bulur.</summary>
    public static Profile? Find(Platform p, string key)
    {
        static string Flat(string s) => s.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
        if (p == Platform.Windows) key = key.ToLowerInvariant() switch { "balanced" => "standard", "performance" => "aggressive", _ => key };
        return For(p).FirstOrDefault(x => Flat(x.Id) == Flat(key) || Flat(x.Name) == Flat(key) || Flat(x.Source) == Flat(key));
    }
}
