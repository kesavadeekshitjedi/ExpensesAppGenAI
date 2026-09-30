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
            CancellationToken ct) =>
        {
            var identity = await validator.ValidateAsync(request.Provider, request.Token, ct);
            if (identity is null)
            {
                return Results.Unauthorized();
            }

            var result = await auth.SignInOrProvisionAsync(identity, request.InvitationCode, ct);
            if (result.Outcome != SignInOutcome.SignedIn)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: result.Outcome == SignInOutcome.InvalidInvitation
                        ? "That invitation is invalid or has expired."
                        : "An invitation is required to join a household.");
            }

            var member = result.Member!;
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, BuildPrincipal(member));
            return Results.Ok(new MeResponse(member.Id, member.HouseholdId, member.Role.ToString(), member.DisplayName, member.Email));
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
