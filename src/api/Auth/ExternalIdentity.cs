using Expenses.Api.Domain;

namespace Expenses.Api.Auth;

// A caller's identity as proven by a validated provider ID token. Subject is the provider's stable,
// unique id for this user (the token's `sub`), used to match or create a Member.
public record ExternalIdentity(IdentityProvider Provider, string Subject, string? Email, string? DisplayName);
