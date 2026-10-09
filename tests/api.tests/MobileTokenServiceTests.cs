using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Tests;

public class MobileTokenServiceTests
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private static ExpensesDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Member NewMember() => new()
    {
        Id = Guid.NewGuid(),
        HouseholdId = Guid.NewGuid(),
        DisplayName = "Alex",
        Role = MemberRole.Parent,
        Email = "alex@example.com",
    };

    private static MobileTokenService Service(ExpensesDbContext db, TimeProvider clock) =>
        new(new EphemeralDataProtectionProvider(), db, clock);

    [Fact]
    public void AccessToken_RoundTrips_WithMemberClaims()
    {
        using var db = NewDb();
        var service = Service(db, new TestClock());
        var member = NewMember();

        var (token, _) = service.CreateAccessToken(member);
        var principal = service.ValidateAccessToken(token);

        Assert.NotNull(principal);
        Assert.Equal(member.Id.ToString(), principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(member.HouseholdId.ToString(), principal.FindFirstValue(AppClaims.HouseholdId));
        Assert.Equal("Parent", principal.FindFirstValue(AppClaims.Role));
    }

    [Fact]
    public void AccessToken_Expired_ReturnsNull()
    {
        using var db = NewDb();
        var clock = new TestClock { UtcNow = DateTimeOffset.UtcNow };
        var service = Service(db, clock);

        var (token, _) = service.CreateAccessToken(NewMember());
        clock.UtcNow = clock.UtcNow.Add(MobileTokenService.AccessTokenLifetime).AddMinutes(1);

        Assert.Null(service.ValidateAccessToken(token));
    }

    [Fact]
    public void AccessToken_Tampered_ReturnsNull()
    {
        using var db = NewDb();
        var service = Service(db, new TestClock());
        Assert.Null(service.ValidateAccessToken("not-a-real-token"));
    }

    [Fact]
    public async Task RefreshToken_Consume_ReturnsMember_AndRotates()
    {
        using var db = NewDb();
        var member = NewMember();
        db.Members.Add(member);
        await db.SaveChangesAsync();
        var service = Service(db, new TestClock());

        var refresh = await service.IssueRefreshTokenAsync(member.Id);

        var consumed = await service.ConsumeRefreshTokenAsync(refresh);
        Assert.NotNull(consumed);
        Assert.Equal(member.Id, consumed!.Id);

        // The same refresh token cannot be used twice (rotation revoked it).
        Assert.Null(await service.ConsumeRefreshTokenAsync(refresh));
    }

    [Fact]
    public async Task RefreshToken_Revoked_CannotBeConsumed()
    {
        using var db = NewDb();
        var member = NewMember();
        db.Members.Add(member);
        await db.SaveChangesAsync();
        var service = Service(db, new TestClock());

        var refresh = await service.IssueRefreshTokenAsync(member.Id);
        await service.RevokeRefreshTokenAsync(refresh);

        Assert.Null(await service.ConsumeRefreshTokenAsync(refresh));
    }
}
