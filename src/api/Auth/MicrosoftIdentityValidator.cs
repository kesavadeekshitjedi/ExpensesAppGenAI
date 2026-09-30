using Expenses.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Validators;

namespace Expenses.Api.Auth;

// Validates a Microsoft-issued ID token: signature against Microsoft's published keys, audience is
// our client ID, and issuer is a real Microsoft issuer (AadIssuerValidator handles the per-tenant
// issuer that "common" produces, plus personal Microsoft accounts). No client secret is involved.
public class MicrosoftIdentityValidator : IExternalIdentityValidator
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly string _clientId;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager;
    private readonly IssuerValidator _issuerValidator;

    public MicrosoftIdentityValidator(IOptions<AuthOptions> options)
    {
        var ms = options.Value.Microsoft;
        _clientId = ms.ClientId;
        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{ms.Authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());
        _issuerValidator = AadIssuerValidator.GetAadIssuerValidator(ms.Authority).Validate;
    }

    public async Task<ExternalIdentity?> ValidateAsync(IdentityProvider provider, string token, CancellationToken ct = default)
    {
        if (provider != IdentityProvider.Microsoft || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var config = await _configManager.GetConfigurationAsync(ct);
        var parameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = _clientId,
            ValidateIssuer = true,
            IssuerValidator = _issuerValidator,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ValidateLifetime = true,
        };

        var result = await Handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid)
        {
            return null;
        }

        var claims = result.ClaimsIdentity;
        var subject = claims.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(subject))
        {
            return null;
        }

        var email = claims.FindFirst("email")?.Value ?? claims.FindFirst("preferred_username")?.Value;
        var name = claims.FindFirst("name")?.Value;
        return new ExternalIdentity(IdentityProvider.Microsoft, subject, email, name);
    }
}
