using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Expenses.Api.Tests;

public class AuthServiceTests
{
    private static ExpensesDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ExternalIdentity Identity(string sub, string? name = "Alex", string? email = "alex@example.com") =>
        new(IdentityProvider.Microsoft, sub, email, name);

    private static AuthService WithAllowlist(ExpensesDbContext db, params string[] allowed) =>
        new(db, TimeProvider.System, Options.Create(new AuthOptions { AllowedEmails = allowed }));

    [Fact]
    public async Task Allowlist_BlocksEmailNotOnTheList()
    {
        using var db = NewDb();
        var service = WithAllowlist(db, "owner@example.com");

        var result = await service.SignInOrProvisionAsync(Identity("sub-x", email: "stranger@example.com"), null);

        Assert.Equal(SignInOutcome.NotAllowed, result.Outcome);
        Assert.Empty(db.Members);
    }

    [Fact]
    public async Task Allowlist_AllowsListedEmail_CaseInsensitively()
    {
        using var db = NewDb();
        var service = WithAllowlist(db, "Owner@Example.com");

        var result = await service.SignInOrProvisionAsync(Identity("sub-1", email: "owner@example.com"), null);

        Assert.Equal(SignInOutcome.SignedIn, result.Outcome);
    }

    [Fact]
    public async Task FirstSignIn_CreatesHouseholdAndParent()
    {
        using var db = NewDb();
        var service = new AuthService(db, TimeProvider.System);

        var result = await service.SignInOrProvisionAsync(Identity("sub-1"), invitationCode: null);

        Assert.Equal(SignInOutcome.SignedIn, result.Outcome);
        Assert.Equal(MemberRole.Parent, result.Member!.Role);
        Assert.Single(db.Households);
        Assert.Single(db.Members);
    }

    [Fact]
    public async Task FirstSignIn_SeedsTheDefaultCategoryList()
    {
        using var db = NewDb();
        var service = new AuthService(db, TimeProvider.System);

        var result = await service.SignInOrProvisionAsync(Identity("sub-1"), invitationCode: null);

        var categories = db.Categories.Where(c => c.HouseholdId == result.Member!.HouseholdId).ToList();
        Assert.Equal(DefaultCategories.Names.Length, categories.Count);
        Assert.Contains(categories, c => c.Name == "Groceries");
    }

    [Fact]
    public async Task ExistingIdentity_SignsInWithoutCreatingAnother()
    {
        using var db = NewDb();
        var service = new AuthService(db, TimeProvider.System);
        await service.SignInOrProvisionAsync(Identity("sub-1"), null);

        var result = await service.SignInOrProvisionAsync(Identity("sub-1"), null);

        Assert.Equal(SignInOutcome.SignedIn, result.Outcome);
        Assert.Single(db.Members);
    }

    [Fact]
    public async Task NewIdentity_WithoutInvitation_WhenHouseholdExists_IsDenied()
    {
        using var db = NewDb();
        var service = new AuthService(db, TimeProvider.System);
        await service.SignInOrProvisionAsync(Identity("sub-1"), null); // bootstraps the household

        var result = await service.SignInOrProvisionAsync(Identity("sub-2", "Sam", "sam@example.com"), null);

        Assert.Equal(SignInOutcome.NeedsInvitation, result.Outcome);
        Assert.Null(result.Member);
        Assert.Single(db.Members);
    }

    [Fact]
    public async Task ValidInvitation_JoinsHouseholdWithInvitedRoleAndMarksAccepted()
    {
        using var db = NewDb();
        var service = new AuthService(db, TimeProvider.System);
        var parent = (await service.SignInOrProvisionAsync(Identity("sub-1"), null)).Member!;

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            HouseholdId = parent.HouseholdId,
            Role = MemberRole.Child,
            Code = "invite-code",
            Status = InvitationStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedByMemberId = parent.Id,
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        var result = await service.SignInOrProvisionAsync(Identity("sub-2", "Kid", "kid@example.com"), "invite-code");

        Assert.Equal(SignInOutcome.SignedIn, result.Outcome);
        Assert.Equal(MemberRole.Child, result.Member!.Role);
        Assert.Equal(parent.HouseholdId, result.Member.HouseholdId);

        var stored = await db.Invitations.FindAsync(invitation.Id);
        Assert.Equal(InvitationStatus.Accepted, stored!.Status);
        Assert.Equal(result.Member.Id, stored.AcceptedByMemberId);
    }

    [Fact]
    public async Task TargetedInvitation_AttachesToExistingMember_WithoutDuplicating()
    {
        using var db = NewDb();
        var service = new AuthService(db, TimeProvider.System);
        var parent = (await service.SignInOrProvisionAsync(Identity("sub-1"), null)).Member!;

        // An adult added for expense tagging, no sign-in yet.
        var adult = new Member { Id = Guid.NewGuid(), HouseholdId = parent.HouseholdId, DisplayName = "Spouse", Role = MemberRole.Child };
        db.Members.Add(adult);
        db.Invitations.Add(new Invitation
        {
            Id = Guid.NewGuid(),
            HouseholdId = parent.HouseholdId,
            Role = MemberRole.Parent,
            MemberId = adult.Id,
            Code = "target-code",
            Status = InvitationStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedByMemberId = parent.Id,
        });
        await db.SaveChangesAsync();

        var result = await service.SignInOrProvisionAsync(Identity("sub-2", "Spouse", "spouse@example.com"), "target-code");

        Assert.Equal(SignInOutcome.SignedIn, result.Outcome);
        Assert.Equal(adult.Id, result.Member!.Id);           // same member, not a new one
        Assert.Equal(MemberRole.Parent, result.Member.Role); // role from the invitation applied
        Assert.Equal("sub-2", result.Member.ExternalId);
        Assert.Equal(2, db.Members.Count());                 // parent + the attached adult only
    }

    [Fact]
    public async Task TargetedInvitation_WhenMemberAlreadySignsIn_CreatesANewMember()
    {
        using var db = NewDb();
        var service = new AuthService(db, TimeProvider.System);
        var parent = (await service.SignInOrProvisionAsync(Identity("sub-1"), null)).Member!;

        // The target already has a sign-in identity, so the invite can't re-claim it.
        db.Invitations.Add(new Invitation
        {
            Id = Guid.NewGuid(),
            HouseholdId = parent.HouseholdId,
            Role = MemberRole.Child,
            MemberId = parent.Id,
            Code = "stale-code",
            Status = InvitationStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedByMemberId = parent.Id,
        });
        await db.SaveChangesAsync();

        var result = await service.SignInOrProvisionAsync(Identity("sub-2", "Other", "other@example.com"), "stale-code");

        Assert.Equal(SignInOutcome.SignedIn, result.Outcome);
        Assert.NotEqual(parent.Id, result.Member!.Id);
        Assert.Equal(2, db.Members.Count());
    }

    [Fact]
    public async Task ExpiredInvitation_IsRejected()
    {
        using var db = NewDb();
        var service = new AuthService(db, TimeProvider.System);
        var parent = (await service.SignInOrProvisionAsync(Identity("sub-1"), null)).Member!;
        db.Invitations.Add(new Invitation
        {
            Id = Guid.NewGuid(),
            HouseholdId = parent.HouseholdId,
            Role = MemberRole.Child,
            Code = "expired",
            Status = InvitationStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByMemberId = parent.Id,
        });
        await db.SaveChangesAsync();

        var result = await service.SignInOrProvisionAsync(Identity("sub-2"), "expired");

        Assert.Equal(SignInOutcome.InvalidInvitation, result.Outcome);
        Assert.Single(db.Members);
    }
}
