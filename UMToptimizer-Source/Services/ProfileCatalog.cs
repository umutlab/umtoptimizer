using UMToptimizer.Models;
using System.Reflection;
using System.Text;
using System.IO;

namespace UMToptimizer.Services;

public static class ProfileCatalog
{
    private static ProfileAction Setting(string key, string value, string title, string detail) => new(ProfileActionKind.AndroidSetting, key, value, title, detail);
    private static ProfileAction Plan(string alias, string title, string detail) => new(ProfileActionKind.PowerPlan, alias, alias, title, detail);

    private static OptimizationProfile Android(string id, string name, string subtitle, string description, string file, string risk) =>
        new(id, name, subtitle, description, PlatformKind.Android, [])
        {
            Commands = LoadCommands(file),
            RiskNotice = risk
        };

    private static OptimizationProfile WindowsScript(string id, string name, string subtitle, string mode, string description, string risk, IReadOnlyList<ProfileAction> actions) =>
        new(id, name, subtitle, description, PlatformKind.Windows, actions) { WindowsScriptMode = mode, RiskNotice = risk };

    private static IReadOnlyList<string> LoadCommands(string file)
    {
        var resource = $"UMToptimizer.Resources.Android.{file}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Android profil kaynağı bulunamadı: {resource}");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(line => line.TrimEnd('\r')).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
    }

    public static IReadOnlyList<OptimizationProfile> All { get; } =
    [
        WindowsScript("windows-standard", "Standard", "Standart Windows ayarları", "Standard",
            "Gizlilik ve sistem davranışı ayarlarını düzenler; bazı hizmetleri sınırlar, geçici dosyaları temizler, hazırda bekletmeyi kapatır ve mümkünse geri yükleme noktası oluşturur.",
            "Hazırda bekletme kapanır. Bazı hizmetler ve konum/telemetri davranışı değişir; geçici dosyalar silinir ve Windows bileşen temizliği yapılır. BitLocker yalnızca kurtarma anahtarı için ayrıca onay verirseniz kapatılır. Dosyalar ve önbellekler geri getirilemez.",
            [new(ProfileActionKind.WindowsSetting, "", "", "Gizlilik ve arka plan", "Telemetri, konum erişimi, harita güncellemesi ve bazı arka plan hizmetleri değişir."), new(ProfileActionKind.WindowsSetting, "", "", "Temizlik ve güç", "Geçici dosyalar temizlenir ve hazırda bekletme kapatılır."), new(ProfileActionKind.WindowsSetting, "", "", "Geri alma", "Kayıt defteri/hizmet yedeği ve mümkünse Sistem Geri Yükleme noktası oluşturulur.")]),
        WindowsScript("windows-aggressive", "Agresif (Performans)", "İleri seviye sistem ayarları", "Aggressive",
            "Standart ayarların tamamına ek olarak Windows arayüzü, ağ, tarayıcılar ve isteğe bağlı bileşenlerde kapsamlı değişiklikler uygular.",
            "IPv6/Teredo ve bildirimler kapatılabilir; Edge, OneDrive, AI bileşenleri ve isteğe bağlı uygulamalar kaldırılabilir. Edge profili silinebilir, hosts dosyasına internetten alınan liste eklenir. Kaldırılan uygulamalar ve hosts ekleri tam geri alınamayabilir; performans artışı garanti edilmez.",
            [new(ProfileActionKind.WindowsSetting, "", "", "Arayüz ve ağ", "Animasyonlar/bildirimler azaltılır; IPv6 ve Teredo devre dışı bırakılır."), new(ProfileActionKind.WindowsSetting, "", "", "Bileşen ve uygulamalar", "Edge, OneDrive, AI bileşenleri ve seçili isteğe bağlı uygulamalar kaldırılabilir."), new(ProfileActionKind.WindowsSetting, "", "", "Sürücü ve hosts", "Bazı otomatik üretici yükleyicileri engellenir ve hosts dosyasına çevrimiçi Adobe listesi eklenir.")]),
        Android("android-balanced", "Balanced", "Günlük kullanım", "Günlük pil, uygulama ve performans davranışını dengelemeyi hedefler. Kaynak betik 1.496 komutla Android çalışma zamanı, bellek, ağ, güç ve üretici ayarlarına dokunur.", "Balanced.bat", "Wi-Fi ülke kodunu JP, operatör kodlarını 9999 yapmayı deneyen satırlar var. Bölgeye özgü ağ davranışı değişebilir; bazı ayarlar etkisiz kalabilir."),
        Android("android-extra-boost", "Extra Boost", "Yüksek performans", "Performans, CPU/GPU, çizim ve ekran yenileme davranışını yükseltmeyi hedefleyen geniş profil.", "Extra_Boost.bat", "120 Hz ve frekans değerlerini zorlayan ayarlar içerir. Daha fazla pil tüketimi/ısı oluşturabilir; çoğu değer cihaza özgüdür veya Android tarafından yok sayılabilir."),
        Android("android-hardware", "Hardware", "Donanım hızlandırma", "Donanım bileşimi, GPU çizimi ve video hızlandırma bayraklarını etkinleştirmeyi dener.", "Hardware.bat", "Üreticiye ve Android sürümüne bağlı özellikler içerir; desteklenmeyen cihazlarda etkisiz kalabilir veya görüntü davranışını bozabilir."),
        Android("android-lite-ps", "Lite PS", "Hafif güç tasarrufu", "Performans kısıtlaması, uygulama arka planı ve düşük güç davranışını ayarlamayı hedefler.", "Lite_ps.bat", "Arka plan uygulamalarını, bildirimleri ve bazı performans davranışlarını sınırlayabilir."),
        Android("android-power-saving", "Power Saving", "Güç tasarrufu", "Pil/boşta kalma, uygulama davranışı, ağ ve sistem kaynaklarını güç tasarrufu yönünde değiştirmeyi hedefler.", "Power_Saving.bat", "Zorunlu idle ve ağ politikası komutları içerir; bildirimleri, bağlantıyı veya arka plan işlerini etkileyebilir."),
        Android("android-qualcom-only", "Qualcomm only", "Qualcomm cihazları", "Qualcomm/MDP görüntü birleştirme ve yeniden başlatma davranışına ait bayrakları yazar.", "Qualcom_only.bat", "Yalnızca bazı Qualcomm cihazlarında anlamlıdır. Diğer yonga setlerinde uygunsuz veya etkisiz olabilir."),
        Android("android-vulkan", "Vulkan", "Vulkan çizim yolu", "Android arayüz çizimini Vulkan/Skia yoluna yönlendirmeyi dener.", "Vulkan.bat", "Cihazın Vulkan desteğine bağlıdır; uyumsuzluk grafik hatası veya kararsızlığa neden olabilir."),
        Android("android-compile", "Compile", "Uygulama derleme", "Cihazdaki tüm uygulamalar için zorunlu `everything` derlemesini ve arka plan dexopt işini başlatır.", "Compile.bat", "Uzun sürebilir, pil/ısı/depolama kullanır. Android uygulama derleme durumunu otomatik olarak önceki haline döndürmek mümkün değildir.")
    ];

    public static OptimizationProfile? Find(PlatformKind platform, string idOrName) => All.FirstOrDefault(p => p.Platform == platform &&
        (p.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
         (platform == PlatformKind.Windows && p.WindowsScriptMode == "Standard" && idOrName.Equals("balanced", StringComparison.OrdinalIgnoreCase)) ||
         (platform == PlatformKind.Windows && p.WindowsScriptMode == "Aggressive" && idOrName.Equals("performance", StringComparison.OrdinalIgnoreCase)) ||
         (p.Id.StartsWith("android-", StringComparison.OrdinalIgnoreCase) && p.Id[8..].Equals(idOrName, StringComparison.OrdinalIgnoreCase)) ||
         p.Id.Split('-').Last().Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
         p.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase)));
    public static IEnumerable<OptimizationProfile> For(PlatformKind platform) => All.Where(p => p.Platform == platform);
}
