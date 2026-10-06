using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentFlow.Infrastructure.Persistence;
using DocumentFlow.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentFlow.IntegrationTests;

public sealed class DocumentApiTests
{
    private const string PdfType = "application/pdf";
    private const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [Fact]
    public async Task Create_starts_draft_at_version_one_and_saves_sha256()
    {
        await using var factory = new DocumentApiFactory();
        using var employee = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        var bytes = "%PDF-1.4 test document"u8.ToArray();
        using var response = await UploadDocumentAsync(employee, "Annual policy", "Policies", "../../policy.pdf", PdfType, bytes);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var document = body.RootElement.GetProperty("document");
        var id = document.GetProperty("id").GetGuid();
        Assert.Equal("Draft", document.GetProperty("status").GetString());
        Assert.StartsWith($"DOC-{DateTime.UtcNow:yyyy}-", document.GetProperty("documentNumber").GetString());
        var versions = body.RootElement.GetProperty("versions");
        Assert.Equal(1, versions.GetArrayLength());
        Assert.Equal(1, versions[0].GetProperty("versionNumber").GetInt32());
        Assert.Equal("policy.pdf", versions[0].GetProperty("originalFileName").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        var stored = await db.DocumentVersions.AsNoTracking().SingleAsync(x => x.DocumentId == id);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), stored.FileHash);
        Assert.DoesNotContain("/storage/", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Owner_can_add_versions_download_old_version_and_other_employee_cannot_access_document()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        var otherEmail = $"employee-{Guid.NewGuid():N}@documentflow.local";
        const string otherPassword = "OtherEmployee!2026";
        await AddEmployeeAsync(factory, otherEmail, otherPassword);
        using var otherEmployee = await LoginAsync(factory, otherEmail, otherPassword);
        using var admin = await LoginAsync(factory, "admin@documentflow.local", "DevAdmin!2026");

        var create = await UploadDocumentAsync(owner, "Versioned doc", "Ops", "first.pdf", PdfType, [1, 2, 3]);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        create.Dispose();

        using var v2 = await UploadVersionAsync(owner, id, "second.pdf", PdfType, [4, 5, 6]);
        Assert.True(v2.StatusCode == HttpStatusCode.OK, await v2.Content.ReadAsStringAsync());
        using var v2Body = JsonDocument.Parse(await v2.Content.ReadAsStringAsync());
        Assert.Equal(2, v2Body.RootElement.GetProperty("versions").GetArrayLength());
        Assert.Equal(2, v2Body.RootElement.GetProperty("versions")[1].GetProperty("versionNumber").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await otherEmployee.GetAsync($"/api/documents/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherEmployee.GetAsync($"/api/documents/{id}/versions/1/download")).StatusCode);
        using var forbiddenVersion = await UploadVersionAsync(otherEmployee, id, "foreign.pdf", PdfType, [7]);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenVersion.StatusCode);
        using var adminVersion = await UploadVersionAsync(admin, id, "admin-foreign.pdf", PdfType, [8]);
        Assert.Equal(HttpStatusCode.NotFound, adminVersion.StatusCode);
        using var employeeList = await otherEmployee.GetAsync("/api/documents?page=1&pageSize=100");
        using var employeeListJson = JsonDocument.Parse(await employeeList.Content.ReadAsStringAsync());
        Assert.DoesNotContain(employeeListJson.RootElement.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);

        using var oldVersion = await owner.GetAsync($"/api/documents/{id}/versions/1/download");
        Assert.Equal(HttpStatusCode.OK, oldVersion.StatusCode);
        Assert.Equal(new byte[] { 1, 2, 3 }, await oldVersion.Content.ReadAsByteArrayAsync());
        using var adminView = await admin.GetAsync($"/api/documents/{id}");
        Assert.Equal(HttpStatusCode.OK, adminView.StatusCode);
        using var adminList = await admin.GetAsync("/api/documents?page=1&pageSize=100");
        using var adminListJson = JsonDocument.Parse(await adminList.Content.ReadAsStringAsync());
        Assert.Contains(adminListJson.RootElement.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        using var adminDownload = await admin.GetAsync($"/api/documents/{id}/versions/2/download");
        Assert.Equal(HttpStatusCode.OK, adminDownload.StatusCode);
    }

    [Fact]
    public async Task Simultaneous_uploads_get_distinct_sequential_versions()
    {
        await using var factory = new DocumentApiFactory();
        using var employee = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        var create = await UploadDocumentAsync(employee, "Concurrent", "Ops", "v1.pdf", PdfType, [1]);
        using var body = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        create.Dispose();

        var results = await Task.WhenAll(
            UploadVersionAsync(employee, id, "parallel-a.pdf", PdfType, [2]),
            UploadVersionAsync(employee, id, "parallel-b.pdf", PdfType, [3]));
        try
        {
            foreach (var response in results)
                Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var details = await employee.GetAsync($"/api/documents/{id}");
            using var json = JsonDocument.Parse(await details.Content.ReadAsStringAsync());
            var numbers = json.RootElement.GetProperty("versions").EnumerateArray().Select(x => x.GetProperty("versionNumber").GetInt32()).ToArray();
            Assert.Equal([1, 2, 3], numbers);
        }
        finally
        {
            foreach (var response in results) response.Dispose();
        }
    }

    [Fact]
    public async Task Rejects_oversized_and_unsupported_files_and_paginates_results()
    {
        await using var factory = new DocumentApiFactory();
        using var employee = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");

        using var tooLarge = await UploadDocumentAsync(employee, "Too large", "Ops", "large.pdf", PdfType, new byte[10 * 1024 * 1024 + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        using var unsupported = await UploadDocumentAsync(employee, "Unsupported", "Ops", "image.png", "image/png", [1, 2]);
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        using var mismatch = await UploadDocumentAsync(employee, "Mismatch", "Ops", "file.pdf", DocxType, [1, 2]);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        using var first = await UploadDocumentAsync(employee, "Page A", "Ops", "a.pdf", PdfType, [1]);
        using var second = await UploadDocumentAsync(employee, "Page B", "Ops", "b.docx", DocxType, [2]);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        using var page = await employee.GetAsync("/api/documents?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var pageJson = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        Assert.True(pageJson.RootElement.GetProperty("items").GetArrayLength() == 1, pageJson.RootElement.ToString());
        Assert.Equal(1, pageJson.RootElement.GetProperty("pageSize").GetInt32());
        Assert.True(pageJson.RootElement.GetProperty("totalCount").GetInt32() >= 2);
        using var capped = await employee.GetAsync("/api/documents?page=1&pageSize=1000");
        using var cappedJson = JsonDocument.Parse(await capped.Content.ReadAsStringAsync());
        Assert.Equal(100, cappedJson.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Approval_revision_resubmit_and_final_approval_preserve_each_version_request()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await LoginAsync(factory, "manager@documentflow.local", "DevManager!2026");
        using var admin = await LoginAsync(factory, "admin@documentflow.local", "DevAdmin!2026");
        using var createdResponse = await UploadDocumentAsync(owner, "Workflow", "Legal", "v1.pdf", PdfType, [1, 2]);
        using var created = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync());
        var docId = created.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        var managerId = await GetUserIdAsync(factory, "manager@documentflow.local");

        using var submit = await owner.PostAsJsonAsync($"/api/documents/{docId}/submit-for-approval", new { managerId, comment = "Please review v1" });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        using var submitted = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());
        var approvalId = submitted.RootElement.GetProperty("approval").GetProperty("id").GetGuid();
        Assert.Equal(1, submitted.RootElement.GetProperty("approval").GetProperty("versionNumber").GetInt32());
        using var docPending = await owner.GetAsync($"/api/documents/{docId}");
        using var pendingJson = JsonDocument.Parse(await docPending.Content.ReadAsStringAsync());
        Assert.Equal("PendingApproval", pendingJson.RootElement.GetProperty("document").GetProperty("status").GetString());
        using var duplicate = await owner.PostAsJsonAsync($"/api/documents/{docId}/submit-for-approval", new { managerId });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var versionWhilePending = await UploadVersionAsync(owner, docId, "pending.pdf", PdfType, [9]);
        Assert.Equal(HttpStatusCode.BadRequest, versionWhilePending.StatusCode);

        using var managerDoc = await manager.GetAsync($"/api/documents/{docId}");
        Assert.Equal(HttpStatusCode.OK, managerDoc.StatusCode);
        using var managerDocJson = JsonDocument.Parse(await managerDoc.Content.ReadAsStringAsync());
        Assert.Equal(1, managerDocJson.RootElement.GetProperty("versions").GetArrayLength());
        using var managerFile = await manager.GetAsync($"/api/documents/{docId}/versions/1/download");
        Assert.Equal(HttpStatusCode.OK, managerFile.StatusCode);
        using var otherManager = await CreateUserClientAsync(factory, "SecondManager", "Manager", UserRole.Manager);
        Assert.Equal(HttpStatusCode.NotFound, (await otherManager.GetAsync($"/api/documents/{docId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherManager.GetAsync($"/api/documents/{docId}/versions/1/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherManager.GetAsync($"/api/approvals/{approvalId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherManager.PostAsJsonAsync($"/api/approvals/{approvalId}/approve", new { comment = "not assigned" })).StatusCode);
        using var managerInbox = await manager.GetAsync("/api/approvals");
        using var managerInboxJson = JsonDocument.Parse(await managerInbox.Content.ReadAsStringAsync());
        Assert.Contains(managerInboxJson.RootElement.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == approvalId);
        using var adminInbox = await admin.GetAsync("/api/approvals");
        Assert.Equal(HttpStatusCode.OK, adminInbox.StatusCode);
        using var adminInboxJson = JsonDocument.Parse(await adminInbox.Content.ReadAsStringAsync());
        Assert.Contains(adminInboxJson.RootElement.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == approvalId);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/approvals/{approvalId}/approve", new { comment = "No" })).StatusCode);

        using var revision = await manager.PostAsJsonAsync($"/api/approvals/{approvalId}/request-revision", new { comment = "Update payment terms." });
        Assert.Equal(HttpStatusCode.OK, revision.StatusCode);
        using var afterRevision = await owner.GetAsync($"/api/documents/{docId}");
        using var revisionDoc = JsonDocument.Parse(await afterRevision.Content.ReadAsStringAsync());
        Assert.Equal("RevisionRequested", revisionDoc.RootElement.GetProperty("document").GetProperty("status").GetString());
        var otherEmployeeEmail = $"revision-observer-{Guid.NewGuid():N}@documentflow.local";
        await AddEmployeeAsync(factory, otherEmployeeEmail, "ObserverEmployee!2026");
        using var otherEmployee = await LoginAsync(factory, otherEmployeeEmail, "ObserverEmployee!2026");
        using var foreignRevisionUpload = await UploadVersionAsync(otherEmployee, docId, "foreign.pdf", PdfType, [8]);
        Assert.Equal(HttpStatusCode.NotFound, foreignRevisionUpload.StatusCode);
        using var v2 = await UploadVersionAsync(owner, docId, "v2.pdf", PdfType, [3, 4]);
        Assert.Equal(HttpStatusCode.OK, v2.StatusCode);
        using var v2Json = JsonDocument.Parse(await v2.Content.ReadAsStringAsync());
        Assert.Equal("Draft", v2Json.RootElement.GetProperty("document").GetProperty("status").GetString());
        Assert.Equal(2, v2Json.RootElement.GetProperty("versions").GetArrayLength());
        Assert.Equal("v1.pdf", v2Json.RootElement.GetProperty("versions")[0].GetProperty("originalFileName").GetString());
        Assert.Equal(1, v2Json.RootElement.GetProperty("versions")[0].GetProperty("versionNumber").GetInt32());
        Assert.Equal("v2.pdf", v2Json.RootElement.GetProperty("versions")[1].GetProperty("originalFileName").GetString());
        Assert.Equal(2, v2Json.RootElement.GetProperty("versions")[1].GetProperty("versionNumber").GetInt32());
        using var submitAgain = await owner.PostAsJsonAsync($"/api/documents/{docId}/submit-for-approval", new { managerId });
        Assert.Equal(HttpStatusCode.OK, submitAgain.StatusCode);
        using var submittedAgain = JsonDocument.Parse(await submitAgain.Content.ReadAsStringAsync());
        var secondApprovalId = submittedAgain.RootElement.GetProperty("approval").GetProperty("id").GetGuid();
        Assert.Equal(2, submittedAgain.RootElement.GetProperty("approval").GetProperty("versionNumber").GetInt32());
        using var approve = await manager.PostAsJsonAsync($"/api/approvals/{secondApprovalId}/approve", new { comment = "Looks good." });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        using var finalDoc = await owner.GetAsync($"/api/documents/{docId}");
        using var finalJson = JsonDocument.Parse(await finalDoc.Content.ReadAsStringAsync());
        Assert.Equal("Approved", finalJson.RootElement.GetProperty("document").GetProperty("status").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        var requests = await db.ApprovalRequests.AsNoTracking().Where(x => x.DocumentId == docId).OrderBy(x => x.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, requests.Count);
        var v1Id = await db.DocumentVersions.AsNoTracking().Where(x => x.DocumentId == docId && x.VersionNumber == 1).Select(x => x.Id).SingleAsync();
        Assert.Equal(v1Id, requests[0].DocumentVersionId);
        Assert.Equal(ApprovalStatus.RevisionRequested, requests[0].Status);
        Assert.Equal(ApprovalStatus.Approved, requests[1].Status);
        using var workflow = await owner.GetAsync($"/api/documents/{docId}/workflow");
        using var workflowJson = JsonDocument.Parse(await workflow.Content.ReadAsStringAsync());
        Assert.Equal(7, workflowJson.RootElement.GetArrayLength());
        Assert.Contains(workflowJson.RootElement.EnumerateArray(), x => x.GetProperty("detail").GetString() == "Update payment terms.");
        using var secondDecision = await manager.PostAsJsonAsync($"/api/approvals/{approvalId}/reject", new { comment = "second decision" });
        Assert.Equal(HttpStatusCode.Conflict, secondDecision.StatusCode);
        using var versionAfterApproval = await UploadVersionAsync(owner, docId, "v3.pdf", PdfType, [5]);
        Assert.Equal(HttpStatusCode.BadRequest, versionAfterApproval.StatusCode);
    }

    [Fact]
    public async Task Submission_rejects_non_manager_and_self_and_required_decision_comments_are_enforced()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await LoginAsync(factory, "manager@documentflow.local", "DevManager!2026");
        var employeeId = await GetUserIdAsync(factory, "employee@documentflow.local");
        using var createdResponse = await UploadDocumentAsync(owner, "Invalid routing", "Ops", "route.pdf", PdfType, [1]);
        using var created = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync());
        var docId = created.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        using var employeeAsManager = await owner.PostAsJsonAsync($"/api/documents/{docId}/submit-for-approval", new { managerId = employeeId });
        Assert.Equal(HttpStatusCode.Conflict, employeeAsManager.StatusCode);
        using var managerOwnedResponse = await UploadDocumentAsync(manager, "Manager-owned", "Ops", "self.pdf", PdfType, [1]);
        using var managerOwned = JsonDocument.Parse(await managerOwnedResponse.Content.ReadAsStringAsync());
        var managerOwnedDocId = managerOwned.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        using var self = await manager.PostAsJsonAsync($"/api/documents/{managerOwnedDocId}/submit-for-approval", new { managerId = await GetUserIdAsync(factory, "manager@documentflow.local") });
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        using var managers = await owner.GetAsync("/api/users/managers");
        using var managersJson = JsonDocument.Parse(await managers.Content.ReadAsStringAsync());
        Assert.DoesNotContain(managersJson.RootElement.EnumerateArray(), x => x.GetProperty("email").GetString() == "employee@documentflow.local");
        Assert.DoesNotContain("passwordHash", managersJson.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        var inactiveName = $"Inactive{Guid.NewGuid():N}";
        var inactiveManager = await CreateUserClientAsync(factory, inactiveName, "Manager", UserRole.Manager);
        var inactiveManagerEmail = await GetSingleUserEmailByNameAsync(factory, inactiveName);
        inactiveManager.Dispose();
        var inactiveManagerId = await GetUserIdAsync(factory, inactiveManagerEmail);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            await db.Users.Where(x => x.Id == inactiveManagerId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, false));
        }
        using var managersAfterDeactivate = await owner.GetAsync("/api/users/managers");
        using var managerListAfterDeactivate = JsonDocument.Parse(await managersAfterDeactivate.Content.ReadAsStringAsync());
        Assert.DoesNotContain(managerListAfterDeactivate.RootElement.EnumerateArray(), x => x.GetProperty("id").GetGuid() == inactiveManagerId);
        using var inactiveTarget = await owner.PostAsJsonAsync($"/api/documents/{docId}/submit-for-approval", new { managerId = inactiveManagerId });
        Assert.Equal(HttpStatusCode.Conflict, inactiveTarget.StatusCode);
        var otherEmail = $"other-{Guid.NewGuid():N}@documentflow.local";
        await AddEmployeeAsync(factory, otherEmail, "OtherEmployee!2026");
        using var otherOwner = await LoginAsync(factory, otherEmail, "OtherEmployee!2026");
        using var foreignSubmit = await otherOwner.PostAsJsonAsync($"/api/documents/{docId}/submit-for-approval", new { managerId = await GetUserIdAsync(factory, "manager@documentflow.local") });
        Assert.Equal(HttpStatusCode.Conflict, foreignSubmit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/approvals")).StatusCode);

        var managerId = await GetUserIdAsync(factory, "manager@documentflow.local");
        using var submit = await owner.PostAsJsonAsync($"/api/documents/{docId}/submit-for-approval", new { managerId });
        using var submitBody = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());
        var approvalId = submitBody.RootElement.GetProperty("approval").GetProperty("id").GetGuid();
        using var rejectNoComment = await manager.PostAsJsonAsync($"/api/approvals/{approvalId}/reject", new { comment = " " });
        Assert.Equal(HttpStatusCode.Conflict, rejectNoComment.StatusCode);
    }

    [Fact]
    public async Task Manager_directory_returns_only_active_managers_without_sensitive_properties()
    {
        await using var factory = new DocumentApiFactory();
        using var employee = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        var inactiveName = $"InactiveDirectory{Guid.NewGuid():N}";
        var inactive = await CreateUserClientAsync(factory, inactiveName, "Manager", UserRole.Manager);
        inactive.Dispose();
        var inactiveEmail = await GetSingleUserEmailByNameAsync(factory, inactiveName);
        var inactiveId = await GetUserIdAsync(factory, inactiveEmail);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            await db.Users.Where(x => x.Id == inactiveId).ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false));
        }

        using var response = await employee.GetAsync("/api/users/managers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var managers = json.RootElement.EnumerateArray().ToArray();
        var manager = Assert.Single(managers);
        Assert.Equal("manager@documentflow.local", manager.GetProperty("email").GetString());
        Assert.DoesNotContain(managers, x => x.GetProperty("id").GetGuid() == inactiveId);
        Assert.DoesNotContain("employee@documentflow.local", json.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin@documentflow.local", json.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", json.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Concurrent_decisions_allow_only_one_transition()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var managerA = await LoginAsync(factory, "manager@documentflow.local", "DevManager!2026");
        using var managerB = await LoginAsync(factory, "manager@documentflow.local", "DevManager!2026");
        using var createdResponse = await UploadDocumentAsync(owner, "Concurrent decisions", "Ops", "decision.pdf", PdfType, [1]);
        using var created = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync());
        var docId = created.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        var managerId = await GetUserIdAsync(factory, "manager@documentflow.local");
        using var submit = await owner.PostAsJsonAsync($"/api/documents/{docId}/submit-for-approval", new { managerId });
        using var submitJson = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());
        var approvalId = submitJson.RootElement.GetProperty("approval").GetProperty("id").GetGuid();
        var decisions = await Task.WhenAll(
            managerA.PostAsJsonAsync($"/api/approvals/{approvalId}/approve", new { comment = "Approved" }),
            managerB.PostAsJsonAsync($"/api/approvals/{approvalId}/reject", new { comment = "Rejected" }));
        try { Assert.Single(decisions, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(decisions, x => x.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var result in decisions) result.Dispose(); }

        using var concurrentSubmitDocument = await UploadDocumentAsync(owner, "Concurrent submits", "Ops", "submit.pdf", PdfType, [5]);
        using var concurrentSubmitBody = JsonDocument.Parse(await concurrentSubmitDocument.Content.ReadAsStringAsync());
        var secondDocumentId = concurrentSubmitBody.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        var submits = await Task.WhenAll(
            owner.PostAsJsonAsync($"/api/documents/{secondDocumentId}/submit-for-approval", new { managerId }),
            owner.PostAsJsonAsync($"/api/documents/{secondDocumentId}/submit-for-approval", new { managerId }));
        try { Assert.Single(submits, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(submits, x => x.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var result in submits) result.Dispose(); }
    }

    [Fact]
    public async Task Reject_requires_comment_and_keeps_document_rejected()
    {
        await using var factory = new DocumentApiFactory();
        using var owner = await LoginAsync(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await LoginAsync(factory, "manager@documentflow.local", "DevManager!2026");
        using var create = await UploadDocumentAsync(owner, "Reject flow", "Ops", "reject.pdf", PdfType, [1]);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        var managerId = await GetUserIdAsync(factory, "manager@documentflow.local");
        using var submit = await owner.PostAsJsonAsync($"/api/documents/{id}/submit-for-approval", new { managerId });
        using var submitted = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());
        var approvalId = submitted.RootElement.GetProperty("approval").GetProperty("id").GetGuid();
        using var reject = await manager.PostAsJsonAsync($"/api/approvals/{approvalId}/reject", new { comment = "Missing required attachment." });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        using var document = await owner.GetAsync($"/api/documents/{id}");
        using var json = JsonDocument.Parse(await document.Content.ReadAsStringAsync());
        Assert.Equal("Rejected", json.RootElement.GetProperty("document").GetProperty("status").GetString());
        using var rejectedVersion = await UploadVersionAsync(owner, id, "v2.pdf", PdfType, [2]);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedVersion.StatusCode);
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task AddEmployeeAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User(Guid.NewGuid(), email, "pending", "Test", "Employee", UserRole.Employee, DateTime.UtcNow);
        db.Users.Add(new User(user.Id, email, hasher.HashPassword(user, password), user.FirstName, user.LastName, user.Role, user.CreatedAtUtc));
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> GetUserIdAsync(WebApplicationFactory<Program> factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>().Users.AsNoTracking().Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
    }

    private static async Task<string> GetSingleUserEmailByNameAsync(WebApplicationFactory<Program> factory, string firstName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>().Users.AsNoTracking().Where(x => x.FirstName == firstName).Select(x => x.Email).SingleAsync();
    }

    private static async Task<HttpClient> CreateUserClientAsync(WebApplicationFactory<Program> factory, string firstName, string lastName, UserRole role)
    {
        var email = $"{firstName.ToLowerInvariant()}-{Guid.NewGuid():N}@documentflow.local";
        const string password = "NewManager!2026";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            var user = new User(Guid.NewGuid(), email, "pending", firstName, lastName, role, DateTime.UtcNow);
            db.Users.Add(new User(user.Id, email, hasher.HashPassword(user, password), firstName, lastName, role, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        return await LoginAsync(factory, email, password);
    }

    private static Task<HttpResponseMessage> UploadDocumentAsync(HttpClient client, string title, string category, string fileName, string contentType, byte[] bytes) =>
        client.PostAsync("/api/documents", BuildContent(("title", title), ("description", "integration test"), ("category", category), ("file", fileName, contentType, bytes)));

    private static Task<HttpResponseMessage> UploadVersionAsync(HttpClient client, Guid id, string fileName, string contentType, byte[] bytes) =>
        client.PostAsync($"/api/documents/{id}/versions", BuildContent(("file", fileName, contentType, bytes)));

    private static MultipartFormDataContent BuildContent(params (string Name, string Value)[] fields)
    {
        var content = new MultipartFormDataContent();
        foreach (var (name, value) in fields) content.Add(new StringContent(value), name);
        return content;
    }

    private static MultipartFormDataContent BuildContent(params (string Name, string FileName, string ContentType, byte[] Bytes)[] files)
    {
        var content = new MultipartFormDataContent();
        foreach (var (name, fileName, contentType, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(part, name, fileName);
        }
        return content;
    }

    private static MultipartFormDataContent BuildContent((string Name, string Value) field1, (string Name, string Value) field2, (string Name, string Value) field3, (string Name, string FileName, string ContentType, byte[] Bytes) file)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(field1.Value), field1.Name);
        content.Add(new StringContent(field2.Value), field2.Name);
        content.Add(new StringContent(field3.Value), field3.Name);
        var part = new ByteArrayContent(file.Bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        content.Add(part, file.Name, file.FileName);
        return content;
    }
}
