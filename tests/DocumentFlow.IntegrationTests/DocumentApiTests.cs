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
