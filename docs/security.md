# Güvenlik yaklaşımı

## Kimlik doğrulama ve yetkilendirme

Kullanıcı parolaları ASP.NET Core `PasswordHasher` ile hash olarak saklanır. Login JWT döndürür; her yetkili istekte imza, issuer, audience, süre, kullanıcının aktifliği ve güncel rolü denetlenir. `Employee`, `Manager` ve `Admin` rolleri controller düzeyinde; belge sahibi, atanmış yönetici ve ilgili versiyon sınırları servis düzeyinde uygulanır. Yetkisiz belgeye erişim bazı uçlarda kaydın varlığını açığa çıkarmamak için 404 döndürür. Admin tüm onayları görüntüleyebilir ancak Manager kararını veremez.

Belge sahibi kendi belgesini ve sürümlerini yönetir. Atanmış Manager yalnızca kendisine yöneltilmiş onayın ilgili belge/versiyon kapsamındaki görüntüleme ve indirme yetkisini alır; başka Manager'ların belgelerine bu yolla erişemez. Global Audit, webhook yönetimi ve operasyon sayaçları Admin'e açıktır.

## Dosya bütünlüğü ve imza

PDF/DOCX yüklemesi 10 MB ile sınırlıdır. Her versiyonun SHA-256 özeti saklanır; imzalama ve doğrulama sırasında dosyanın güncel özetiyle karşılaştırılır. İmza, onaylı versiyonu onaylayan aktif Manager tarafından RSA-PSS/SHA-256 ile üretilir. Doğrulama, saklanan imzayı ve güvenilen yerel açık anahtarı kullanır. Bu uygulama imzası **nitelikli elektronik imza veya PAdES değildir**; gerçek kişi kimliği ve hukuki geçerlilik garantisi vermez.

RFC 3161 zaman damgası, belirli `DocumentSignature` kaydına bağlıdır. Development ortamındaki yerel sağlayıcı kendi test sertifikasını kullanır ve güvenilir harici TSA ile eşdeğer değildir. Harici sağlayıcı yapılandırıldığında HTTPS TSA yanıtı, yapılandırılmış güven sertifikaları ve token içindeki imprint/nonce gibi alanlar doğrulanır; doğrulama yalnızca yapılandırılmış güven çapasının ve uygulamadaki imza bütünlüğünün kapsamındadır. TSA yapılandırması veya sağlayıcı yoksa oluşturma 503 ile sonuçlanabilir.

Mobil imza ekranı **geliştirme amaçlı backend destekli simülasyondur**. Onaylanmış versiyonun atanmış Manager'ı talep açabilir; talep varsayılan 5 dakika geçerlidir. Onay anında parola yeniden doğrulanır ve dosyanın SHA-256 değeri tekrar kontrol edilir. Başarılı onay mevcut RSA-PSS imza servisini kullanır; operatör, SIM veya SMS entegrasyonu yoktur.

## Webhook ve olay güvenliği

Webhook URL'leri HTTPS ve güvenli hedef doğrulamasından geçer. Özel/yerel adresler normalde reddedilir; yalnızca Development/Test için açıkça etkinleştirilen yerel hedef seçeneği vardır. HTTP yönlendirmesi ve proxy kullanımı kapalıdır; bağlantı/yanıt zaman aşımı ve yanıt boyutu sınırı uygulanır. Secret değerleri AES-GCM ile şifreli saklanır, API yanıtlarında dönmez ve gönderilen JSON gövdesi HMAC-SHA256 ile imzalanır. Alıcı, imzayı kendi secret'ıyla doğrulamalı ve `EventId` tekrarlarını işlemelidir.

İşlem verisiyle Outbox/Audit kaydının aynı transaction içinde tutulması olay kaybı riskini azaltır; RabbitMQ/webhook için exactly-once iddiası taşımaz. Teslimat ayrıntıları [entegrasyon belgesindedir](integrations.md).

## Yerel gizli bilgiler

`.env`, `appsettings.Development.json`, anahtar dosyaları ve build çıktıları Git dışında tutulur. README'deki kurulum komutları yerel rastgele anahtar üretir; JWT imzalama anahtarı ve webhook şifreleme anahtarı dokümantasyona yazılmaz. Development demo hesapları yalnızca bu ortamda oluşturulur. Yerel imza ve TSA anahtarlarını silmek, önceden üretilmiş imza ve zaman damgası doğrulamasını etkileyebilir.
