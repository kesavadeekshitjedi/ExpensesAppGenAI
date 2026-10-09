namespace Expenses.Api.Domain;

// A rotating refresh token for a native mobile session. Only the SHA-256 hash of the opaque token is
// stored, so a database leak cannot be used to impersonate a user. Consumed (revoked) on each refresh
// and on logout, so stolen refresh tokens have a short useful life.
public class MobileRefreshToken
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
