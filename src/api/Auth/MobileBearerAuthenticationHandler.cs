using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Expenses.Api.Auth;

// Authenticates requests that carry "Authorization: Bearer <access token>" (native mobile clients).
// No header -> NoResult, so the cookie scheme (web app) still applies. A present-but-invalid token fails.
public class MobileBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = header["Bearer ".Length..].Trim();
        var tokens = Context.RequestServices.GetRequiredService<MobileTokenService>();
        var principal = tokens.ValidateAccessToken(token);
        if (principal is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired access token."));
        }

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, MobileTokenService.BearerScheme)));
    }
}
