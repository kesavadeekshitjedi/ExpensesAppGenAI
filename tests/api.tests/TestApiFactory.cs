using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Expenses.Api.Receipts;
using Expenses.Api.Storage;
using Expenses.Api.TaxRates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Expenses.Api.Tests;

// Spins up the real API with an in-memory database and a stub "always this parent" authentication
// scheme, so endpoints can be driven through the full HTTP pipeline (routing, model binding,
// validation, serialization) without a real Microsoft sign-in.
public class TestApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    public Guid HouseholdId { get; } = Guid.NewGuid();
    public Guid ParentMemberId { get; } = Guid.NewGuid();
    public MemberRole Role { get; set; } = MemberRole.Parent;
    public FakeBlobStorage Blobs { get; } = new();
    public StubReceiptReader Receipts { get; } = new();
    public StubTaxRateLookup TaxRates { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // In "Testing" the API does not load the Development LocalDB connection string, so Program
        // registers no SQL Server provider and the in-memory provider below is the only one.
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddDbContext<ExpensesDbContext>(options => options.UseInMemoryDatabase(_dbName));

            // Replace blob storage, the receipt reader, and the tax-rate lookup with in-memory/stub
            // versions (no Azure, no network). Registered after Program's versions so these win.
            services.AddSingleton<IBlobStorage>(Blobs);
            services.AddSingleton<IReceiptReader>(Receipts);
            services.AddSingleton<ISalesTaxRateLookup>(TaxRates);

            // Sign every request in as this factory's member via a test scheme, and make it the default.
            services.AddSingleton(this);
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.Configure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

            // Program pins the authorization policies to the cookie + bearer schemes; re-point them at the
            // test scheme so the stub principal satisfies both the default and Parent-only policies.
            services.AddAuthorizationBuilder()
                .SetDefaultPolicy(new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName).RequireAuthenticatedUser().Build())
                .AddPolicy(AppClaims.ParentPolicy, p => p
                    .AddAuthenticationSchemes(TestAuthHandler.SchemeName)
                    .RequireClaim(AppClaims.Role, nameof(MemberRole.Parent)));
        });
    }

    // Seeds a household with this parent plus one category and one payment method, and returns them.
    public async Task<(Category category, PaymentMethod method)> SeedBaselineAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        db.Households.Add(new Household { Id = HouseholdId, Name = "Test Household" });
        db.Members.Add(new Member { Id = ParentMemberId, HouseholdId = HouseholdId, DisplayName = "Parent", Role = MemberRole.Parent });
        var category = new Category { Id = Guid.NewGuid(), HouseholdId = HouseholdId, Name = "Groceries" };
        var method = new PaymentMethod { Id = Guid.NewGuid(), HouseholdId = HouseholdId, Label = "Discover card", Type = PaymentMethodType.CreditCard };
        db.Categories.Add(category);
        db.PaymentMethods.Add(method);
        await db.SaveChangesAsync();
        return (category, method);
    }

    public ExpensesDbContext NewDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
    }
}

// In-memory blob store for tests: keyed by "container/blobName".
public sealed class FakeBlobStorage : IBlobStorage
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _store = new();

    private static string Key(string container, string blobName) => $"{container}/{blobName}";

    public int Count => _store.Count;

    public async Task UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _store[Key(container, blobName)] = (ms.ToArray(), contentType);
    }

    public Task<BlobDownload?> OpenReadAsync(string container, string blobName, CancellationToken ct = default)
    {
        if (_store.TryGetValue(Key(container, blobName), out var entry))
        {
            return Task.FromResult<BlobDownload?>(new BlobDownload(new MemoryStream(entry.Bytes), entry.ContentType));
        }
        return Task.FromResult<BlobDownload?>(null);
    }

    public Task DeleteAsync(string container, string blobName, CancellationToken ct = default)
    {
        _store.TryRemove(Key(container, blobName), out _);
        return Task.CompletedTask;
    }
}

// Returns a canned extraction so receipt endpoints can be tested without a real Document Intelligence
// call. A test sets Result (or Throw) before scanning.
public sealed class StubReceiptReader : IReceiptReader
{
    public ExtractedReceipt Result { get; set; } = new(null, null, null, null, []);
    public bool Throw { get; set; }

    public Task<ExtractedReceipt> AnalyzeAsync(BinaryData image, CancellationToken ct = default)
    {
        if (Throw) throw new InvalidOperationException("stub failure");
        return Task.FromResult(Result);
    }
}

// Returns a canned sales-tax rate so the lookup endpoint can be tested without calling WA DOR.
public sealed class StubTaxRateLookup : ISalesTaxRateLookup
{
    public SalesTaxRate? Result { get; set; }

    public Task<SalesTaxRate?> LookupAsync(string? address, string? city, string zip, CancellationToken ct = default) =>
        Task.FromResult(Result);
}

public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TestApiFactory factory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, factory.ParentMemberId.ToString()),
            new Claim(AppClaims.HouseholdId, factory.HouseholdId.ToString()),
            new Claim(AppClaims.Role, factory.Role.ToString()),
            new Claim(ClaimTypes.Name, "Parent"),
        };
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
