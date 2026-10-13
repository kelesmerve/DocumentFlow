using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentFlow.IntegrationTests;

public sealed class MobileSignatureApiTests
{
    private const string ManagerPassword = "DevManager!2026";

    [Fact]
    public async Task Approved_manager_can_request_and_sign_with_password_once_and_audit_is_consistent()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await Login(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await Login(factory, "manager@documentflow.local", ManagerPassword);
        var (doc, version) = await ApprovedDocument(factory, owner, manager);
        using var created = await manager.PostAsync(CreateRoute(doc, version), null);
        Assert.True(created.StatusCode == HttpStatusCode.OK, $"{created.StatusCode}: {await created.Content.ReadAsStringAsync()}");
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Pending", createdJson.RootElement.GetProperty("status").GetString());
        using var duplicateCreate = await manager.PostAsync(CreateRoute(doc, version), null);
        using var duplicateJson = JsonDocument.Parse(await duplicateCreate.Content.ReadAsStringAsync());
        Assert.Equal(id, duplicateJson.RootElement.GetProperty("id").GetGuid());

        using var wrong = await manager.PostAsJsonAsync($"/api/mobile-signature-requests/{id}/approve", new { password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var approved = await manager.PostAsJsonAsync($"/api/mobile-signature-requests/{id}/approve", new { password = ManagerPassword });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        using var approvedJson = JsonDocument.Parse(await approved.Content.ReadAsStringAsync());
        Assert.Equal("Approved", approvedJson.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("password", approvedJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        var signatureId = approvedJson.RootElement.GetProperty("documentSignatureId").GetGuid();
        using var repeated = await manager.PostAsJsonAsync($"/api/mobile-signature-requests/{id}/approve", new { password = ManagerPassword });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        using var repeatedJson = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync());
        Assert.Equal(signatureId, repeatedJson.RootElement.GetProperty("documentSignatureId").GetGuid());
        using var verify = await owner.GetAsync($"/api/documents/{doc}/versions/{version}/verify");
        using var verifyJson = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        Assert.Equal("Valid", Assert.Single(verifyJson.RootElement.EnumerateArray()).GetProperty("result").GetString());
        using var listed = await owner.GetAsync($"/api/documents/{doc}/versions/{version}/signatures");
        using var listedJson = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        Assert.Equal("MobileSimulation", Assert.Single(listedJson.RootElement.GetProperty("items").EnumerateArray()).GetProperty("method").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        Assert.Equal(1, await db.DocumentSignatures.CountAsync(x => x.DocumentVersionId == version));
        foreach (var action in new[] { AuditAction.MobileSignatureRequested, AuditAction.MobileSignatureApproved,
                     AuditAction.MobileSignatureCompleted, AuditAction.DocumentSigned })
            Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == action && x.DocumentId == doc && x.DocumentVersionId == version));
    }

    [Fact]
    public async Task Only_assigned_active_manager_can_request_or_see_and_rejected_requests_cannot_sign()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await Login(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await Login(factory, "manager@documentflow.local", ManagerPassword);
        using var admin = await Login(factory, "admin@documentflow.local", "DevAdmin!2026");
        using var other = await OtherManager(factory);
        var (doc, version) = await ApprovedDocument(factory, owner, manager);
        using var ownerCreate = await owner.PostAsync(CreateRoute(doc, version), null);
        Assert.Equal(HttpStatusCode.Forbidden, ownerCreate.StatusCode);
        using var adminCreate = await admin.PostAsync(CreateRoute(doc, version), null);
        Assert.Equal(HttpStatusCode.Forbidden, adminCreate.StatusCode);
        using var otherCreate = await other.PostAsync(CreateRoute(doc, version), null);
        Assert.Equal(HttpStatusCode.NotFound, otherCreate.StatusCode);
        using var created = await manager.PostAsync(CreateRoute(doc, version), null);
        Assert.True(created.StatusCode == HttpStatusCode.OK, $"{created.StatusCode}: {await created.Content.ReadAsStringAsync()}");
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        using var otherDetail = await other.GetAsync($"/api/mobile-signature-requests/{id}");
        Assert.Equal(HttpStatusCode.NotFound, otherDetail.StatusCode);
        using var otherApprove = await other.PostAsJsonAsync($"/api/mobile-signature-requests/{id}/approve", new { password = "OtherManager!2026" });
        Assert.Equal(HttpStatusCode.NotFound, otherApprove.StatusCode);
        using var otherList = await other.GetAsync("/api/mobile-signature-requests");
        using var otherListJson = JsonDocument.Parse(await otherList.Content.ReadAsStringAsync());
        Assert.Empty(otherListJson.RootElement.EnumerateArray());
        using var rejected = await manager.PostAsync($"/api/mobile-signature-requests/{id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using var rejectedAgain = await manager.PostAsync($"/api/mobile-signature-requests/{id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, rejectedAgain.StatusCode);
        using var lateApprove = await manager.PostAsJsonAsync($"/api/mobile-signature-requests/{id}/approve", new { password = ManagerPassword });
        Assert.Equal(HttpStatusCode.Conflict, lateApprove.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        Assert.Equal(0, await db.DocumentSignatures.CountAsync(x => x.DocumentVersionId == version));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == AuditAction.MobileSignatureRejected && x.DocumentId == doc));
    }

    [Fact]
    public async Task Expired_or_changed_file_cannot_be_signed_and_concurrent_decisions_have_one_result()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await Login(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await Login(factory, "manager@documentflow.local", ManagerPassword);
        var (doc, version) = await ApprovedDocument(factory, owner, manager);
        using var created = await manager.PostAsync(CreateRoute(doc, version), null);
        Assert.True(created.StatusCode == HttpStatusCode.OK, $"{created.StatusCode}: {await created.Content.ReadAsStringAsync()}");
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            await db.MobileSignatureRequests.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }
        using var expired = await manager.PostAsJsonAsync($"/api/mobile-signature-requests/{id}/approve", new { password = ManagerPassword });
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        using var again = await manager.PostAsync(CreateRoute(doc, version), null);
        using var againJson = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
        var secondId = againJson.RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(id, secondId);

        string path;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var stored = await db.DocumentVersions.Where(x => x.Id == version).Select(x => x.StoredFileName).SingleAsync();
            path = Path.Combine(scope.ServiceProvider.GetRequiredService<IConfiguration>()["FileStorage:RootPath"]!, "documents", doc.ToString("N"), "1", stored);
        }
        var bytes = await File.ReadAllBytesAsync(path);
        try
        {
            await File.WriteAllBytesAsync(path, [7, 8, 9]);
            using var tampered = await manager.PostAsJsonAsync($"/api/mobile-signature-requests/{secondId}/approve", new { password = ManagerPassword });
            Assert.Equal(HttpStatusCode.Conflict, tampered.StatusCode);
        }
        finally { await File.WriteAllBytesAsync(path, bytes); }

        var approveTask = manager.PostAsJsonAsync($"/api/mobile-signature-requests/{secondId}/approve", new { password = ManagerPassword });
        var rejectTask = manager.PostAsync($"/api/mobile-signature-requests/{secondId}/reject", null);
        using var approve = await approveTask;
        using var reject = await rejectTask;
        Assert.Equal(1, new[] { approve.StatusCode, reject.StatusCode }.Count(x => x == HttpStatusCode.OK));
        await using var checkScope = factory.Services.CreateAsyncScope();
        var check = checkScope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        var record = await check.MobileSignatureRequests.AsNoTracking().SingleAsync(x => x.Id == secondId);
        Assert.True(record.Status is MobileSignatureStatus.Approved or MobileSignatureStatus.Rejected);
        Assert.Equal(record.Status == MobileSignatureStatus.Approved ? 1 : 0, await check.DocumentSignatures.CountAsync(x => x.DocumentVersionId == version));
    }

