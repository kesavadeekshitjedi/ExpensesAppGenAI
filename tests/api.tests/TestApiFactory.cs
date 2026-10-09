using System.Security.Claims;
using System.Text.Encodings.Web;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.AspNetCore.Authentication;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // In "Testing" the API does not load the Development LocalDB connection string, so Program
        // registers no SQL Server provider and the in-memory provider below is the only one.
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddDbContext<ExpensesDbContext>(options => options.UseInMemoryDatabase(_dbName));

            // Sign every request in as this factory's member via a test scheme, and make it the default.
            services.AddSingleton(this);
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.Configure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
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
