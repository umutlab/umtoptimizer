<div align="center">

# UMToptimizer 4.1

**Windows ve Android (ADB) için açık kaynak optimizasyon kontrol merkezi.**

Güncel sürüm: [**4.1.0**](https://github.com/umutlab/umtoptimizer/releases/tag/v4.1.0)

UMToptimizer Web : https://umtoptimizer.vercel.app/

</div>

> ⚠️ Bu araç sistem ayarlarını değiştirir. Her uygulamadan önce otomatik yedek alır ve tek tıkla geri yükleme sunar; yine de **Agresif** Windows profili bazı uygulamaları kalıcı olarak kaldırır. Uygulamadan önce açıklamaları okuyun.

## İndir

[**Releases**](https://github.com/umutlab/umtoptimizer/releases/latest) sayfasından indirin:

- `UMToptimizer-Setup-4.1.0.exe`: kurulum sihirbazı ("yalnızca benim için" seçilirse yönetici izni istemez).
- `UMToptimizer-4.1.0-portable.exe`: kurulum gerektirmeyen tek dosya.

.NET kurulu olması gerekmez (self-contained). Windows 10 1809 ve üzeri, x64.
Dosyalar imzalı değildir; Windows SmartScreen "bilinmeyen yayımcı" uyarısı gösterebilir ("Ek bilgi → Yine de çalıştır").
Sürüm dosyaları bu depodaki kaynaktan GitHub Actions tarafından derlenir ([release.yml](.github/workflows/release.yml));
indirdiğiniz dosyayı sürüm notlarındaki SHA-256 özetiyle karşılaştırabilirsiniz: `Get-FileHash <dosya>`

## Ne yapar?

| | Profiller |
|---|---|
| **Windows** | **Standart** (önerilen): telemetri, konum, etkinlik geçmişi ve tüketici özelliklerini kapatır, bazı hizmetleri sınırlar, geçici dosyaları temizler. **Agresif**: Standart + animasyon/bildirim azaltma, IPv6/Teredo, Edge/Brave politikaları, Copilot/Recall, Edge/OneDrive ve isteğe bağlı uygulamaları kaldırma, Adobe hosts engelleme listesi. |
| **Android (ADB)** | Balanced, Extra Boost, Hardware, Lite PS, Power Saving, Qualcomm only, Vulkan, Compile. Komutların %95'inden fazlası (`settings`, `setprop`, `device_config`) uygulama öncesi değerleri yedeklendiği için geri alınabilir. |
| **Debloat (Android)** | Telefonla gelen gereksiz sistem uygulamalarını root gerektirmeden kaldırır ve kaldırılanları tek tıkla geri getirir. Her paket için güvenlik düzeyi ve açıklama gösterir. Ayrıntı aşağıda. |

## Debloat (Android)

Kenar çubuğundaki **Debloat (Android)** girişinden (Ctrl+5) ya da Android sayfasındaki **Debloat** düğmesinden açılır.

1. **Paketleri tara:** Cihazdaki sistem uygulamalarını listeler; hiçbir şeyi değiştirmez.
2. **Süz ve seç:** Her paket bir güvenlik düzeyi taşır. Arama kutusu paket adında ve açıklamada arar.
3. **Seçilenleri kaldır:** Onaydan sonra paketler tek bir ADB oturumunda kaldırılır.
4. **Geri getir:** "Kaldırılmış" görünümü kaldırılmış paketleri listeler; bu uygulamayla kaldırılanlar tarihleriyle en üstte durur.

| Düzey | Anlamı |
|---|---|
| **Önerilen** | Kaldırılması güvenli, çoğu kullanıcının ihtiyaç duymadığı paketler |
| **Gelişmiş** | Kaldırılabilir ama bazı özellikler çalışmayabilir |
| **Uzman** | Önemli işlevleri bozabilir |
| **Güvensiz** | Telefonun açılmamasına (bootloop) yol açabilir |
| **Listede yok** | Listede bulunmayan, ne işe yaradığı bilinmeyen paket |

**Nasıl çalışır?** Kaldırma `pm uninstall --user 0 <paket>`, geri getirme `cmd package install-existing --user 0 <paket>` komutunu kullanır.
Paket yalnızca ana kullanıcıdan kaldırılır; APK sistem bölümünde kalır, bu yüzden geri getirilebilir. **Uygulamanın verileri silinir ve geri gelmez.**

**Güvenlik önlemleri**

- Yalnızca sistem paketleri listelenir. Kullanıcının kendi kurduğu uygulamalar kaldırılınca APK da silindiği için geri getirilemez; bu yüzden listeye alınmaz.
- "Görünenleri seç" **Güvensiz** ve **Listede yok** düzeyindeki paketleri toplu seçime asla almaz; bunlar tek tek işaretlenmelidir.
- Seçimde Uzman, Güvensiz ya da listede olmayan paket varsa onay penceresi telefonun açılmayabileceğini ayrıca uyarır. Açılmayan telefonda geri getirme yapılamaz.
- Paket adları cihaz kabuğuna gitmeden önce doğrulanır (yalnızca harf, rakam, nokta, alt çizgi); listedeki tanınmayan güvenlik düzeyi en temkinli hâliyle (Güvensiz) kabul edilir.
- Tarama eksik dönerse (kurulu paket listesi boşsa) hiçbir şey gösterilmez; aksi hâlde her paket "kaldırılmış" görünürdü.
- İşlem iptal edilse ya da kablo çıksa bile o ana kadar kaldırılanlar kayda geçer ve liste cihazın gerçek durumundan yeniden okunur.

Paket açıklamaları ve güvenlik düzeyleri [Universal Android Debloater Next Generation](https://github.com/Universal-Debloater-Alliance/universal-android-debloater-next-generation) topluluk listesinden gelir (`uad_lists.json`, 5.399 paket, İngilizce).

## Cihaz algılama

Cihaz takılınca otomatik algılanır: uygulama `adb track-devices` akışını dinler, buna ek olarak listeyi 10 saniyede bir yeniden sorar.
Telefon yine de görünmezse Android ve Debloat sayfalarındaki **Cihazı bağla** düğmesi listeyi elle yeniler; hazır cihaz bulamazsa ADB sunucusunu yeniden başlatır
ve sonucu söyler (bağlandı / telefonda izin bekliyor / bulunamadı).

## Sürüm 4.1.0'da ne değişti?

| Konu | Sürüm 4.0.0 | Sürüm 4.1.0 |
|---|---|---|
| Gereksiz sistem uygulamaları | Kaldırılamıyordu | **Debloat (Android)** sayfası: tara, güvenlik düzeyine göre süz, kaldır |
| Kaldırılan uygulamayı geri getirme | Yoktu | "Kaldırılmış" görünümünden tek tıkla geri getirme; bu uygulamayla kaldırılanlar tarihleriyle listelenir |
| Paket bilgisi | Yoktu | 5.399 paket için açıklama ve güvenlik düzeyi (Universal Android Debloater NG listesi) |
| Cihaz algılama | Yalnızca `adb track-devices` akışı; akış sessiz kalırsa cihaz ancak uygulama yeniden açılınca görünüyordu | Akışa ek olarak 10 saniyede bir yoklama |
| Cihaz görünmezse | Uygulamayı kapatıp açmak gerekiyordu | **Cihazı bağla** düğmesi: listeyi yeniler, gerekirse ADB sunucusunu yeniden başlatır |
| Testler | 91 xUnit testi | 120 xUnit testi |

Ayrıntılar yukarıdaki [Debloat (Android)](#debloat-android) ve [Cihaz algılama](#cihaz-algılama) bölümlerinde; denenmeyen kısımlar [Bilinen sınırlamalar](#bilinen-sınırlamalar) bölümünde.

## Sürüm 4.0.0'da ne değişti?

Sürüm 3'te her işlem (Windows profili, Android profili, ADB indirme, geri yükleme) başlar başlamaz
**"Object reference not set to an instance of an object"** penceresiyle düşüyor ve düğmeler kilitli kalıyordu:
işlem çalıştırıcı `Window.TaskbarItemInfo` üzerine yazıyordu ama bu nesne hiç tanımlanmamıştı.
Sürüm 4 bu hatayı düzeltmekle kalmaz; arayüz ve çekirdek, **kullanıcının karşısına ham hata çıkmaması** hedefiyle baştan yazıldı.

| Konu | Sürüm 3 | Sürüm 4 |
|---|---|---|
| Hata gösterimi | Ham .NET iletisiyle `MessageBox` | Sayfa içinde sakin bildirim satırı; ayrıntı `errors.log`'a. Uygulama kapanmaz, düğmeler kilitli kalmaz |
| Beklenen durumlar (ADB yok, cihaz yok, izin bekleniyor, UAC reddedildi, internet yok) | Hata penceresi | Düğme zaten kapalıdır ve nedenini yazar; ya da bilgi satırı |
| Onay | `MessageBox` | Pencere içi onay katmanı; odak "Vazgeç"te, Esc vazgeçer |
| Çalışan işlem | Durdurulamaz | Android ve ADB indirme için **İptal et** |
| Arayüz mimarisi | Code-behind (test edilemez) | MVVM; görünüm modeli sahte arka uçla test edilir |
| Arayüz iş parçacığı | Her durum değişiminde tüm yedek JSON'ları diskten okunur | Disk/süreç işi arka planda; yalnızca açılışta ve işlem sonrasında |
| Günlük akışı | Satır başına bir arayüz güncellemesi (Balanced ≈ 1.500) | Saniyede en çok 10 toplu güncelleme |
| `--serial` | Çalışmıyordu (yardım metni basılıyordu) | Çalışır; değer doğrulanır |
| ADB güncelleme | Eski sürüm önce silinir; indirme yarıda kalırsa ADB kaybolur | Yeni sürüm doğrulandıktan sonra yer değiştirir |
| Yedek yazımı | Doğrudan yazım | Atomik (geçici dosya + taşıma) |
| Açılış | Ölçülmemişti | Ölçüldü: ilk açılış ~4 sn, sonrakiler ~1 sn (pencere görünene kadar) |

## Güvenlik

Sürüm 3'teki önlemler korunur: yükseltilmiş betiğin SHA-256 sabitlemesi (TOCTOU), geri yüklemede kayıt defteri/hizmet beyaz listesi,
`%ProgramData%\UMToptimizer` sahiplik ve ACL sıkılaştırması, günlük yolu doğrulaması, HKCU'nun doğru kullanıcıya yönlendirilmesi,
Android geri yüklemede anahtar beyaz listesi ve değer kaçışlama.

Sürüm 4'te eklenenler (bağımsız bir güvenlik incelemesinin bulguları dahil):

| Konu | Düzeltme | Test |
|---|---|---|
| Yönetici betiği kullanıcının yazabildiği klasörlerde çalışıyordu: `%TEMP%` ve `%WINDIR%\Temp` özyinelemeli siliniyor, `store.db` üzerinde izin değiştiriliyordu. Araya konan bir junction ile başka klasörler silinebilir ya da kilitlenebilirdi | Bu adımlar yükseltilmiş betikten **çıkarıldı**. Geçici dosya temizliği ve `store.db` adımı uygulamada, kullanıcının kendi yetkisiyle yapılır; bağlantılar takip edilmez, son 24 saatte değişen dosyalar korunur. Sistem geçici dosyaları Disk Temizleme'ye bırakıldı | `CleanTemp_*`, betik §9 |
| Yükseltilmiş komuta yol ve değerler tırnak içinde gömülüyordu; PowerShell `’ ‘ ‚ ‛` karakterlerini de tırnak sayar. Kullanıcı adı ya da TEMP yolu bunlardan birini içerirse özet denetiminden **önce** komut çalıştırılabilirdi | Tüm değişken değerler Base64 olarak gömülür | `ElevatedCommand_PinsHash_AndNeverEmbedsRawValues`; tırnaklı klasör adıyla gerçek PowerShell testi |
| Yükseltilmiş süreç kullanıcının yazabildiği TEMP klasöründe başlıyor ve `cleanmgr.exe`'yi yalın adla çağırıyordu | Çalışma klasörü `System32`; tam yol | — |
| `%ProgramData%\UMToptimizer`: önceden eklenmiş açık izin kuralları silinmiyordu; sahte `backup.json` "orijinal" sayılıyordu | DACL tek adımda tamamen değiştirilir ve doğrulanır (başarısızsa durur). Klasörün önceki sahibi/izinleri ya da yedeğin sahibi güvenilir değilse yedek kullanılmaz, `backup-untrusted-*.json` olarak ayrılır | Betik §10, §13 (başka hesap gerektiren uçtan uca senaryo denenmedi) |
| Windows yedeği yerinde yazılıyordu; yazım hatası fark edilmeden profil uygulanıyor, yarım kalmış dosya "mevcut yedek" sayılıyordu | Atomik yazım + geri okuyup doğrulama; yedek alınamazsa hiçbir ayar değişmez; okunamayan eski yedek ayrılır | Betik §11–12 |
| Android: okunamayan ya da boş dönen ayar bölümü "ayar önceden yoktu" diye kaydediliyor, geri yükleme o ayarları siliyordu | Okuma eksikse hiçbir değişiklik yapılmaz | `IsCompleteRead_*` |
| Android: geri yüklemede atlanan girdiler yedekle birlikte siliniyordu; aynı ayarı hedefleyen iki komuttan biri başarısızsa yedeği siliniyordu; kilitli/bozuk yedek dosyasının üzerine yazılıyordu | Atlanan girdiler saklanır; yalnızca hiçbir komutu başarılı olmayan ayarın yedeği silinir; okunamayan dosya kenara alınır ya da işlem durur | — |
| Android yedek dosya adı: `a:b` ile `a_b` aynı dosyaya düşüyor, dosyadaki seri denetlenmiyordu | Ada serinin özeti eklenir; seri eşleşmezse yedek kullanılmaz (eski adlı dosyalar hâlâ bulunur) | `SnapshotFile_*`, `LoadSnapshot_*` |
| Adobe hosts listesi: ele geçirilirse `0.0.0.0 github.com` gibi satırlarla ilgisiz siteler engellenebilirdi | Yalnızca adında "adobe" geçen alan adları, en çok 5.000 satır | Betik §6 |
| Cihaz seri numarası `adb`'ye doğrulanmadan gidiyordu; göreli `PATH` girdilerinden `adb.exe` çalıştırılabiliyordu | Güvenli olmayan seri reddedilir; yalnızca tam yollar | `IsSafeSerial_*`, `UnsafeSerial_IsRejected_BeforeAdbRuns` |
| Elle bozulmuş Android yedek JSON'u (null alanlar, çok satırlı değer) | Kullanılamaz girdiler ayıklanır, betik bölünemez | `AllSnapshots_SkipsBrokenFiles_AndNullFields`, `RestoreCommand_RejectsUnsafeInput` |

**Dürüst notlar**

- Yukarıdaki açıkların çoğu **kod incelemesiyle** bulundu; saldırı senaryoları çalıştırılarak doğrulanmadı. "TEMP içindeki junction üzerinden silme" Windows 11'de (PowerShell 5.1.26100) eski kodla denendi ve hedef dosya **silinmedi**; yine de yönetici betiği artık kullanıcı klasörlerine hiç dokunmuyor.
- Geçici dosya temizliği artık yalnızca kullanıcının `%LOCALAPPDATA%\Temp` klasöründeki 24 saatten eski dosyaları siler; `%WINDIR%\Temp` doğrudan silinmez.

**Düzeltilmeyen / kalan riskler**

- Geri yükleme bazı adımlar başarısız olsa da Windows yedeğini arşivler (`backup-restored-*.json` durur ama arayüzden yeniden denenemez).
- Mevcut bir Windows yedeği varken, yeni sürümde tabloya eklenen ayarlar yedeğe eklenmez.
- Başka bir yönetici hesabıyla yükseltmede betik hâlâ kullanıcının kayıt defteri kovanına (`HKEY_USERS\<SID>`) yönetici olarak yazar.
- Adobe listesi sabit bir commit'e bağlı değildir; hosts dosyası sistem ANSI kod sayfasıyla yeniden yazılır.
- `platform-tools` paketi için Google sabit özet yayımlamıyor (güven HTTPS'e dayanır). Setup imzalı değil. GitHub Actions eylemleri ve Inno Setup sürümü sabitlenmedi.
- Aynı kullanıcının yükseltilmemiş ve yükseltilmiş süreçleri arasındaki sınır (UAC) güvenlik sınırı sayılmadı.

## Testler

```powershell
dotnet test tests\UMToptimizer.Tests                                               # 120 xUnit testi
powershell -NoProfile -ExecutionPolicy Bypass -File tests\WindowsScript.Tests.ps1   # 45 betik denetimi
```

| Dosya | Kapsam |
|---|---|
| `CoreTests.cs` | Profil yükleme, `adb devices` ayrıştırma, seri doğrulama, geri alınabilir komut tanıma, ayar çıktısı ayrıştırma, geri yükleme komutu ve enjeksiyon reddi, bozuk yedek dosyaları, komut satırı (`--serial` dahil), eksik okuma reddi, yedek dosya adları, geçici dosya temizliği (junction dahil), betik özeti sabitlemesi ve kurcalanmış betiğin gerçek PowerShell'de reddi. **Debloat:** gömülü listenin yüklenmesi, tanınmayan düzeyin Güvensiz sayılması, paket taraması ayrıştırma, `pm` çıktısından başarı/başarısızlık, paket adı doğrulama ve enjeksiyon reddi, cihaz başına geçmiş |
| `ViewModelTests.cs` | Beklenmeyen istisna → sakin bildirim + düğmeler açık; `UserException` → uyarı; UAC reddi → bilgi; onay reddi → hiçbir şey değişmez; cihaz yokken düğme kapalı + nedeni; günlük/ilerleme toplu güncelleme; iptal; geri yükleme koşulları. **Pencere testi:** gerçek pencere açılır, tema anahtarları doğrulanır, "Profili uygula → onayla → işlem" akışı sahte arka uçla baştan sona çalıştırılır; Debloat sayfası gerçek satırlarla çizilir. **Debloat:** varsayılan süzgeç, arama, toplu seçimin Güvensiz paketleri atlaması, kaldır → geri getir döngüsü, riskli seçimde uyarı, cihazın reddettiği paketler, cihaz çıkınca listenin temizlenmesi. **Cihazı bağla:** izleyicinin kaçırdığı telefonun bulunması |
| `WindowsScript.Tests.ps1` | Betiği gerçek sisteme dokunmadan, `HKCU:\Software\UMToptimizerTest` ve geçici klasörle çalıştırır: yedek → uygula → ikinci yedek ezilmez → kurcalanmış yedek → geri yükle → hosts filtresi → günlük yolu → HKCU yönlendirmesi → bağlantı güvenliği → DACL değiştirme → atomik yedek → bozuk yedek → sahiplik denetimi |

## Bilinen sınırlamalar

Aşağıdakiler **gerçek ortamda uçtan uca** denenmedi:

- **Android profil uygulama ve geri yükleme gerçek bir telefonda denenmedi.** Ayrıştırma, komut üretimi ve arayüz akışı testlerle doğrulandı.
- **Debloat yalnızca tek bir telefonda (Android, OPPO/ColorOS) elle denendi:** 3 sistem paketi kaldırıldı ve 3'ü de geri getirildi. Başka üretici ve Android sürümlerinde denenmedi.
- **Debloat geri getirme Android 8 ve üzerini bekler** (`cmd package install-existing`). Daha eski sürümlerde kaldırılan paket bu uygulamayla geri getirilemeyebilir.
- **Debloat uygulama verilerini siler;** geri getirilen uygulama fabrika sürümüyle ve boş veriyle gelir.
- **Güvensiz düzeydeki bir paketi kaldırmak telefonu açılmaz hâle getirebilir.** Bu durumda geri getirme yapılamaz; fabrika ayarlarına dönmek gerekebilir.
- **Paket açıklamaları İngilizcedir** ve topluluk listesinden gelir; liste eksik ya da hatalı olabilir.
- **"Cihazı bağla" ve 10 saniyelik yoklama gerçek cihazla denenmedi.** Cihazın takılınca görünmemesi geliştirme makinesinde yeniden üretilemedi; düzeltme, izleme akışının sessiz kalması varsayımına dayanır.
- **Windows profillerinin yönetici olarak tam çalışması denenmedi**, çünkü test bilgisayarının ayarlarını değiştirirdi. Yedek/uygula/geri yükle mantığı test anahtarında doğrulandı; hizmetler, AppX kaldırma, DISM gibi adımlar yalnızca kod incelemesinden geçti.
- **Setup GitHub Actions'ta derleniyor ama kurulup elle denenmedi** (sihirbaz, kısayollar, kaldırma). Taşınabilir exe geliştirme makinesinde açılıp denendi.
- **Bazı adımlar geri alınamaz:** kaldırılan uygulamalar (Edge, OneDrive vb.), silinen geçici dosyalar, Explorer klasör görünümü önbelleği ve Android `pm compile`.
- Çalışan bir **Windows** profili arayüzden iptal edilemez (yükseltilmiş süreç yükseltilmemiş uygulamadan durdurulamaz).

## Mimari

```
src/UMToptimizer/
  Program.cs               Argüman yoksa arayüzü, varsa komut satırını başlatır
  App.xaml(.cs)            Fluent tema, ortak stiller, yakalanmamış istisna ağı
  UI/
    MainViewModel.cs       Tüm durum ve eylemler; Backend sınıfı dış dünyaya açılan tek kapı
    MainViewModel.Debloat.cs  Debloat sayfası: tarama, süzme, seçim, kaldırma, geri getirme
    MainWindow.xaml(.cs)   Görünüm; code-behind yalnızca günlük kutusu, odak ve dosya seçici
    Mvvm.cs                Observable, Command, dönüştürücüler
  Core/
    Infra.cs               Yollar, atomik JSON, hata günlüğü, süreç çalıştırıcı, UserException
    Profiles.cs            Profil kataloğu; Android komutları ilk erişimde yüklenir
    Adb.cs                 adb.exe bulma/kurma, track-devices + yoklama, yeniden bağlanma, komut çalıştırma
    Android.cs             Tek oturumda uygulama, ilk-değer yedeği, geri yükleme
    Debloat.cs             Paket taraması, kaldırma/geri getirme, UAD listesi, cihaz başına geçmiş
    WindowsTweaks.cs       Özet doğrulamalı yükseltilmiş çalıştırma, günlüğü canlı izleme
    Cli.cs                 Komut satırı
  Resources/
    Windows/UMToptimizer-Windows.ps1   Tablo tabanlı Windows profilleri
    Android/*.bat                      Android profilleri (değiştirilmedi)
    Android/uad_lists.json             Debloat paket listesi (Universal Android Debloater NG)
tests/                     xUnit + PowerShell betik testleri
installer/UMToptimizer.iss Inno Setup betiği
build.ps1                  Test → publish → setup → SHA-256
```

**Hata kuralı:** Kullanıcıya gösterilebilecek beklenen durumlar `UserException` fırlatır ve iletisi olduğu gibi gösterilir.
Bunun dışındaki her istisna beklenmeyendir: ayrıntısı `errors.log`'a yazılır, kullanıcı tek satırlık sade bir bildirim görür.

## Komut satırı

```
UMToptimizer profiles
UMToptimizer windows apply <standard|aggressive> --yes [--restore-point] [--allow-bitlocker]
UMToptimizer windows restore --yes
UMToptimizer android devices
UMToptimizer android preview <profil>
UMToptimizer android apply <profil> [--serial ID] --yes
UMToptimizer android restore [--serial ID] --yes
UMToptimizer android install-adb
```
Sistemi değiştiren her komut `--yes` olmadan çalışmaz; yalnızca ne yapacağını gösterir.

## Derleme

```powershell
winget install Microsoft.DotNet.SDK.10 JRSoftware.InnoSetup   # bir kez
.\build.ps1                    # test → publish → dist\UMToptimizer-Setup-4.1.0.exe + .sha256
.\build.ps1 -NoSetup           # Inno Setup olmadan: test → publish\UMToptimizer.exe
```

Windows 10 1809 ve üzeri, x64. Uygulama self-contained'dir; .NET kurulu olması gerekmez.

## Veriler nerede tutulur?

| Ne | Nerede |
|---|---|
| Android ilk-değer yedekleri | `%LOCALAPPDATA%\UMToptimizer\android\<seri>.json` |
| Debloat geçmişi (bu uygulamayla kaldırılan paketler ve tarihleri) | `%LOCALAPPDATA%\UMToptimizer\debloat\<seri>-<özet>.json` |
| İndirilen ADB | `%LOCALAPPDATA%\UMToptimizer\platform-tools` |
| Android günlükleri, `errors.log` | `%LOCALAPPDATA%\UMToptimizer\logs` |
| Windows yedeği ve günlükleri | `%ProgramData%\UMToptimizer` (yalnızca yöneticiler yazabilir) |

Program kaldırıldığında bu veriler bilerek silinmez; yeniden kurup geri yükleme yapabilirsiniz.

## Lisans
UMToptimizer'ın kendi kodu MIT lisanslıdır. Ayrıntılar için [LICENSE](LICENSE).

Debloat paket listesi (`uad_lists.json`) Universal Android Debloater Next Generation projesinden alınmıştır ve **GPL-3.0** lisanslıdır;
derlenmiş exe bu listeyi içerir. Kaynak, telif ve dağıtım notu için [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
