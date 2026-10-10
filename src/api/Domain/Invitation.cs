namespace Expenses.Api.Domain;

public class Invitation
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }

    // Optional intended recipient. Phase 1 invitations are shareable codes, so an invitation can be
    // created without knowing the exact email.
    public string? Email { get; set; }

    // The role the invitee receives when they accept.
    public MemberRole Role { get; set; }

    // Optional existing member this invitation is for. When set, accepting the invitation attaches the
    // sign-in identity to that member (e.g. an adult added for expense tagging who is later invited to
    // sign in) instead of creating a new member — so inviting an existing member never duplicates them.
    public Guid? MemberId { get; set; }

    // The unguessable code embedded in the shareable invite link.
    public required string Code { get; set; }

    public InvitationStatus Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public Guid CreatedByMemberId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? AcceptedByMemberId { get; set; }
}
