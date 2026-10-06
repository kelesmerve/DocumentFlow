using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DocumentFlow.IntegrationTests;

public sealed class DocumentApiFactory : WebApplicationFactory<Program>
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"documentflow-integration-{Guid.NewGuid():N}");
    private readonly string _databaseName = $"documentflow_test_{Guid.NewGuid():N}";
    private string? _adminConnectionString;
    private bool _databaseCreated;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var connectionString = configuration.Build().GetConnectionString("DocumentFlow")
                ?? throw new InvalidOperationException("The integration test PostgreSQL connection string is missing.");
            var databaseConnection = new NpgsqlConnectionStringBuilder(connectionString);
            _adminConnectionString = new NpgsqlConnectionStringBuilder(databaseConnection.ConnectionString) { Database = "postgres" }.ConnectionString;
            using (var connection = new NpgsqlConnection(_adminConnectionString))
            {
                connection.Open();
                using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
                command.ExecuteNonQuery();
            }
            _databaseCreated = true;
            databaseConnection.Database = _databaseName;
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DocumentFlow"] = databaseConnection.ConnectionString,
                ["FileStorage:RootPath"] = _storageRoot
            });
        });
        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(AuthorizationProbeController).Assembly);
            services.AddLogging(logging => logging.ClearProviders());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _databaseCreated && _adminConnectionString is not null)
        {
            using var connection = new NpgsqlConnection(_adminConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
            command.ExecuteNonQuery();
            _databaseCreated = false;
        }
        if (disposing && Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }
}
