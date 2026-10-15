# HTTP API rehberi

Temel yol `/api`'dir. JSON yanıtlarında enum değerleri metin olarak döner. Kimlik isteyen çağrılar `Authorization: Bearer <JWT>` başlığı kullanır. Aşağıdaki liste `src/DocumentFlow.Api/Controllers` route tanımlarından çıkarılmıştır; ayrıntılı şema Development ortamındaki `/swagger` sayfasındadır.

**Rol gösterimi:** `Oturum` geçerli JWT sahibi; `Sahip`, belgenin sahibi; `Atanmış Manager`, ilgili talep/versiyonun yöneticisi anlamına gelir. Rol yeterli olsa bile kaynak bazlı servis kontrolü devam eder.

| Method | Route | Erişim / amaç |
| --- | --- | --- |
| POST | `/api/auth/login` | Herkese açık; JWT alır |
| GET | `/api/auth/me` | Oturum; güncel kullanıcı |
| POST | `/api/documents` | Oturum; multipart belge ve v1 oluşturur |
| GET | `/api/documents` | Oturum; kendi belgeleri, Admin için tüm belgeler; `page`, `pageSize` |
| GET | `/api/documents/{id}` | Oturum; sahip, atanmış Manager veya Admin |
| GET | `/api/documents/{id}/versions/{versionNumber}/download` | Oturum; versiyona erişebilen kullanıcı |
| POST | `/api/documents/{id}/versions` | Sahip; `Draft`/`RevisionRequested` iken multipart yeni versiyon |
| POST | `/api/documents/{id}/submit-for-approval` | Sahip; son `Draft` versiyonunu Manager'a gönderir |
| GET | `/api/documents/{id}/workflow` | Belgeye erişebilen kullanıcı; süreç zaman çizelgesi |
| GET | `/api/users/managers` | Oturum; aktif Manager listesi |
| GET | `/api/approvals` | Manager: kendine atananlar; Admin: tümü; `page`, `pageSize` |
| GET | `/api/approvals/{id}` | Atanmış Manager veya Admin |
| POST | `/api/approvals/{id}/approve` | Yalnız atanmış Manager; karar notu isteğe bağlı |
| POST | `/api/approvals/{id}/reject` | Yalnız atanmış Manager; karar notu zorunlu |
| POST | `/api/approvals/{id}/request-revision` | Yalnız atanmış Manager; karar notu zorunlu |
| POST | `/api/documents/{id}/versions/{versionId}/sign` | İlgili onaylı versiyonun atanmış Manager'ı |
| GET | `/api/documents/{id}/versions/{versionId}/signatures` | Versiyona erişebilen kullanıcı; imza kayıtları |
| GET | `/api/documents/{id}/versions/{versionId}/verify` | Versiyona erişebilen kullanıcı; imza doğrulama |
| POST | `/api/signatures/{signatureId}/timestamp` | İmzalayan Manager veya Admin; RFC 3161 zaman damgası |
| GET | `/api/signatures/{signatureId}/timestamps` | İmza kaydına erişebilen kullanıcı |
| GET | `/api/signatures/{signatureId}/timestamps/{timestampId}/verify` | İmza kaydına erişebilen kullanıcı |
| POST | `/api/documents/{documentId}/versions/{versionId}/mobile-signature-requests` | Onaylı versiyonun atanmış Manager'ı |
| GET | `/api/mobile-signature-requests` | Manager; kendi talepleri |
| GET | `/api/mobile-signature-requests/{id}` | Talebin sahibi Manager |
| POST | `/api/mobile-signature-requests/{id}/approve` | Talebin sahibi Manager; parolayla yeniden doğrulama |
| POST | `/api/mobile-signature-requests/{id}/reject` | Talebin sahibi Manager |
| GET | `/api/audit` | Admin; `action`, `userId`, `documentNumber`, `page`, `pageSize` ve isteğe bağlı UTC cursor |
| GET | `/api/integrations/webhooks` | Admin; webhook listesi, `page`, `pageSize` |
| POST | `/api/integrations/webhooks` | Admin; HTTPS endpoint ve olay abonelikleri ekler |
| PATCH | `/api/integrations/webhooks/{id}/active` | Admin; aktiflik değiştirir |
| DELETE | `/api/integrations/webhooks/{id}` | Admin; kaydı listeden kaldırır |
| POST | `/api/integrations/webhooks/{id}/rotate-secret` | Admin; HMAC secret yeniler |
| GET | `/api/integrations/webhooks/deliveries` | Admin; teslimatlar, `page`, `pageSize` ve isteğe bağlı UTC cursor |
| GET | `/api/operations/metrics` | Admin; Outbox backlog ve webhook teslimat sayaçları |

