using Expenses.Api.Domain;

namespace Expenses.Api.Auth;

public interface IExternalIdentityValidator
{
    // Validates a provider-issued ID token and returns the caller's identity, or null if the token
    // is invalid, expired, or from an unsupported provider.
    Task<ExternalIdentity?> ValidateAsync(IdentityProvider provider, string token, CancellationToken ct = default);
}
