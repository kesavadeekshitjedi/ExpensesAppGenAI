using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

public static class MemberEndpoints
{
    public record MemberResponse(Guid Id, string DisplayName, string Role, string? Email, bool CanSignIn);
    public record CreateMemberRequest(string DisplayName, MemberRole Role);

    public static void MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/members").RequireAuthorization();

        // Everyone in the household (parents and view-only children) can read the member list.
        group.MapGet("/", async (ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var members = await db.Members
                .Where(m => m.HouseholdId == householdId)
                .OrderBy(m => m.DisplayName)
                .ToListAsync(ct);
            return Results.Ok(members.Select(ToResponse));
        });

        // Add a member who does not sign in (e.g. a child), so their spending can be tagged "for" them.
        // Parent only.
        group.MapPost("/", async (CreateMemberRequest request, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["displayName"] = ["Display name is required."],
                });
            }

            var member = new Member
            {
                Id = Guid.NewGuid(),
                HouseholdId = user.GetHouseholdId(),
                DisplayName = request.DisplayName.Trim(),
                Role = request.Role,
            };
            db.Members.Add(member);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/members/{member.Id}", ToResponse(member));
        }).RequireAuthorization(AppClaims.ParentPolicy);
    }

    private static MemberResponse ToResponse(Member m) =>
        new(m.Id, m.DisplayName, m.Role.ToString(), m.Email, m.ExternalId is not null);
}
