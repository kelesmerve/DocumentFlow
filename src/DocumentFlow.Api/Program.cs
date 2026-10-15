using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using DocumentFlow.Application.Authentication;
using DocumentFlow.Application.Approvals;
using DocumentFlow.Application.Audit;
using DocumentFlow.Application.Documents;
using DocumentFlow.Application.Signatures;
using DocumentFlow.Application.Integrations;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Authentication;
using DocumentFlow.Infrastructure.Approvals;
using DocumentFlow.Infrastructure.Audit;
using DocumentFlow.Infrastructure.Documents;
using DocumentFlow.Infrastructure.Persistence;
using DocumentFlow.Infrastructure.Storage;
using DocumentFlow.Infrastructure.Signatures;
using DocumentFlow.Application.Timestamps;
using DocumentFlow.Infrastructure.Timestamps;
using DocumentFlow.Infrastructure.Integrations;
using DocumentFlow.Api.Hosted;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options => options.AddPolicy("FrontendDevelopment", policy =>
    policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "DocumentFlow API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Description = "Bearer {token}" });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>() });
});
builder.Services.AddDbContext<DocumentFlowDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("DocumentFlow")));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? throw new InvalidOperationException("JWT configuration is missing.");
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32) throw new InvalidOperationException("JWT signing key must contain at least 32 bytes.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Audience, ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)), ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), RoleClaimType = System.Security.Claims.ClaimTypes.Role, NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var idValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleValue = context.Principal?.FindFirstValue(ClaimTypes.Role);
            if (!Guid.TryParse(idValue, out var id) || roleValue is null)
            {
                context.Fail("Invalid identity.");
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<DocumentFlowDbContext>();
            var currentRole = await db.Users.AsNoTracking()
                .Where(user => user.Id == id && user.IsActive)
                .Select(user => (UserRole?)user.Role)
                .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (currentRole?.ToString() != roleValue) context.Fail("Identity is no longer active.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<DevelopmentUserSeeder>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IApprovalService, ApprovalService>();
builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();
builder.Services.AddSingleton<IRsaSigningKeyStore>(services => new RsaSigningKeyStore(
    builder.Environment.IsDevelopment(), services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()));
builder.Services.AddScoped<IDocumentSignatureService, DocumentSignatureService>();
builder.Services.AddScoped<IMobileSignatureService, MobileSignatureService>();
builder.Services.AddSingleton<IOutboxTransport, RabbitEventPublisher>();
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddScoped<WebhookSecretProtector>();
builder.Services.AddScoped<WebhookTargetPolicy>();
builder.Services.AddScoped<IWebhookManagementService, WebhookManagementService>();
var timestampProviderType = builder.Configuration["Timestamping:ProviderType"] ?? (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test") ? "Local" : "ExternalRfc3161");
if (timestampProviderType == "Local")
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test"))
        throw new InvalidOperationException("Local timestamp provider is only permitted in Development and Test environments.");
    builder.Services.AddSingleton<LocalTimestampProvider>();
    builder.Services.AddSingleton<ITimestampProvider>(services => services.GetRequiredService<LocalTimestampProvider>());
}
else if (timestampProviderType == "ExternalRfc3161")
{
    var externalUrl = builder.Configuration["Timestamping:External:Url"];
    var trustAnchors = builder.Configuration.GetSection("Timestamping:External:TrustedCertificates").Get<string[]>();
    var hasTrustedExternalConfiguration = Uri.TryCreate(externalUrl, UriKind.Absolute, out var externalUri) &&
        externalUri.Scheme == Uri.UriSchemeHttps && trustAnchors is { Length: > 0 } && trustAnchors.All(path => File.Exists(Path.GetFullPath(path)));
    if (!hasTrustedExternalConfiguration)
    {
        builder.Services.AddSingleton<ITimestampProvider, DisabledTimestampProvider>();
    }
    else
    {
        builder.Services.AddHttpClient<ExternalRfc3161TimestampProvider>(client => client.Timeout = TimeSpan.FromSeconds(10));
        builder.Services.AddTransient<ITimestampProvider>(services => services.GetRequiredService<ExternalRfc3161TimestampProvider>());
    }
}
else throw new InvalidOperationException("Timestamping:ProviderType must be Local or ExternalRfc3161.");
builder.Services.AddScoped<ISignatureTimestampService, SignatureTimestampService>();
builder.Services.AddSingleton<IFileStorage>(_ =>
{
    var storagePath = builder.Configuration["FileStorage:RootPath"] ?? "storage";
    var rootPath = Path.IsPathRooted(storagePath) ? storagePath : Path.Combine(builder.Environment.ContentRootPath, storagePath);
    return new LocalFileStorage(rootPath);
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception exception)
    {
        app.Logger.LogError("Unhandled API error of type {ExceptionType}", exception.GetType().Name);
        if (context.Response.HasStarted) { context.Abort(); return; }
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Detail = "An unexpected error occurred. Please try again later."
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web)), context.RequestAborted);
    }
});
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(); app.UseSwaggerUI();
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DevelopmentUserSeeder>().SeedAsync(CancellationToken.None);
}
app.UseRouting();
if (app.Environment.IsDevelopment()) app.UseCors("FrontendDevelopment");
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
app.Run();

public partial class Program;
