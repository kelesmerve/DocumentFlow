using System.Text;
using System.Text.Json.Serialization;
using DocumentFlow.Application.Authentication;
using DocumentFlow.Application.Documents;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Authentication;
using DocumentFlow.Infrastructure.Documents;
using DocumentFlow.Infrastructure.Persistence;
using DocumentFlow.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
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
});
builder.Services.AddAuthorization();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<DevelopmentUserSeeder>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddSingleton<IFileStorage>(_ =>
{
    var storagePath = builder.Configuration["FileStorage:RootPath"] ?? "storage";
    var rootPath = Path.IsPathRooted(storagePath) ? storagePath : Path.Combine(builder.Environment.ContentRootPath, storagePath);
    return new LocalFileStorage(rootPath);
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(); app.UseSwaggerUI();
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DevelopmentUserSeeder>().SeedAsync(CancellationToken.None);
}
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
app.Run();

public partial class Program;
