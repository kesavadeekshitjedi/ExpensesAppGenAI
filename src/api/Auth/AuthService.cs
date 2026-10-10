using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Expenses.Api.Auth;

public enum SignInOutcome
{
    SignedIn,
    NeedsInvitation,
    InvalidInvitation,
    NotAllowed,
}

public record SignInResult(SignInOutcome Outcome, Member? Member);

public class AuthService(ExpensesDbContext db, TimeProvider clock, IOptions<AuthOptions>? options = null)
{
    private readonly string[] _allowedEmails = options?.Value.AllowedEmails ?? [];

    // Matches the identity to an existing member, or provisions one per the step 6 rules:
    // - existing identity -> sign in
    // - valid invitation code -> create the invited member and accept the invitation
    // - no households yet (first ever sign-in) -> create the household and a Parent (SPEC #45)
    // - otherwise -> no access without an invitation
    public async Task<SignInResult> SignInOrProvisionAsync(ExternalIdentity identity, string? invitationCode, CancellationToken ct = default)
    {
        // An email allowlist (when configured) gates every sign-in, before matching or provisioning.
        if (_allowedEmails.Length > 0 &&
            (identity.Email is null || !_allowedEmails.Contains(identity.Email, StringComparer.OrdinalIgnoreCase)))
        {
            return new SignInResult(SignInOutcome.NotAllowed, null);
        }

        var existing = await db.Members.FirstOrDefaultAsync(
            m => m.Provider == identity.Provider && m.ExternalId == identity.Subject, ct);
        if (existing is not null)
        {
            return new SignInResult(SignInOutcome.SignedIn, existing);
        }

        if (!string.IsNullOrWhiteSpace(invitationCode))
        {
            return await AcceptInvitationAsync(identity, invitationCode, ct);
        }

        if (!await db.Households.AnyAsync(ct))
        {
            return new SignInResult(SignInOutcome.SignedIn, await BootstrapHouseholdAsync(identity, ct));
        }

        return new SignInResult(SignInOutcome.NeedsInvitation, null);
    }

    private async Task<SignInResult> AcceptInvitationAsync(ExternalIdentity identity, string code, CancellationToken ct)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Code == code, ct);
        if (invitation is null
            || invitation.Status != InvitationStatus.Pending
            || invitation.ExpiresAt <= clock.GetUtcNow())
        {
            return new SignInResult(SignInOutcome.InvalidInvitation, null);
        }

        // A targeted invitation attaches the sign-in to an existing member (an adult/child already added
        // for expense tagging), as long as that member exists, is in the invitation's household, and has
        // not already claimed a sign-in. Otherwise we fall back to creating a fresh member.
        Member? member = null;
        if (invitation.MemberId is Guid targetId)
        {
            var target = await db.Members.FirstOrDefaultAsync(
                m => m.Id == targetId && m.HouseholdId == invitation.HouseholdId, ct);
            if (target is not null && target.ExternalId is null)
            {
                target.Role = invitation.Role;
                target.Email = identity.Email;
                target.Provider = identity.Provider;
                target.ExternalId = identity.Subject;
                member = target;
            }
        }

        if (member is null)
        {
            member = new Member
            {
                Id = Guid.NewGuid(),
                HouseholdId = invitation.HouseholdId,
                DisplayName = DisplayNameFor(identity),
                Role = invitation.Role,
                Email = identity.Email,
                Provider = identity.Provider,
                ExternalId = identity.Subject,
            };
            db.Members.Add(member);
        }

        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedAt = clock.GetUtcNow();
        invitation.AcceptedByMemberId = member.Id;

        await db.SaveChangesAsync(ct);
        return new SignInResult(SignInOutcome.SignedIn, member);
    }

    private async Task<Member> BootstrapHouseholdAsync(ExternalIdentity identity, CancellationToken ct)
    {
        var household = new Household
        {
            Id = Guid.NewGuid(),
            Name = $"{DisplayNameFor(identity)}'s Household",
        };
        db.Households.Add(household);

        // Every new household starts with the default category list (SPEC feature 5).
        db.Categories.AddRange(DefaultCategories.For(household.Id));

        var member = new Member
        {
            Id = Guid.NewGuid(),
            HouseholdId = household.Id,
            DisplayName = DisplayNameFor(identity),
            Role = MemberRole.Parent,
            Email = identity.Email,
            Provider = identity.Provider,
            ExternalId = identity.Subject,
        };
        db.Members.Add(member);

        await db.SaveChangesAsync(ct);
        return member;
    }

    private static string DisplayNameFor(ExternalIdentity identity) =>
        identity.DisplayName ?? identity.Email ?? "Member";
}
