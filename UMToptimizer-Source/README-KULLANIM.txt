UMToptimizer Windows x64

Uygulamayı açmak için UMToptimizer.exe dosyasını çalıştırın.
Komut satırı seçenekleri için terminalden: UMToptimizer.exe --help

Uygulama açılışta ADB'yi otomatik arar. Android sayfasında `adb.exe seç`
düğmesiyle farklı bir konumu gösterebilirsin; uygulama yolu kaydeder. Yenileme
hataları ve telefondaki ADB yetkilendirme durumu ekranda açıklanır.
Uygulama açıkken yeni cihaz bağlantıları beş saniyede bir otomatik taranır.
Android profil çalışırken komut akışı ekranda görünür; bittiğinde onayla
cihazı yeniden başlatabilirsin.

Uygulama Windows güç planını değiştirir ve ADB ile bağlanan Android cihazda
seçilen Android profilinin bütün adb shell komutlarını çalıştırır. Android
profilleri: Balanced, Extra Boost, Hardware, Lite PS, Power Saving,
Qualcomm only, Vulkan ve Compile. Uygulamadan önce profili ve tüm komut
önizlemesini inceleyin; önemli cihaz değişikliği onayı gösterilir. Cihazın
desteklemediği komutlar kısmi uygulama uyarısında gösterilir. Ayrıntılı ADB
çıktısı %LOCALAPPDATA%\UMToptimizer\logs altına kaydedilir. Eski betiklerin
tam otomatik geri alması yoktur.

Android için USB hata ayıklama izni ve ADB gerekir. ADB'yi PATH'e ekleyin
veya uygulama klasöründe platform-tools\adb.exe bulundurun.

Bu paket .NET 10 Desktop Runtime gerektirir. Hazır kurulum: https://dotnet.microsoft.com/download/dotnet/10.0/runtime
Kaynak kodu ve MIT lisansı GitHub'da yayımlanabilir.

