using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentFlow.IntegrationTests;

public sealed class ProductQualityTests
{
    [Fact]
    public async Task Inactive_user_token_is_rejected_on_next_request()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, "employee@documentflow.local", "DevEmployee!2026");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == "employee@documentflow.local");
            db.Entry(user).Property(x => x.IsActive).CurrentValue = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Role_change_invalidates_existing_admin_token()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@documentflow.local", "DevAdmin!2026");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test/authorization/admin")).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == "admin@documentflow.local");
            db.Entry(user).Property(x => x.Role).CurrentValue = DocumentFlow.Domain.UserRole.Employee;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/test/authorization/admin")).StatusCode);
    }

    [Fact]
    public async Task Unexpected_error_returns_safe_problem_details()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/test/authorization/failure");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("Sensitive internal diagnostic marker", body);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AuthenticateAsync(HttpClient client, string email, string password)
    {
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
    }
}
