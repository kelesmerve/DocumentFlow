using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentFlow.IntegrationTests;

public sealed class PerformanceSafetyTests
{
    [Fact]
    public async Task Maximum_allowed_file_streams_with_matching_hash_and_download_content()
    {
        await using var factory = new DocumentApiFactory();
        using var employee = await Login(factory, "employee@documentflow.local", "DevEmployee!2026");
        var bytes = new byte[10 * 1024 * 1024];
        RandomNumberGenerator.Fill(bytes);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Large file boundary"), "title");
        form.Add(new StringContent("Performance"), "category");
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, "file", "boundary.pdf");
        using var created = await employee.PostAsync("/api/documents", form);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var result = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = result.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        var expectedHash = Convert.ToHexString(SHA256.HashData(bytes));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            Assert.Equal(expectedHash, await db.DocumentVersions.Where(x => x.DocumentId == id)
                .Select(x => x.FileHash).SingleAsync());
        }
        using var downloaded = await employee.GetAsync($"/api/documents/{id}/versions/1/download");
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(await downloaded.Content.ReadAsByteArrayAsync())));
    }

    [Fact]
    public async Task Integration_pages_are_bounded_and_operational_metrics_are_admin_only()
    {
        await using var factory = new DocumentApiFactory();
        using var admin = await Login(factory, "admin@documentflow.local", "DevAdmin!2026");
        using var employee = await Login(factory, "employee@documentflow.local", "DevEmployee!2026");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var now = DateTime.UtcNow;
            var adminId = await db.Users.Where(x => x.Email == "admin@documentflow.local")
                .Select(x => x.Id).SingleAsync();
            for (var i = 0; i < 3; i++)
            {
                var endpoint = new WebhookEndpoint(Guid.NewGuid(), $"https://example.com/{i}", "DocumentApproved", "encrypted-test-value", now.AddSeconds(i));
                db.WebhookEndpoints.Add(endpoint);
                db.WebhookDeliveries.Add(new WebhookDelivery(Guid.NewGuid(), Guid.NewGuid(), endpoint.Id,
                    "DocumentApproved", "{}", now.AddSeconds(i)));
                db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), adminId, AuditAction.WebhookCreated,
                    null, null, null, "Performance pagination fixture", now.AddSeconds(i)));
            }
            await db.SaveChangesAsync();
        }

        using var hooksPage = await admin.GetAsync("/api/integrations/webhooks?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, hooksPage.StatusCode);
        using var hooksJson = JsonDocument.Parse(await hooksPage.Content.ReadAsStringAsync());
        Assert.Equal(3, hooksJson.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, hooksJson.RootElement.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("encryptedSecret", hooksJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        using var nextPage = await admin.GetAsync("/api/integrations/webhooks?page=2&pageSize=2");
        using var nextJson = JsonDocument.Parse(await nextPage.Content.ReadAsStringAsync());
        Assert.Single(nextJson.RootElement.GetProperty("items").EnumerateArray());
        using var capped = await admin.GetAsync("/api/integrations/webhooks?page=2147483647&pageSize=10000");
        using var cappedJson = JsonDocument.Parse(await capped.Content.ReadAsStringAsync());
        Assert.Equal(100, cappedJson.RootElement.GetProperty("pageSize").GetInt32());
        using var deliveries = await admin.GetAsync("/api/integrations/webhooks/deliveries?page=1&pageSize=1");
        using var deliveryJson = JsonDocument.Parse(await deliveries.Content.ReadAsStringAsync());
        Assert.Equal(3, deliveryJson.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Single(deliveryJson.RootElement.GetProperty("items").EnumerateArray());
        var deliveryCursorAt = Uri.EscapeDataString(deliveryJson.RootElement.GetProperty("nextAfterCreatedAtUtc").GetString()!);
        var deliveryCursorId = deliveryJson.RootElement.GetProperty("nextAfterId").GetGuid();
        using var nextDelivery = await admin.GetAsync($"/api/integrations/webhooks/deliveries?pageSize=1&afterCreatedAtUtc={deliveryCursorAt}&afterId={deliveryCursorId}");
        Assert.Equal(HttpStatusCode.OK, nextDelivery.StatusCode);
        using var nextDeliveryJson = JsonDocument.Parse(await nextDelivery.Content.ReadAsStringAsync());
        Assert.NotEqual(deliveryCursorId, nextDeliveryJson.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        using var auditPage = await admin.GetAsync("/api/audit?pageSize=1&action=WebhookCreated");
        using var auditJson = JsonDocument.Parse(await auditPage.Content.ReadAsStringAsync());
        var auditCursorAt = Uri.EscapeDataString(auditJson.RootElement.GetProperty("nextAfterCreatedAtUtc").GetString()!);
        var auditCursorId = auditJson.RootElement.GetProperty("nextAfterId").GetGuid();
        using var nextAudit = await admin.GetAsync($"/api/audit?pageSize=1&action=WebhookCreated&afterCreatedAtUtc={auditCursorAt}&afterId={auditCursorId}");
        Assert.Equal(HttpStatusCode.OK, nextAudit.StatusCode);
        using var nextAuditJson = JsonDocument.Parse(await nextAudit.Content.ReadAsStringAsync());
        Assert.NotEqual(auditCursorId, nextAuditJson.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        using var invalidCursor = await admin.GetAsync($"/api/audit?afterId={auditCursorId}");
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);

        using var denied = await employee.GetAsync("/api/operations/metrics");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var metrics = await admin.GetAsync("/api/operations/metrics");
        Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
        using var metricsJson = JsonDocument.Parse(await metrics.Content.ReadAsStringAsync());
        Assert.Equal(3, metricsJson.RootElement.GetProperty("pendingDeliveries").GetInt32());
        Assert.True(metrics.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Single(values!);
    }

    private static async Task<HttpClient> Login(DocumentApiFactory factory, string email, string password)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }
}
