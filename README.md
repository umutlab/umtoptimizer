<div align="center">

[![Downloads](https://img.shields.io/github/downloads/umutlab/umtoptimizer/total?style=for-the-badge&label=DOWNLOADS&color=22c55e)](https://github.com/umutlab/umtoptimizer/releases)
[![Version](https://img.shields.io/github/v/release/umutlab/umtoptimizer?style=for-the-badge&label=VERSION)](https://github.com/umutlab/umtoptimizer/releases/latest)
[![License](https://img.shields.io/github/license/umutlab/umtoptimizer?style=for-the-badge&label=LICENSE)](LICENSE)
[![Stars](https://img.shields.io/github/stars/umutlab/umtoptimizer?style=for-the-badge&label=STARS)](https://github.com/umutlab/umtoptimizer/stargazers)

# UMToptimizer

UMToptimizer Web : https://umtoptimizer.vercel.app

<img src="UMToptimizer-Source/umtoptimizer-main.png" alt="UMToptimizer uygulama arayüzü" width="850">

**Windows masaüstü arayüzünden ve komut satırından Windows ve Android profillerini yöneten açık kaynaklı C# uygulaması.**

[İndir](https://github.com/umutlab/umtoptimizer/releases) · [Kaynak kod](https://github.com/umutlab/umtoptimizer) · [Sorun bildir](https://github.com/umutlab/umtoptimizer/issues)

</div>

> **Uyarı:** UMToptimizer Windows ve Android ayarlarını değiştirebilir. Bazı işlemler uygulama, dosya veya ağ ayarlarını etkileyebilir ve tam olarak geri alınamayabilir. Her cihazda aynı sonuç alınmaz; FPS, pil ömrü veya performans artışı garanti edilmez. Bir profili çalıştırmadan önce önizlemesini ve risk uyarılarını okuyun.

## İçindekiler

- [Uygulama hakkında](#uygulama-hakkında)
- [Özellikler](#özellikler)
- [Nasıl çalışır?](#nasıl-çalışır)
- [Windows profilleri](#windows-profilleri)
- [Android profilleri](#android-profilleri)
- [Android cihazı bağlama](#android-cihazı-bağlama)
- [Geri yükleme ve günlükler](#geri-yükleme-ve-günlükler)
- [Kurulum ve çalıştırma](#kurulum-ve-çalıştırma)
- [Komut satırı](#komut-satırı)
- [Kaynak koddan derleme](#kaynak-koddan-derleme)
- [Proje yapısı](#proje-yapısı)
- [Kapsam ve güvenlik sınırları](#kapsam-ve-güvenlik-sınırları)
- [Lisans](#lisans)

## Uygulama hakkında

UMToptimizer, Windows bilgisayardan Windows ayarlarını yönetmek ve ADB üzerinden bağlı Android cihaza profil komutları göndermek için geliştirilmiş bir masaüstü uygulamasıdır. WPF arayüzü ve komut satırı aynı profil kataloğunu kullanır. Android profilleri uygulamaya gömülü `.bat` kaynaklarından, Windows profilleri ise gömülü PowerShell betiğinden çalıştırılır.

Uygulama, Android cihazı hızlandırmayı veya pil ömrünü belirli bir oranda artırmayı vaat etmez. Profil açıklamaları, betiklerin neyi denemek üzere hazırlandığını anlatır. Komutların cihaz tarafından kabul edilmesi ve gerçek etkisi Android sürümüne, üretici yazılımına, donanıma ve kullanıcı izinlerine bağlıdır.

## Özellikler

- Koyu kontrol merkezi arayüzü: **Genel Bakış**, **Windows**, **Android ADB** ve **Geri Yükleme** bölümleri.
- Arayüz ve CLI tarafından paylaşılan tek profil kataloğu.
- Profil uygulanmadan önce değişiklik özeti ve uyarıların gösterilmesi; arayüzde ayrıca kullanıcı onayı.
- Android profil önizlemesinde kaynak betiğin komut satırlarının gösterilmesi.
- Hafif renk geçişleriyle oluşturulmuş liquid-glass görünümü; kısa sayfa, tıklama ve açılır menü animasyonları.
- Blur, shader veya sürekli çalışan animasyon döngüsü kullanmayan arayüz.
- EXE ve uygulama penceresi için çok çözünürlüklü UMToptimizer “U” simgesi.
- Uygulama açılışında ADB'nin uygulama klasöründe, Android SDK konumlarında ve `PATH` içinde aranması.
- Android sayfasında **adb.exe seç** ile ADB konumunu elle gösterme ve seçimi saklama.
- USB hata ayıklama yetkilendirme durumunun, ADB yenileme hatalarının ve bağlı cihazların arayüzde gösterilmesi.
- Uygulama açık kaldığı sürece cihazların yaklaşık beş saniyede bir taranması; yeni USB bağlantısında cihaz listesinin güncellenmesi.
- Son işlem bilgisinin `%LOCALAPPDATA%\UMToptimizer\last-change.json` dosyasına kaydedilmesi.
- Android profil komutlarının canlı akış olarak görüntülenmesi ve ayrıntılı işlem günlüğü.
- Profil bittikten sonra kullanıcı onayıyla cihazı yeniden başlatma seçeneği.
- Windows Standard ve Agresif (Performans) profillerinin yönetici izniyle çalıştırılması.

## Nasıl çalışır?

### Arayüz üzerinden

1. **Windows** veya **Android ADB** bölümünü açın.
2. İlgili profili seçin ve gösterilen açıklama, değişiklik özeti ve risk uyarılarını okuyun.
3. Android için bağlı ve yetkilendirilmiş cihazı seçin. Profil önizlemesinde komutları inceleyin.
4. Profil uygulama onayını verin. Windows profilleri yönetici izni ve betiğin kendi ek onaylarını isteyebilir.
5. Android çalışırken komut akışını ve sonuçları uygulama ekranından takip edin. İşlem bitince isterseniz cihazı ayrıca yeniden başlatın.

Android'de uygulama seçilen cihazın seri numarasını işlem boyunca kullanır. Böylece komutlar başlatıldıktan sonra başka cihaz bağlansa bile hedef cihaz değişmez. Profil satırlarından bazıları başarısız olursa kalan satırlar yine çalıştırılabilir; sonuç kısmen uygulanmış olabilir.

### Komut satırı üzerinden

Uygulama `--help` ile komutları listeler. CLI'da profili uygulama komutları için `--yes` parametresi açıkça verilmelidir. Bu parametre kullanıcı onayını komut satırında geçirir; profilin etkilerini veya risklerini azaltmaz. Önce `preview` komutunu çalıştırıp sonucu inceleyin.

## Windows profilleri

Windows profilleri `Resources\Windows\UMToptimizer-Windows.ps1` dosyasındaki betik kullanılarak çalışır. Profil seçildikten sonra uygulama değişiklik özetini sunar ve onay ister. Betik ayrı PowerShell sürecinde yönetici izniyle çalışır ve bazı işlemler için kendi ek onaylarını gösterebilir.

### Standard

Günlük kullanıma yönelik, Windows gizlilik ve sistem davranışı ayarlarını düzenleyen profildir. Betik sürümüne göre telemetri, etkinlik geçmişi, konum ve harita davranışı, bazı arka plan özellikleri ve hizmetler değiştirilebilir. Geçici dosyalar temizlenir ve hazırda bekletme kapatılır. Bazı Dosya Gezgini/Store davranışları ve Defender örnek gönderme ayarı da düzenlenebilir.

Betik kayıt defteri ve hizmet ayarlarını yedeklemeye ve mümkünse Sistem Geri Yükleme noktası oluşturmaya çalışır. Geçici dosyalar ve bazı önbellekler silindiğinden bu işlemlerin tamamı geri getirilemez. BitLocker ile ilgili işlem ayrı kurtarma anahtarı onayı gerektirebilir; anahtar bilinmiyorsa devam etmeyin.

### Agresif (Performans)

Standart profilin değişikliklerine ek olarak daha geniş Windows arayüzü, ağ, tarayıcı ve isteğe bağlı bileşen ayarlarını değiştirmeyi dener. Kaynak betikte IPv6/Teredo, bildirimler, arama ve arka plan davranışları, tarayıcı ilkeleri ve seçili Windows hizmetleriyle ilgili değişiklikler bulunur. OneDrive, Edge, Copilot/AI bileşenleri veya seçili isteğe bağlı uygulamaların kaldırılması denenebilir. Edge profili silinebilir. Betik çevrimiçi bir Adobe alan adı listesini `hosts` dosyasına ekleyip DNS önbelleğini temizleyebilir.

Bu işlemler ağ bağlantısını, VPN'i, tarayıcıları, bildirimleri veya üretici araçlarını etkileyebilir. Kaldırılan uygulamalar, silinen profil verileri ve dosyalar yedekleme/geri yükleme ile geri gelmeyebilir. `hosts` dosyasına eklenen satırların eksiksiz geri alınacağı garanti edilmez. Agresif profilin performans artışı garanti edilmez.

## Android profilleri

Android profilleri ADB ile bağlı telefona gönderilir. Kaynak betiklerdeki komutlar üreticiye ve Android sürümüne göre reddedilebilir, yok sayılabilir veya kısmen uygulanabilir. Profil isimleri ve açıklamaları betiklerin hedefini anlatır; tüm Android cihazlarda aynı davranışı garanti etmez.

| Profil | Hedefi | Dikkat edilmesi gerekenler |
| --- | --- | --- |
| **Balanced** | Günlük pil, uygulama ve performans davranışını dengelemeye çalışır; çalışma zamanı, bellek, ağ, güç ve üretici ayarlarını kapsar. | Kaynak betikte Wi-Fi bölge kodunu `JP`, operatör kodlarını `9999` yapmayı deneyen satırlar vardır. Bölgesel ağ davranışını etkileyebilir. |
| **Extra Boost** | CPU/GPU, çizim ve ekran yenileme davranışını yükseltmeyi dener. | 120 Hz ve frekans gibi cihaza bağlı değerler içerir; ısı ve pil tüketimini artırabilir veya etkisiz kalabilir. |
| **Hardware** | Donanım bileşimi, GPU çizimi ve video hızlandırma bayraklarını değiştirmeyi dener. | Üretici ve Android sürümüne bağlıdır; grafik/video sorunlarına yol açabilir. |
| **Lite PS** | Arka plan ve düşük güç davranışlarını düzenleyerek hafif güç tasarrufu hedefler. | Arka plan uygulamalarını veya bildirimleri kısıtlayabilir. |
| **Power Saving** | Boşta kalma, güç, ağ ve uygulama davranışlarını tasarruf yönünde değiştirmeyi dener. | Ağ ve idle kısıtları bildirimleri, bağlantıyı ve arka plan işlerini etkileyebilir. |
| **Qualcomm only** | Bazı Qualcomm cihazlarında MDP/görüntü birleştirme bayraklarını değiştirir. | Qualcomm olmayan cihazlarda kullanmayın; Qualcomm cihazlarında da destek üretici yazılımına bağlıdır. |
| **Vulkan** | Android arayüz çizimini Vulkan/Skia yoluna yönlendirmeyi dener. | Vulkan sürücü desteğine bağlıdır; grafik hatası veya kararsızlık oluşturabilir. |
| **Compile** | Android'de tüm uygulamalar için `everything` derlemesi/dexopt işlemini başlatır. | Uzun sürebilir ve işlemci, depolama, pil ve sıcaklık üzerinde yük oluşturabilir; otomatik geri alma yoktur. |

Balanced betiği yaklaşık 1.496 boş olmayan komut satırı içerir. Profilin çok sayıda satır çalıştırması, her ayarın telefon tarafından kabul edildiğini göstermez. Komutlar satır satır denenir; hata veren satırlar ve tam çıktı uygulama günlüğüne yazılır.

## Android cihazı bağlama

Android Platform Tools içindeki `adb.exe`, Android cihazıyla bilgisayar arasında bağlantı kurar. UMToptimizer ADB'yi uygulama klasöründeki `platform-tools` yolunda, daha önce seçilen yolda, Android SDK konumlarında veya `PATH` içinde arar. `UMTOPT_ADB` ortam değişkeniyle özel bir ADB yolu da belirtilebilir.

1. [Android SDK Platform Tools'u indirin](https://developer.android.com/tools/releases/platform-tools) ve ZIP arşivini çıkarın.
2. Telefonda **Geliştirici seçenekleri** ve **USB hata ayıklama** özelliğini açın.
3. Telefonu veri aktarabilen USB kablosuyla bağlayıp kilidini açın.
4. Telefonda görünen RSA/USB hata ayıklama izin penceresini kabul edin.
5. UMToptimizer içinde **Cihazları yenile** seçeneğini kullanın. ADB bulunamazsa **adb.exe seç** ile `platform-tools\adb.exe` dosyasını gösterin.
6. Yetkilendirilmiş cihazı listeden seçip profil önizlemesini inceleyin.

`adb devices` çıktısında `unauthorized` görünüyorsa telefondaki izin penceresini onaylayın. Cihaz hiç görünmüyorsa USB kablosunu/portunu, USB modunu, telefon sürücülerini ve USB hata ayıklama ayarını kontrol edin. Birden fazla cihaz bağlıysa doğru seri numarasını seçin.

## Geri yükleme ve günlükler

- Son işlem kaydı: `%LOCALAPPDATA%\UMToptimizer\last-change.json`
- Uygulama ve Android işlem günlükleri: `%LOCALAPPDATA%\UMToptimizer\logs`
- ADB için saklanan yol: `%LOCALAPPDATA%\UMToptimizer\adb-path.txt`
- Windows betiği yedeği: `%ProgramData%\UMToptimizer\backup.json`
- Windows betiği günlüğü: `%ProgramData%\UMToptimizer\windows-tweaks.log`

Windows geri yükleme, betiğin yedeklediği kayıt defteri ve hizmet ayarlarını geri yüklemeyi dener; tam disk veya sistem imajı geri yüklemesi değildir. Silinen dosyaları, kaldırılmış uygulamaları ve tüm `hosts` değişikliklerini geri getirmez. Android kaynak profilleri için genel ve güvenilir bir geri alma yöntemi yoktur; Geri Yükleme bölümü profilin geri alınabilir olup olmadığını belirtir.

## Kurulum ve çalıştırma

GitHub deposunun **Releases** bölümünden Windows ZIP paketini indirin ve ZIP'in tamamını bir klasöre çıkarın. EXE'yi gerekli eşlik eden dosyalarla aynı klasörde tutun. Framework-dependent yayın için Windows'ta **.NET 10 Desktop Runtime** gereklidir. Android profilleri için ayrıca ADB gerekir. Windows profillerini çalıştırırken yönetici izni verin.

Bir uygulamanın GitHub'da kaynak kodunun bulunması, indirdiğiniz EXE'nin o kaynak koddan üretildiğini tek başına kanıtlamaz. Sürüm etiketini ve yayın notlarını inceleyin; isterseniz aşağıdaki adımlarla kaynağı kendiniz derleyin.

## Komut satırı

PowerShell veya Komut İstemi'ni uygulamanın bulunduğu klasörde açın. Aşağıdaki örneklerde `DEVICE_ID` yerine `adb devices` çıktısındaki gerçek seri numarasını yazın.

```powershell
# Kullanım bilgisini göster
UMToptimizer.exe --help

# Bağlı Android cihazları listele
UMToptimizer.exe android devices

# Profil değişikliklerini önizle
UMToptimizer.exe windows preview performance
UMToptimizer.exe android preview balanced

# Önizlemeyi inceledikten sonra profili uygula
UMToptimizer.exe windows apply performance --yes
UMToptimizer.exe android apply balanced --serial DEVICE_ID --yes

# Geri yüklemeyi dene
UMToptimizer.exe restore windows
UMToptimizer.exe restore android --serial DEVICE_ID
```

Android profil adları: `balanced`, `extra-boost`, `hardware`, `lite-ps`, `power-saving`, `qualcom-only`, `vulkan`, `compile`. Windows profil adları CLI yardımında `balanced` ve `performance` olarak listelenir. Android kullanımı için USB hata ayıklama yetkisi verilmiş cihaz ve `adb.exe` gerekir. `--yes`, uygulama onayını komut satırında geçirir; komutları gözden geçirme sorumluluğunu ortadan kaldırmaz.

## Kaynak koddan derleme

Gereksinimler: Windows ve .NET 10 SDK. Visual Studio kullanılıyorsa **.NET desktop development** iş yükü kurulabilir.

`UMToptimizer.csproj` dosyasının bulunduğu kaynak dizininde çalıştırın:

```powershell
dotnet restore .\UMToptimizer.csproj
dotnet build .\UMToptimizer.csproj -c Release
dotnet publish .\UMToptimizer.csproj -c Release -p:UseAppHost=true
```

Yayımlama çıktısı `bin\Release\net10.0-windows\publish\` altında oluşur. Bu framework-dependent paketi çalıştırmak için .NET 10 Desktop Runtime gerekir. Tek dosyalı, kendi kendine çalışan `win-x64` EXE üretmek için ağ bağlantısı olan geliştirme makinesinde aşağıdaki komut kullanılabilir:

```powershell
dotnet publish .\UMToptimizer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Tek dosya yayımlama biçimi kaynak proje ayarlarına ve .NET SDK sürümüne göre farklı ek çıktılar üretebilir; dağıtımdan önce publish klasörünün tamamını kontrol edin.

## Proje yapısı

```text
UMToptimizer-Source/
├── App.xaml / App.xaml.cs             # WPF uygulama başlangıcı
├── MainWindow.xaml                    # Ana pencere arayüzü
├── MainWindow.xaml.cs                 # Sayfalar ve kullanıcı etkileşimleri
├── Program.cs                         # GUI ve CLI başlangıcı
├── Models/                            # Profil, cihaz ve işlem modelleri
├── Services/
│   ├── CliRunner.cs                   # Komut satırı komutları
│   ├── OptimizerService.cs             # Profil çalıştırma ve ADB keşfi
│   ├── ProcessRunner.cs                # Süreç başlatma ve çıktı akışı
│   ├── ProfileCatalog.cs               # Profil kataloğu, açıklamalar ve riskler
│   └── UndoStore.cs                    # Geri yükleme kayıtları
├── Resources/
│   ├── Android/*.bat                   # ADB ile telefona gönderilen profiller
│   └── Windows/UMToptimizer-Windows.ps1 # Windows profil betiği
├── Assets/                             # Simge ve görsel kaynakları
└── UMToptimizer.csproj                 # .NET/WPF ve gömülü kaynak ayarları
```

## Kapsam ve güvenlik sınırları

- Bu proje Windows masaüstü uygulamasıdır. Android profil komutları Windows'ta çalışan ADB üzerinden Android cihaza gönderilir.
- Kaynak Android `.bat` dosyaları uygulamaya gömülür. Windows uygulaması bu nedenle Android profilinde Windows'a uygun `.bat` kaynaklarını kullanır; özgün indirme klasöründeki dosyaları değiştirmez.
- Aynı isimli `.sh` dosyalarının kaynak içerikleri incelenen sürümde `.bat` dosyalarıyla aynı komutlara sahipti; bu Windows uygulaması `.bat` kaynaklarını kullanır.
- Android üretici yazılımı, işletim sistemi sürümü ve güvenlik politikaları komutları reddedebilir veya yok sayabilir. Komutların denenmesi, tüm değişikliklerin başarıyla uygulandığını göstermez.
- Android profillerinin çoğunda otomatik geri alma yoktur. Özellikle ağ bölgesi, operatör kodu, güç politikası ve derleme komutlarını uygulamadan önce önizlemeyi okuyun.
- Agresif Windows profili uygulama kaldırabilir ve ağ/hosts ayarlarını değiştirebilir. Tam geri yükleme garantisi yoktur.
- Bu yazılım belirli bir performans, pil ömrü, sıcaklık veya FPS kazanımı vaat etmez.

## Lisans

UMToptimizer [MIT Lisansı](LICENSE) altında yayımlanır.
