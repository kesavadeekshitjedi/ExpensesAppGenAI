using System.Text.Json.Serialization;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Expenses.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);

// Serialize/accept enums as strings (e.g. "Microsoft", "Child") so the web app uses readable values.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The connection string is absent in unit tests (WebApplicationFactory) and in any environment
// without a database configured, so registration is guarded like the telemetry block below.
var connectionString = builder.Configuration.GetConnectionString("Expenses");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<ExpensesDbContext>(options => options.UseSqlServer(connectionString));
}

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // the session cookie is sent cross-site from the web app

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<IExternalIdentityValidator, MicrosoftIdentityValidator>();
builder.Services.AddScoped<AuthService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "expenses_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.None;         // web app and API are on different sites
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        // This is an API: answer with status codes instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AppClaims.ParentPolicy, policy => policy.RequireClaim(AppClaims.Role, nameof(MemberRole.Parent)));

// Session protection keys: in Azure, persist them to Blob Storage and encrypt them with a Key Vault
// key, both reached through the managed identity (no secrets). Locally (no Azure config) the default
// local key ring is used so sign-in still works during development.
var blobEndpoint = builder.Configuration["Storage:BlobEndpoint"];
var keyVaultKeyId = builder.Configuration["DataProtection:KeyVaultKeyId"];
if (!string.IsNullOrEmpty(blobEndpoint) && !string.IsNullOrEmpty(keyVaultKeyId))
{
    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = builder.Configuration["AZURE_CLIENT_ID"],
    });
    builder.Services.AddDataProtection()
        .SetApplicationName("expenses")
        .PersistKeysToAzureBlobStorage(new Uri(new Uri(blobEndpoint), "dataprotection/keys.xml"), credential)
        .ProtectKeysWithAzureKeyVault(new Uri(keyVaultKeyId), credential);
}

if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapMemberEndpoints();
app.MapInvitationEndpoints();

app.Run();

public partial class Program;
