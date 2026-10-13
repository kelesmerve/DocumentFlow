using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using DocumentFlow.Infrastructure.Signatures;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentFlow.IntegrationTests;

public sealed class SignatureApiTests
{
    private const string PdfType = "application/pdf";

    [Fact]
    public void Production_key_store_does_not_fall_back_to_development_signing_key()
    {
        var config = new ConfigurationBuilder().Build();
        var keyStore = new RsaSigningKeyStore(isDevelopment: false, configuration: config);
        Assert.Null(keyStore.GetCurrentSigningKey());
    }

    [Fact]
    public async Task Approved_manager_signs_once_and_signature_verifies_without_disclosing_private_key()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await LoginAsync(factory, "manager@documentflow.local", "DevManager!2026");
        using var admin = await LoginAsync(factory, "admin@documentflow.local", "DevAdmin!2026");
        var (documentId, versionId) = await CreateApprovedDocumentAsync(factory, owner, manager, "Signed agreement");
        var managerId = await UserIdAsync(factory, "manager@documentflow.local");

        using var signed = await manager.PostAsync(SignRoute(documentId, versionId), content: null);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        using var signedJson = JsonDocument.Parse(await signed.Content.ReadAsStringAsync());
        Assert.Equal("RSA-PSS", signedJson.RootElement.GetProperty("signatureAlgorithm").GetString());
        Assert.Equal("SHA-256", signedJson.RootElement.GetProperty("hashAlgorithm").GetString());
        Assert.Equal(versionId, signedJson.RootElement.GetProperty("documentVersionId").GetGuid());
        Assert.DoesNotContain("PRIVATE KEY", signedJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);

        using var duplicate = await manager.PostAsync(SignRoute(documentId, versionId), content: null);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using var duplicateJson = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(signedJson.RootElement.GetProperty("id").GetGuid(), duplicateJson.RootElement.GetProperty("id").GetGuid());

