# Demo senaryosu

Bu senaryo Development ortamındaki üç demo rolüyle, gerçek arayüz sayfaları ve API iş kuralları üzerinden ilerler. Kurulum için [README](../README.md#kurulum-ve-çalıştırma) izlenir. PDF veya DOCX biçiminde 10 MB altı, hassas veri içermeyen iki örnek dosya hazırlayın. Demo parolaları bu belgede yer almaz.

| Sıra | Rol ve ekran | İşlem | Beklenen sonuç |
| --- | --- | --- | --- |
| 1 | Employee · `/login`, Dashboard `/` | Development Employee hesabıyla giriş yapın. | JWT oturumu açılır; genel bakış görünür. |
| 2 | Employee · `/documents/new` | Örnek PDF/DOCX yükleyerek belge oluşturun. | Belge `Draft`, ilk dosya `v1` olur; detay `/documents/{id}` açılır. |
| 3 | Employee · belge detayı | **Onaya Gönder** ile aktif Manager seçip not ekleyin. | Son versiyona bağlı ayrı `ApprovalRequest` oluşur; durum `PendingApproval` olur. |
| 4 | Manager · `/approvals` ve `/approvals/{id}` | Manager hesabına geçip kendisine atanmış talebi ve dosyayı inceleyin. | Yalnızca atanmış talep ve ilgili belge/versiyon görünür. |
| 5 | Manager · onay detayı | Zorunlu karar notuyla **Revizyon İste** seçin. | Talep ve belge `RevisionRequested` olur; not süreç geçmişinde görünür. |
| 6 | Employee · belge detayı, Versiyonlar | Employee hesabına dönüp güncellenmiş dosyayı yükleyin. | `v2` oluşur, `v1` korunur ve belge `Draft` durumuna döner. |
| 7 | Employee · belge detayı | `v2` için yeniden onaya gönderin. | İlk talep korunur; `v2` için yeni bekleyen talep oluşur. |
| 8 | Manager · `/approvals` | Yeni talebi **Onayla** ile sonuçlandırın. | Belge `Approved`; onaylanan sürüm `v2` olur. |
| 9 | Manager · belge detayı, Dijital imza | `v2` için doğrudan dijital imza oluşturup **İmzayı doğrula** seçin. | `RSA-PSS` imza kaydı ve doğrulama sonucu görünür; belge durumu `Approved` kalır. |
| 10 | Manager · aynı imza alanı | İmza için zaman damgası oluşturun ve doğrulamayı gösterin. | Development'ta yerel RFC 3161 sağlayıcısı kullanılır; bu güvenilir harici TSA iddiası değildir. |
| 11 | Admin · `/audit` | Audit filtreleriyle belge ve imza işlemlerini arayın. | Yapılandırılmış işlem kayıtları yeni tarih önce olacak şekilde görünür. |
| 12 | Admin · `/integrations` | Webhook listesi, abonelikler ve son teslimatları inceleyin. | Secret listede görünmez; yalnızca gerçekten oluşmuş teslimatlar gösterilir. |
| 13 | Admin · Development Swagger | `GET /api/operations/metrics` çağrısını Admin JWT ile yapın. | Outbox backlog, bekleyen ve başarısız webhook teslimat sayaçları döner. Bu metrikler için ayrı frontend sayfası yoktur. |

## Mobil imza için ayrı akış

Doğrudan imzalanmış `v2` üzerinde aynı Manager için ikinci imza talebi açmayın. Bunun yerine başka bir belge oluşturup Manager'a onaylatın. Manager, onaylanmış versiyonun detayından **Mobil İmza İste** seçer ve `/mobile-sign` ekranında kendisine ait bekleyen talebi görür. Süre dolmadan parolasını yeniden girerek onaylar; backend mevcut RSA-PSS imza servisiyle `MobileSimulation` yöntemli imza oluşturur. Belge detayındaki imza listesinde yöntem ve doğrulama görünür. Ayrı bir talepte **Reddet** akışı gösterilebilir. Bu ekran gerçek operatör/SIM veya SMS mobil imzası değildir.

## Entegrasyon gösterimi

Gerçek webhook teslimatı göstermek için Admin tarafından güvenli, erişilebilir bir **HTTPS test alıcısı** tanımlanmış ve `integration-worker` çalışıyor olmalıdır. Alıcı, HMAC başlığını kendi secret'ıyla doğrulayıp `EventId` tekrarlarını idempotent işlemelidir. Belge onayı ve imza sonrasında RabbitMQ Management UI, `/integrations` teslimat tablosu ve Admin operasyon sayaçları birlikte incelenebilir. Dış alıcı yoksa teslimat başarısı varsayılmamalıdır; kayıtların mevcut durumu gösterilir.

## Ekran görüntüleri

Gerçek görüntüleri alma sırası, dosya adları ve hassas verileri gizleme notları [ekran görüntüsü rehberindedir](screenshots/README.md). Depoda henüz görsel dosya bulunmadığından README'ye görüntü bağlantısı eklenmemiştir.
