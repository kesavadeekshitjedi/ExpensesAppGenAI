namespace Expenses.Api.Domain;

public class Member
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required string DisplayName { get; set; }
    public MemberRole Role { get; set; }

    // Login fields are set only for members who can sign in (phase 1: parents). They stay null for
    // children, who exist as members so spending can be tagged "for" them but do not authenticate.
    public string? Email { get; set; }
    public IdentityProvider? Provider { get; set; }
    public string? ExternalId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
