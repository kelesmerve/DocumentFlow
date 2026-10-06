using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentFlow.IntegrationTests;

public sealed class AuthContractTests
{
    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(AuthorizationProbeController).Assembly));
    });

    [Fact]
    public async Task Valid_user_can_login_and_access_me_without_exposing_hash()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "employee@documentflow.local", password = "DevEmployee!2026" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginJson.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadAsStringAsync();
        Assert.Contains("employee@documentflow.local", body);
        Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "employee@documentflow.local", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_requires_authentication()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Role_authorization_allows_only_admin_for_admin_probe()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var employeeToken = await Login(client, "employee@documentflow.local", "DevEmployee!2026");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", employeeToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/test/authorization/admin")).StatusCode);

        var adminToken = await Login(client, "admin@documentflow.local", "DevAdmin!2026");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test/authorization/admin")).StatusCode);
    }

    private static async Task<string> Login(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }
}
