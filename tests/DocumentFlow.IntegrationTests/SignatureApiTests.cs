using System.Net;
using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using DocumentFlow.Infrastructure.Signatures;
using DocumentFlow.Infrastructure.Timestamps;
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

    [Fact]
    public async Task RFC3161_local_timestamp_is_valid_idempotent_bound_to_signature_and_audited()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await LoginAsync(factory, "manager@documentflow.local", "DevManager!2026");
        using var admin = await LoginAsync(factory, "admin@documentflow.local", "DevAdmin!2026");
        using var otherManager = await CreateManagerClientAsync(factory);
        var (documentId, versionId) = await CreateApprovedDocumentAsync(factory, owner, manager, "RFC3161 timestamp");
        using var signedResponse = await manager.PostAsync(SignRoute(documentId, versionId), content: null);
        Assert.Equal(HttpStatusCode.OK, signedResponse.StatusCode);
        using var signedJson = JsonDocument.Parse(await signedResponse.Content.ReadAsStringAsync());
        var signatureId = signedJson.RootElement.GetProperty("id").GetGuid();

        using var forbiddenCreate = await otherManager.PostAsync(TimestampRoute(signatureId), content: null);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);
        using var ownerCreate = await owner.PostAsync(TimestampRoute(signatureId), content: null);
        Assert.Equal(HttpStatusCode.Forbidden, ownerCreate.StatusCode);
        using var forbiddenList = await otherManager.GetAsync(TimestampListRoute(signatureId));
        Assert.Equal(HttpStatusCode.NotFound, forbiddenList.StatusCode);

        using var created = await manager.PostAsync(TimestampRoute(signatureId), content: null);
        Assert.True(created.StatusCode == HttpStatusCode.OK, $"{created.StatusCode}: {await created.Content.ReadAsStringAsync()}");
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var timestampId = createdJson.RootElement.GetProperty("timestamp").GetProperty("id").GetGuid();
        Assert.Equal("Local", createdJson.RootElement.GetProperty("timestamp").GetProperty("providerType").GetString());
        Assert.Equal("Valid", createdJson.RootElement.GetProperty("verification").GetProperty("result").GetString());
        Assert.DoesNotContain("PRIVATE KEY", createdJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", createdJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("timestampToken", createdJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(createdJson.RootElement.GetProperty("timestamp").TryGetProperty("nonce", out _));

        using var duplicate = await manager.PostAsync(TimestampRoute(signatureId), content: null);
        using var duplicateJson = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(timestampId, duplicateJson.RootElement.GetProperty("timestamp").GetProperty("id").GetGuid());
        using var adminDuplicate = await admin.PostAsync(TimestampRoute(signatureId), content: null);
        Assert.Equal(HttpStatusCode.OK, adminDuplicate.StatusCode);
        using var adminDuplicateJson = JsonDocument.Parse(await adminDuplicate.Content.ReadAsStringAsync());
        Assert.Equal(timestampId, adminDuplicateJson.RootElement.GetProperty("timestamp").GetProperty("id").GetGuid());

        using var list = await owner.GetAsync(TimestampListRoute(signatureId));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var listed = Assert.Single(listJson.RootElement.EnumerateArray());
        Assert.Equal(signatureId, listed.GetProperty("documentSignatureId").GetGuid());

        using var verify = await owner.GetAsync(TimestampVerifyRoute(signatureId, timestampId));
        using var verifyJson = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        Assert.Equal("Valid", verifyJson.RootElement.GetProperty("result").GetString());
        Assert.Equal("SHA-256", verifyJson.RootElement.GetProperty("algorithm").GetString());
        Assert.True(verifyJson.RootElement.GetProperty("genTimeUtc").GetDateTime() <= DateTime.UtcNow);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        var timestamp = await db.DocumentTimestamps.AsNoTracking().SingleAsync(x => x.DocumentSignatureId == signatureId);
        Assert.Equal(signatureId, timestamp.DocumentSignatureId);
        Assert.Equal("Local", timestamp.ProviderType);
        Assert.NotEmpty(Convert.FromBase64String(timestamp.TimestampToken));
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(x => x.Action == AuditAction.TimestampCreated && x.DocumentId == documentId);
        Assert.Equal(versionId, audit.DocumentVersionId);
        Assert.Equal(await db.DocumentSignatures.Where(x => x.Id == signatureId).Select(x => x.SignedByUserId).SingleAsync(), audit.UserId);
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == AuditAction.TimestampCreated && x.DocumentId == documentId));

        var originalToken = timestamp.TimestampToken;
        var malformedToken = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        await db.DocumentTimestamps.Where(x => x.Id == timestampId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.TimestampToken, malformedToken));
        using (var tokenTampered = await owner.GetAsync(TimestampVerifyRoute(signatureId, timestampId)))
        using (var tamperedJson = JsonDocument.Parse(await tokenTampered.Content.ReadAsStringAsync()))
            Assert.Equal("Invalid", tamperedJson.RootElement.GetProperty("result").GetString());
        await db.DocumentTimestamps.Where(x => x.Id == timestampId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.TimestampToken, originalToken));
        await db.DocumentTimestamps.Where(x => x.Id == timestampId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.MessageImprint, new string('0', 64)));
        using (var imprintTampered = await owner.GetAsync(TimestampVerifyRoute(signatureId, timestampId)))
        using (var tamperedJson = JsonDocument.Parse(await imprintTampered.Content.ReadAsStringAsync()))
            Assert.Equal("Invalid", tamperedJson.RootElement.GetProperty("result").GetString());
        await db.DocumentTimestamps.Where(x => x.Id == timestampId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.MessageImprint, timestamp.MessageImprint));
        await db.DocumentTimestamps.Where(x => x.Id == timestampId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.SerialNumber, "123456"));
        using (var serialTampered = await owner.GetAsync(TimestampVerifyRoute(signatureId, timestampId)))
        using (var tamperedJson = JsonDocument.Parse(await serialTampered.Content.ReadAsStringAsync()))
            Assert.Equal("Invalid", tamperedJson.RootElement.GetProperty("result").GetString());
    }

    [Fact]
    public async Task Broken_document_signature_cannot_be_timestamped()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await LoginAsync(factory, "manager@documentflow.local", "DevManager!2026");
        var (documentId, versionId) = await CreateApprovedDocumentAsync(factory, owner, manager, "Broken signature no timestamp");
        using var sign = await manager.PostAsync(SignRoute(documentId, versionId), content: null);
        Assert.Equal(HttpStatusCode.OK, sign.StatusCode);
        using var signJson = JsonDocument.Parse(await sign.Content.ReadAsStringAsync());
        var signatureId = signJson.RootElement.GetProperty("id").GetGuid();
        var brokenSignature = Convert.ToBase64String(new byte[] { 8, 7, 6 });
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        await db.DocumentSignatures.Where(x => x.Id == signatureId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.SignatureValue, brokenSignature));
        using var response = await manager.PostAsync(TimestampRoute(signatureId), content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await db.DocumentTimestamps.Where(x => x.DocumentSignatureId == signatureId).ToListAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync(x => x.Action == AuditAction.TimestampCreated));
    }

    [Fact]
    public async Task Local_provider_makes_genuine_cms_token_and_rejects_nonce_or_token_tampering()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"documentflow-tsa-test-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Timestamping:Local:KeyDirectory"] = directory
            }).Build();
            using var provider = new LocalTimestampProvider(configuration);
            var signature = RandomNumberGenerator.GetBytes(256);
            var nonce = RandomNumberGenerator.GetBytes(20);
            var issued = await provider.IssueAsync(signature, nonce, CancellationToken.None);
            Assert.Equal("Valid", provider.Verify(issued.Token, signature, nonce).Result);
            Assert.Equal("Invalid", provider.Verify(issued.Token, signature, RandomNumberGenerator.GetBytes(20)).Result);
            var tamperedToken = issued.Token.ToArray();
            tamperedToken[^1] ^= 1;
            Assert.NotEqual("Valid", provider.Verify(tamperedToken, signature, nonce).Result);
            Assert.StartsWith("3082", Convert.ToHexString(issued.Token));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
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
    private static string TimestampRoute(Guid signatureId) => $"/api/signatures/{signatureId}/timestamp";
    private static string TimestampListRoute(Guid signatureId) => $"/api/signatures/{signatureId}/timestamps";
    private static string TimestampVerifyRoute(Guid signatureId, Guid timestampId) => $"/api/signatures/{signatureId}/timestamps/{timestampId}/verify";
}