## Temel istek ve yanıt biçimleri

Login isteği; parolayı gerçek değeriyle yalnızca yerel istemcide gönderin, terminal geçmişine veya dokümantasyona kaydetmeyin:

```http
POST /api/auth/login
Content-Type: application/json

{"email":"employee@documentflow.local","password":"<yerel-parola>"}
```

Başarılı yanıt `accessToken`, `expiresAtUtc` ve `user` alanlarını içerir; `user` içinde `id`, `email`, `firstName`, `lastName`, `role` vardır. `PasswordHash` hiçbir kullanıcı DTO'sunda dönmez.

Belge oluşturma `multipart/form-data` alanları `title`, `description` (isteğe bağlı), `category` ve `file` ile yapılır. Yanıt `201 Created` ve `{ "document": { ... }, "versions": [ ... ] }` yapısındadır. `versions[0].id`, imza ve mobil imza uçlarında kullanılan **GUID versiyon kimliğidir**; indirme uçlarında ise `versionNumber` kullanılır. Dosya PDF/DOCX ve en fazla 10 MB olmalıdır.

Onaya gönderme ve karar gövdeleri:

```json
{"managerId":"<aktif-manager-guid>","comment":"İncelemenizi rica ederim."}
```

```json
{"comment":"Ödeme maddesini güncelleyin."}
```

İlk gövde `/submit-for-approval`, ikinci gövde `/request-revision` veya `/reject` içindir. `/approve` aynı `comment` alanını isteğe bağlı kabul eder. Mobil talep onayı `{"password":"<yerel-parola>"}` gövdesi kullanır; parola saklanmaz.

Webhook oluşturma gövdesi `url`, `eventTypes` (`DocumentApproved`, `DocumentSigned`, `MobileSignatureCompleted`) ve yalnızca oluşturma/yenileme isteğinde verilen `secret` içerir. Liste yanıtı secret'ı içermez. `PATCH .../active` gövdesi `{"isActive":false}`; `POST .../rotate-secret` gövdesi `{"secret":"<yeni-yerel-secret>"}` biçimindedir.

Liste uçları `items`, `page`, `pageSize`, `totalCount` alanlarını döndürür. Audit ve webhook teslimat listeleri ek olarak `nextAfterCreatedAtUtc` ile `nextAfterId` cursor alanları sunar; cursor kullanıldığında ikisi birlikte UTC değerlerle ve `page=1` ile gönderilir.

## HTTP durumları

| Kod | Anlamı |
| --- | --- |
| `200`, `201`, `204` | Başarılı okuma/işlem, belge oluşturma veya gövdesiz webhook değişikliği |
| `400` | Geçersiz gövde, dosya ya da cursor parametresi |
| `401` | JWT yok, geçersiz veya kullanıcı artık aktif/güncel rolde değil; mobil onayda yanlış parola |
| `403` | Rol işleme izin vermiyor |
| `404` | Kaynak yok veya bu kullanıcıya görünür değil |
| `409` | Belge/approval/imza durumu işleme uygun değil veya bütünlük ihlali var |
| `503` | İmza ya da zaman damgası sağlayıcısı veya webhook secret yapılandırması kullanılamıyor |
| `500` | Beklenmeyen sunucu hatası; ayrıntı sızdırmayan `ProblemDetails` |

İlgili yetki kuralları [güvenlik](security.md), onay geçişleri [belge iş akışı](document-workflow.md) belgesindedir.
