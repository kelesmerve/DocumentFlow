using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocumentFlow.IntegrationTests;

public sealed class DocumentApiFactory : WebApplicationFactory<Program>
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"documentflow-integration-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:RootPath"] = _storageRoot
        }));
        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(AuthorizationProbeController).Assembly);
            services.AddLogging(logging => logging.ClearProviders());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }
}
