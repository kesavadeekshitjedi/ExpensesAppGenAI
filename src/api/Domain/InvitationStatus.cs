namespace Expenses.Api.Domain;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Revoked,
    // "Expired" is not stored: it is derived by comparing ExpiresAt to the current time.
}
