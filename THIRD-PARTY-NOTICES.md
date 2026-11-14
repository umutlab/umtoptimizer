# Üçüncü taraf bileşenler

UMToptimizer'ın kendi kaynak kodu [MIT lisansı](LICENSE) altındadır. Aşağıdaki bileşen bu depoda yer alır ama **kendi lisansına tabidir**.

## Universal Android Debloater Next Generation: paket listesi

| | |
|---|---|
| Dosya | `src/UMToptimizer/Resources/Android/uad_lists.json` |
| Kaynak | <https://github.com/Universal-Debloater-Alliance/universal-android-debloater-next-generation> (`resources/assets/uad_lists.json`) |
| Telif hakkı | Universal Debloater Alliance ve katkıda bulunanlar |
| Lisans | GNU General Public License v3.0 (GPL-3.0): <https://www.gnu.org/licenses/gpl-3.0.html> |
| Değişiklik | Yok; dosya olduğu gibi alınmıştır |

Bu dosya Debloat sayfasındaki paket açıklamalarını ve güvenlik düzeylerini sağlar. Uygulama dosyayı derleme sırasında gömülü kaynak olarak içine alır.

**Dağıtanlar için not:** Derlenmiş `UMToptimizer.exe` bu GPL-3.0 lisanslı listeyi içerir. Exe'yi dağıtıyorsanız GPL-3.0'ın koşullarını
(lisans metnine ve bu deponun kaynak koduna erişim sağlamak dahil) gözetmeniz gerekir. Kaynak kodun tamamı bu depoda açıktır.

Listeyi güncellemek için kaynaktaki güncel `uad_lists.json` dosyasını aynı konuma kopyalayın; `DebloatTests.Catalog_LoadsEmbeddedUadList` testi yüklenebildiğini doğrular.
