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

    // The unguessable code embedded in the shareable invite link.
    public required string Code { get; set; }

    public InvitationStatus Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public Guid CreatedByMemberId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? AcceptedByMemberId { get; set; }
}