    [Fact]
    public async Task Inactive_manager_and_changed_file_cannot_create_mobile_request()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await Login(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await Login(factory, "manager@documentflow.local", ManagerPassword);
        var (doc, version) = await ApprovedDocument(factory, owner, manager);
        string path;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var stored = await db.DocumentVersions.Where(x => x.Id == version).Select(x => x.StoredFileName).SingleAsync();
            path = Path.Combine(scope.ServiceProvider.GetRequiredService<IConfiguration>()["FileStorage:RootPath"]!, "documents", doc.ToString("N"), "1", stored);
        }
        var bytes = await File.ReadAllBytesAsync(path);
        try
        {
            await File.WriteAllBytesAsync(path, [8, 8, 8]);
            using var changed = await manager.PostAsync(CreateRoute(doc, version), null);
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        }
        finally { await File.WriteAllBytesAsync(path, bytes); }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            await db.Users.Where(x => x.Email == "manager@documentflow.local")
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
        }
        using var inactive = await manager.PostAsync(CreateRoute(doc, version), null);
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        await using var checkScope = factory.Services.CreateAsyncScope();
        var check = checkScope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        Assert.Equal(0, await check.MobileSignatureRequests.CountAsync(x => x.DocumentId == doc));
    }

    private static async Task<(Guid DocumentId, Guid VersionId)> ApprovedDocument(DocumentApiFactory factory, HttpClient owner, HttpClient manager)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Mobile signing test"), "title");
        form.Add(new StringContent("Test"), "description");
        form.Add(new StringContent("Legal"), "category");
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "test.pdf");
        using var created = await owner.PostAsync("/api/documents", form);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var doc = json.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        var managerId = await db.Users.Where(x => x.Email == "manager@documentflow.local").Select(x => x.Id).SingleAsync();
        using var submitted = await owner.PostAsJsonAsync($"/api/documents/{doc}/submit-for-approval", new { managerId });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        using var submittedJson = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync());
        var approvalId = submittedJson.RootElement.GetProperty("approval").GetProperty("id").GetGuid();
        using var approved = await manager.PostAsJsonAsync($"/api/approvals/{approvalId}/approve", new { comment = "OK" });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        return (doc, await db.DocumentVersions.Where(x => x.DocumentId == doc).Select(x => x.Id).SingleAsync());
    }

    private static async Task<HttpClient> Login(DocumentApiFactory factory, string email, string password)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<HttpClient> OtherManager(DocumentApiFactory factory)
    {
        const string email = "other-mobile-manager@documentflow.local";
        const string password = "OtherManager!2026";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User(Guid.NewGuid(), email, "pending", "Other", "Manager", UserRole.Manager, DateTime.UtcNow);
        db.Users.Add(new User(user.Id, email, hasher.HashPassword(user, password), user.FirstName, user.LastName, user.Role, user.CreatedAtUtc));
        await db.SaveChangesAsync();
        return await Login(factory, email, password);
    }

    private static string CreateRoute(Guid doc, Guid version) => $"/api/documents/{doc}/versions/{version}/mobile-signature-requests";
}
