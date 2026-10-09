using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Expenses.Api.Endpoints;

public static class AuthEndpoints
{
    public record SessionRequest(IdentityProvider Provider, string Token, string? InvitationCode);
    public record MeResponse(Guid MemberId, Guid HouseholdId, string Role, string DisplayName, string? Email);
    public record RefreshRequest(string RefreshToken);
    public record TokenResponse(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, MeResponse Me);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        // Exchange a provider ID token for an app session cookie. Creates the household on the very
        // first sign-in, or joins an existing household when a valid invitation code is supplied.
        group.MapPost("/session", async (
            SessionRequest request,
            IExternalIdentityValidator validator,
            AuthService auth,
            HttpContext http,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            // Audit log for App Insights: every sign-in attempt and its outcome, with the caller's IP.
            var log = loggerFactory.CreateLogger("Expenses.Auth.SignIn");
            var ip = ClientIp(http);

            var identity = await validator.ValidateAsync(request.Provider, request.Token, ct);
            if (identity is null)
            {
                log.LogWarning("Sign-in rejected: invalid {Provider} token from {Ip}", request.Provider, ip);
                return Results.Unauthorized();
            }

            var result = await auth.SignInOrProvisionAsync(identity, request.InvitationCode, ct);
            if (result.Outcome != SignInOutcome.SignedIn)
            {
                log.LogWarning("Sign-in denied ({Outcome}) for {Email} via {Provider} from {Ip}",
                    result.Outcome, identity.Email ?? "(no email)", identity.Provider, ip);
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: result.Outcome switch
                    {
                        SignInOutcome.InvalidInvitation => "That invitation is invalid or has expired.",
                        SignInOutcome.NotAllowed => "This account is not permitted to sign in.",
                        _ => "An invitation is required to join a household.",
                    });
            }

            var member = result.Member!;
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, BuildPrincipal(member));
            log.LogInformation("Sign-in succeeded for {Email} (member {MemberId}, {Role}) via {Provider} from {Ip}",
                member.Email ?? "(no email)", member.Id, member.Role, identity.Provider, ip);
            return Results.Ok(new MeResponse(member.Id, member.HouseholdId, member.Role.ToString(), member.DisplayName, member.Email));
        });

        // Native mobile sign-in: same identity/allowlist checks as /session, but returns bearer tokens
        // instead of setting a cookie.
        group.MapPost("/token", async (
            SessionRequest request,
            IExternalIdentityValidator validator,
            AuthService auth,
            MobileTokenService tokens,
            HttpContext http,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("Expenses.Auth.SignIn");
            var ip = ClientIp(http);

            var identity = await validator.ValidateAsync(request.Provider, request.Token, ct);
            if (identity is null)
            {
                log.LogWarning("Mobile sign-in rejected: invalid {Provider} token from {Ip}", request.Provider, ip);
                return Results.Unauthorized();
            }

            var result = await auth.SignInOrProvisionAsync(identity, request.InvitationCode, ct);
            if (result.Outcome != SignInOutcome.SignedIn)
            {
                log.LogWarning("Mobile sign-in denied ({Outcome}) for {Email} via {Provider} from {Ip}",
                    result.Outcome, identity.Email ?? "(no email)", identity.Provider, ip);
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: result.Outcome switch
                    {
                        SignInOutcome.InvalidInvitation => "That invitation is invalid or has expired.",
                        SignInOutcome.NotAllowed => "This account is not permitted to sign in.",
                        _ => "An invitation is required to join a household.",
                    });
            }

            var member = result.Member!;
            var (accessToken, accessExpiresAt) = tokens.CreateAccessToken(member);
            var refreshToken = await tokens.IssueRefreshTokenAsync(member.Id, ct);
            log.LogInformation("Mobile sign-in succeeded for {Email} (member {MemberId}, {Role}) via {Provider} from {Ip}",
                member.Email ?? "(no email)", member.Id, member.Role, identity.Provider, ip);
            return Results.Ok(new TokenResponse(accessToken, accessExpiresAt, refreshToken, Me(member)));
        });

        // Exchange a valid refresh token for a new access + refresh token pair (the old refresh is revoked).
        group.MapPost("/refresh", async (RefreshRequest request, MobileTokenService tokens, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return Results.Unauthorized();
            }
            var member = await tokens.ConsumeRefreshTokenAsync(request.RefreshToken, ct);
            if (member is null)
            {
                return Results.Unauthorized();
            }
            var (accessToken, accessExpiresAt) = tokens.CreateAccessToken(member);
            var refreshToken = await tokens.IssueRefreshTokenAsync(member.Id, ct);
            return Results.Ok(new TokenResponse(accessToken, accessExpiresAt, refreshToken, Me(member)));
        });

        // Mobile logout: revoke the refresh token so it can no longer be rotated.
        group.MapPost("/mobile-logout", async (RefreshRequest request, MobileTokenService tokens, CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                await tokens.RevokeRefreshTokenAsync(request.RefreshToken, ct);
            }
            return Results.NoContent();
        });

        group.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new MeResponse(
                user.GetMemberId(),
                user.GetHouseholdId(),
                user.GetRole().ToString(),
                user.FindFirstValue(ClaimTypes.Name) ?? "",
                user.FindFirstValue(ClaimTypes.Email))))
            .RequireAuthorization();

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static MeResponse Me(Member member) =>
        new(member.Id, member.HouseholdId, member.Role.ToString(), member.DisplayName, member.Email);

    private static string ClientIp(HttpContext http) =>
        http.Request.Headers.TryGetValue("X-Forwarded-For", out var fwd) && fwd.Count > 0
            ? fwd.ToString()
            : http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static ClaimsPrincipal BuildPrincipal(Member member)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, member.Id.ToString()),
            new(AppClaims.HouseholdId, member.HouseholdId.ToString()),
            new(AppClaims.Role, member.Role.ToString()),
            new(ClaimTypes.Name, member.DisplayName),
        };
        if (!string.IsNullOrEmpty(member.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, member.Email));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
