# UMToptimizer

Windows masaüstü arayüzünden ve komut satırından Windows/Android profillerini yöneten açık kaynak C# uygulaması.

## Özellikler

- Görsel tasarım referansını izleyen koyu kontrol merkezi: Genel Bakış, Windows, Android ADB ve Geri Yükleme.
- Aynı profil kataloğu hem WPF arayüzünde hem CLI'da kullanılır.
- Her profil uygulamadan önce önizlenir; arayüzde ayrıca onay istenir. Android önizlemesi kaynak betikteki komut satırlarının tamamını gösterir.
- Koyu liquid-glass görünümü hafif renk geçişleriyle oluşturulur. Sayfa, tıklama ve açılır menü animasyonları kısadır; blur, shader veya sürekli çalışan animasyon döngüsü yoktur.
- EXE ve pencere için çok çözünürlüklü UMToptimizer “U” simgesi kullanılır.
- Uygulama açılışında ADB, uygulama klasörü, Android SDK konumları ve `PATH` içinde otomatik aranır. Android sayfasında `adb.exe seç` ile farklı bir konum gösterebilir ve bu seçimi kaydedebilirsin; yenileme hataları ve telefon yetkilendirme durumu açıklanır.
- Uygulama açık kaldığı sürece Android cihazlarını beş saniyede bir tarar; yeni USB bağlantısını algılayıp Android sayfasını ve cihaz listesini günceller.
- Son işlem bilgisi `%LOCALAPPDATA%\UMToptimizer\last-change.json` dosyasına kaydedilir.
- Windows'ta Standard ve Agresif (Performans) profilleri gömülü PowerShell betiğini yönetici izniyle çalıştırır; Geri Yükleme betiğin yedeklediği kayıt defteri ve hizmet ayarlarını geri yüklemeyi dener.
- Android'de sekiz kaynak profilin (`Balanced`, `Extra Boost`, `Hardware`, `Lite PS`, `Power Saving`, `Qualcomm only`, `Vulkan`, `Compile`) tüm komutları ADB ile gönderilir.
- Android komutları başlamadan cihaz seri numarası sabitlenir; arayüzde komutların tümü gösterilir ve önemli cihaz değişikliği onayı alınır. CLI'da `apply` için `--yes` gerekir. Cihaz/Android sürümünün reddettiği satırlar kısmi uygulama uyarısı olarak gösterilir; tam çıktı `%LOCALAPPDATA%\UMToptimizer\logs` altına yazılır.
- Android profil çalışması arayüzde canlı komut akışı olarak görünür. İşlem bittikten sonra kullanıcı onayıyla cihazı yeniden başlatma düğmesi belirir.
- Kaynak betik profilleri genel ve güvenli geri alma sağlamaz. Geri Yükleme bölümü bu profiller için otomatik geri alma olmadığını belirtir; standart güç planı/ayar profillerinde önceki değerler saklanır.

## Geliştirme

Gereksinim: Windows ve .NET 10 SDK (Visual Studio Installer içinden **.NET desktop development** workload'u da kurulabilir).

```powershell
dotnet build .\UMToptimizer.csproj -c Release
dotnet publish .\UMToptimizer.csproj -c Release -p:UseAppHost=true
```

Yayımlanan dosyalar `bin\Release\net10.0-windows\publish\` altında oluşur. Bu framework-dependent paketi açmak için Windows'ta .NET 10 Desktop Runtime gerekir. Ağ bağlantısı olan bir geliştirme makinesinde `-r win-x64 --self-contained true -p:PublishSingleFile=true` ekleyerek kendi kendine çalışan tek EXE yayınlanabilir.

## Komut satırı

```powershell
UMToptimizer.exe --help
UMToptimizer.exe windows preview performance
UMToptimizer.exe windows apply performance --yes
UMToptimizer.exe android devices
UMToptimizer.exe android preview balanced
UMToptimizer.exe android apply balanced --serial DEVICE_ID --yes
UMToptimizer.exe restore windows
UMToptimizer.exe restore android --serial DEVICE_ID
```

Android kullanımı için USB hata ayıklama izni verilmiş cihaz ve `adb.exe` gerekir. ADB, uygulama klasöründeki `platform-tools\adb.exe` içinde bulunabilir, Android SDK konumlarından veya `PATH` üzerinden çözümlenebilir; `UMTOPT_ADB` ortam değişkeniyle de gösterilebilir. CLI'da `apply` için `--yes` zorunludur; Android'de cihaz seri numarası seçimi açık olmalıdır.

## Kapsam ve güvenlik

Kaynak Android profilleri kullanıcı onayıyla tüm satırları çalıştırır; Android sürümüne/üreticiye özel satırlar başarısız olabilir veya etkisiz kalabilir. Özellikle `Balanced` kaynak betiği Wi-Fi bölge kodunu `JP` ve operatör kodlarını `9999` yapmaya çalışan satırlar içerir. `Power Saving` zorunlu idle/ağ ayarları, `Compile` ise tüm uygulamalar için derleme komutu içerir. Herhangi bir Android profiline basmadan önce tam komut önizlemesini ve uyarıyı okuyun; otomatik geri alma garanti edilmez.

Uygulama, listelenen Android `.bat` içeriklerini proje kaynaklarına kopyalar ve EXE'ye gömer. Aynı isimli `.sh` dosyaları incelenen kaynakta aynı komut içeriğine sahipti; bu nedenle Windows sürümünde `.bat` kaynağı kullanılır. Özgün indirme klasöründeki dosyalar değiştirilmez.

Windows profilleri `Resources\Windows\UMToptimizer-Windows.ps1` kaynağını EXE'ye gömer. Uygulama profili seçildiğinde önce değişiklikleri gösterir ve kullanıcı onayı ister; sonra ayrı PowerShell penceresini yönetici izniyle açar. Betik kendi ek onaylarını da sorar. Agresif profil Edge/OneDrive ve seçili Windows uygulamalarını kaldırabilir; hosts dosyasına çevrimiçi Adobe engelleme listesi ekler. Yedek/geri yükleme, silinen uygulamaları ve dosyaları veya hosts girdilerini tam geri getirmez. Windows betiğinin performans artışı garanti edilmez.

Kod MIT Lisansı ile yayımlanır.