        using var list = await owner.GetAsync(SignaturesRoute(documentId, versionId));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain("SignatureValue", listJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE KEY", listJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);

        using var verified = await owner.GetAsync(VerifyRoute(documentId, versionId));
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var verifyJson = JsonDocument.Parse(await verified.Content.ReadAsStringAsync());
        var verification = Assert.Single(verifyJson.RootElement.EnumerateArray());
        Assert.Equal("Valid", verification.GetProperty("result").GetString());
        Assert.Equal(managerId, verification.GetProperty("signedByUserId").GetGuid());
        Assert.DoesNotContain("PRIVATE KEY", verifyJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", verifyJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(x => x.Action == AuditAction.DocumentSigned && x.DocumentId == documentId);
        Assert.Equal(managerId, audit.UserId);
        Assert.Equal(versionId, audit.DocumentVersionId);
        Assert.Equal(1, await db.DocumentSignatures.CountAsync(x => x.DocumentId == documentId && x.DocumentVersionId == versionId));

        using var adminSign = await admin.PostAsync(SignRoute(documentId, versionId), content: null);
        Assert.Equal(HttpStatusCode.Forbidden, adminSign.StatusCode);
    }

    [Fact]
    public async Task Signing_requires_assigned_approved_manager_and_version_and_view_access_is_enforced()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await LoginAsync(factory, "manager@documentflow.local", "DevManager!2026");
        using var employee = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var admin = await LoginAsync(factory, "admin@documentflow.local", "DevAdmin!2026");
        using var otherManager = await CreateManagerClientAsync(factory);

        using var created = await UploadAsync(owner, "Awaiting approval", [4, 5, 6]);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var pendingDocId = createdJson.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        var pendingVersionId = await VersionIdAsync(factory, pendingDocId);
        using var pendingManagerSign = await manager.PostAsync(SignRoute(pendingDocId, pendingVersionId), content: null);
        Assert.Equal(HttpStatusCode.NotFound, pendingManagerSign.StatusCode);

        var (approvedDocId, approvedVersionId) = await CreateApprovedDocumentAsync(factory, owner, manager, "Assigned approval");
        using var employeeSign = await employee.PostAsync(SignRoute(approvedDocId, approvedVersionId), content: null);
        Assert.Equal(HttpStatusCode.Forbidden, employeeSign.StatusCode);
        using var otherManagerSign = await otherManager.PostAsync(SignRoute(approvedDocId, approvedVersionId), content: null);
        Assert.Equal(HttpStatusCode.NotFound, otherManagerSign.StatusCode);
        using var wrongVersionSign = await manager.PostAsync(SignRoute(approvedDocId, Guid.NewGuid()), content: null);
        Assert.Equal(HttpStatusCode.NotFound, wrongVersionSign.StatusCode);

        var (inactiveDocId, inactiveVersionId) = await CreateApprovedDocumentAsync(factory, owner, otherManager, "Inactive assigned manager", "other-manager@documentflow.local");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var inactiveId = await db.Users.Where(x => x.Email == "other-manager@documentflow.local").Select(x => x.Id).SingleAsync();
            await db.Users.Where(x => x.Id == inactiveId).ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false));
        }
        using var inactiveManagerSign = await otherManager.PostAsync(SignRoute(inactiveDocId, inactiveVersionId), content: null);
        Assert.Equal(HttpStatusCode.NotFound, inactiveManagerSign.StatusCode);

        using var foreignList = await otherManager.GetAsync(SignaturesRoute(approvedDocId, approvedVersionId));
        Assert.Equal(HttpStatusCode.NotFound, foreignList.StatusCode);
        using var foreignVerify = await otherManager.GetAsync(VerifyRoute(approvedDocId, approvedVersionId));
        Assert.Equal(HttpStatusCode.NotFound, foreignVerify.StatusCode);
        using var adminVerify = await admin.GetAsync(VerifyRoute(approvedDocId, approvedVersionId));
        Assert.Equal(HttpStatusCode.OK, adminVerify.StatusCode);
    }

    [Fact]
    public async Task Verification_detects_file_and_signature_changes_reports_missing_key_and_survives_key_rotation()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await LoginAsync(factory, "manager@documentflow.local", "DevManager!2026");
        var (documentId, versionId) = await CreateApprovedDocumentAsync(factory, owner, manager, "Integrity checks");
        using var sign = await manager.PostAsync(SignRoute(documentId, versionId), content: null);
        Assert.Equal(HttpStatusCode.OK, sign.StatusCode);

        var keyStore = factory.Services.GetRequiredService<IRsaSigningKeyStore>();
        var oldFingerprint = (await SignerFingerprintAsync(factory, documentId, versionId));
        Assert.NotNull(keyStore.RotateDevelopmentKey());
        using (var afterRotation = await owner.GetAsync(VerifyRoute(documentId, versionId)))
        using (var json = JsonDocument.Parse(await afterRotation.Content.ReadAsStringAsync()))
            Assert.Equal("Valid", Assert.Single(json.RootElement.EnumerateArray()).GetProperty("result").GetString());

        string signatureValue;
        string storedFileName;
        var modifiedSignature = Convert.ToBase64String(new byte[] { 9, 8, 7 });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            signatureValue = await db.DocumentSignatures.Where(x => x.DocumentId == documentId && x.DocumentVersionId == versionId).Select(x => x.SignatureValue).SingleAsync();
            storedFileName = await db.DocumentVersions.Where(x => x.Id == versionId).Select(x => x.StoredFileName).SingleAsync();
            await db.DocumentSignatures.Where(x => x.DocumentId == documentId && x.DocumentVersionId == versionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SignatureValue, modifiedSignature));
        }
        using (var signatureChanged = await owner.GetAsync(VerifyRoute(documentId, versionId)))
        using (var json = JsonDocument.Parse(await signatureChanged.Content.ReadAsStringAsync()))
            Assert.Equal("Invalid", Assert.Single(json.RootElement.EnumerateArray()).GetProperty("result").GetString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            await db.DocumentSignatures.Where(x => x.DocumentId == documentId && x.DocumentVersionId == versionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SignatureValue, signatureValue));
        }
        var storageRoot = factory.Services.GetRequiredService<IConfiguration>()["FileStorage:RootPath"]!;
        var filePath = Path.Combine(storageRoot, "documents", documentId.ToString("N"), "1", storedFileName);
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        try
        {
            await File.WriteAllBytesAsync(filePath, [9, 8, 7]);
            using var changedFile = await owner.GetAsync(VerifyRoute(documentId, versionId));
            using var changedFileJson = JsonDocument.Parse(await changedFile.Content.ReadAsStringAsync());
            Assert.Equal("Invalid", Assert.Single(changedFileJson.RootElement.EnumerateArray()).GetProperty("result").GetString());
            using var signingChangedFile = await manager.PostAsync(SignRoute(documentId, versionId), content: null);
            Assert.Equal(HttpStatusCode.Conflict, signingChangedFile.StatusCode);
        }
        finally { await File.WriteAllBytesAsync(filePath, fileBytes); }

        File.Delete(filePath);
        using (var missingFile = await owner.GetAsync(VerifyRoute(documentId, versionId)))
        using (var missingFileJson = JsonDocument.Parse(await missingFile.Content.ReadAsStringAsync()))
            Assert.Equal("Unverifiable", Assert.Single(missingFileJson.RootElement.EnumerateArray()).GetProperty("result").GetString());
        await File.WriteAllBytesAsync(filePath, fileBytes);

        var keyDirectory = factory.Services.GetRequiredService<IConfiguration>()["SignatureSigning:KeyDirectory"]!;
        File.Delete(Path.Combine(keyDirectory, $"{oldFingerprint}.public.pem"));
        using var missingKey = await owner.GetAsync(VerifyRoute(documentId, versionId));
        using var missingKeyJson = JsonDocument.Parse(await missingKey.Content.ReadAsStringAsync());
        Assert.Equal("Unverifiable", Assert.Single(missingKeyJson.RootElement.EnumerateArray()).GetProperty("result").GetString());
    }

    private static async Task<(Guid DocumentId, Guid VersionId)> CreateApprovedDocumentAsync(DocumentApiFactory factory, HttpClient owner, HttpClient manager, string title, string managerEmail = "manager@documentflow.local")
    {
        using var created = await UploadAsync(owner, title, [1, 2, 3]);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var documentId = json.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        var managerId = await UserIdAsync(factory, managerEmail);
        using var submitted = await owner.PostAsJsonAsync($"/api/documents/{documentId}/submit-for-approval", new { managerId });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        using var submittedJson = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync());
        var approvalId = submittedJson.RootElement.GetProperty("approval").GetProperty("id").GetGuid();
        using var approved = await manager.PostAsJsonAsync($"/api/approvals/{approvalId}/approve", new { comment = "Approved for signing" });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        return (documentId, await VersionIdAsync(factory, documentId));
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string title, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "title");
        form.Add(new StringContent("signature integration"), "description");
        form.Add(new StringContent("Legal"), "category");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(PdfType);
        form.Add(file, "file", "agreement.pdf");
        return await client.PostAsync("/api/documents", form);
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<HttpClient> CreateManagerClientAsync(DocumentApiFactory factory)
    {
        const string email = "other-manager@documentflow.local";
        const string password = "OtherManager!2026";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            var user = new User(Guid.NewGuid(), email, "pending", "Other", "Manager", UserRole.Manager, DateTime.UtcNow);
            db.Users.Add(new User(user.Id, email, hasher.HashPassword(user, password), user.FirstName, user.LastName, user.Role, user.CreatedAtUtc));
            await db.SaveChangesAsync();
        }
        return await LoginAsync(factory, email, password);
    }

    private static async Task<Guid> UserIdAsync(DocumentApiFactory factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>().Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
    }

    private static async Task<Guid> VersionIdAsync(DocumentApiFactory factory, Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>().DocumentVersions.Where(x => x.DocumentId == documentId && x.VersionNumber == 1).Select(x => x.Id).SingleAsync();
    }

    private static async Task<string> SignerFingerprintAsync(DocumentApiFactory factory, Guid documentId, Guid versionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>().DocumentSignatures.Where(x => x.DocumentId == documentId && x.DocumentVersionId == versionId).Select(x => x.PublicKeyFingerprint).SingleAsync();
    }

    private static string SignRoute(Guid documentId, Guid versionId) => $"/api/documents/{documentId}/versions/{versionId}/sign";
    private static string SignaturesRoute(Guid documentId, Guid versionId) => $"/api/documents/{documentId}/versions/{versionId}/signatures";
    private static string VerifyRoute(Guid documentId, Guid versionId) => $"/api/documents/{documentId}/versions/{versionId}/verify";
}
