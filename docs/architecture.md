# Sistem mimarisi

DocumentFlow tek bir ASP.NET Core API, React arayüzü ve ayrı bir Integration Worker kullanır. Kalıcı iş verisi PostgreSQL'de, yüklenen dosyalar yapılandırılmış yerel dosya saklama alanında tutulur. RabbitMQ, iş verisinin kaynağı değil; Outbox'a kaydedilmiş entegrasyon olaylarının taşınma katmanıdır. [README'deki sistem diyagramı](../README.md#sistem-mimarisi) bu çalışma zamanı ilişkisini gösterir.

## Clean Architecture katmanları

| Proje | Sorumluluk |
| --- | --- |
| `DocumentFlow.Domain` | `Document`, `DocumentVersion`, `ApprovalRequest`, `AuditLog`, imza ve entegrasyon varlıkları; durumlar ve temel geçiş kuralları |
| `DocumentFlow.Application` | Servis sözleşmeleri, istek/yanıt DTO'ları ve uygulama sınırları |
| `DocumentFlow.Infrastructure` | EF Core/Npgsql, sorgular, yerel dosya saklama, JWT üretimi, RSA-PSS, zaman damgası, RabbitMQ ve webhook uygulamaları |
| `DocumentFlow.Api` | Controller'lar, JWT/RBAC yapılandırması, dependency injection, Development migration/seed ve Outbox publisher'ı |
| `DocumentFlow.IntegrationWorker` | RabbitMQ tüketimi, olayların idempotent kaydı, webhook teslimat işleri ve yeniden deneme döngüsü |
| `frontend` | React sayfaları, rol bazlı gezinme, API istemcisi ve TanStack Query önbelleği |

Proje referansları `Application → Domain`, `Infrastructure → Application + Domain`, `Api → Application + Infrastructure` yönündedir. Worker, Infrastructure bileşenlerini kendi host'unda kullanır. Domain, HTTP veya veritabanı paketlerine bağımlı değildir.

## İstek ve veri akışı

1. React, Vite geliştirme proxy'si üzerinden `/api` isteklerini API'ye gönderir.
2. API, JWT ve rol denetiminden sonra controller'dan ilgili uygulama servisini çağırır. Sahiplik ve atanmış Manager kontrolleri servis sorgularında tekrar uygulanır.
3. Infrastructure, belge meta verisini ve süreç kayıtlarını PostgreSQL'de saklar. PDF/DOCX içerikleri yapılandırılmış yerel saklama alanına yazılır; veritabanındaki versiyon kaydı dosyanın SHA-256 özetini tutar.
4. Onay ve imza işlemleri gerekli Audit/Outbox kayıtlarıyla birlikte veritabanı transaction'ında tamamlanır. API'deki BackgroundService, bekleyen Outbox kayıtlarını RabbitMQ'ya yollar.
5. Integration Worker RabbitMQ olayını alır, ilgili webhook teslimat işlerini PostgreSQL'de güvenli biçimde oluşturur ve HTTPS POST çağrılarını yönetir.

## Tasarım kararları

- **Belge versiyonu birinci sınıf kayıttır.** Onay, imza, indirme audit'i ve zaman damgası belirli versiyon/ilgili imza üzerinden izlenir. [Durum geçişleri](document-workflow.md) ayrı belgede açıklanır.
- **İşlem ile olay kaydı birlikte yazılır.** RabbitMQ erişilemezse onay veya imza veritabanı işlemi RabbitMQ nedeniyle geri alınmaz; olay Outbox'ta bekler. Teslimat ayrıntıları [entegrasyon belgesindedir](integrations.md).
- **Yetki yalnızca arayüze bırakılmaz.** API rol kontrolleri ve servis düzeyinde belge/versiyon erişimi uygulanır. [Güvenlik belgesi](security.md) sınırları açıklar.
- **Okuma sorguları sınırlanır.** Belge, onay, Audit ve webhook teslimat listeleri veritabanında filtrelenip sayfalanır. Audit ve teslimat geçmişi için cursor seçenekleri vardır; salt okunur EF sorgularında projection ve `AsNoTracking` kullanılır.
- **Operasyon gözlemi ayrı tutulur.** API ve worker `System.Diagnostics.Metrics` sayaç/histogramları üretir; Admin `GET /api/operations/metrics` ile Outbox backlog ve webhook teslimat sayılarını görebilir. Bu uç, tüm proses metriklerinin görsel panosu değildir.
