using System.Security.Claims;
using System.Security.Cryptography;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

public static class InvitationEndpoints
{
    public record CreateInvitationRequest(MemberRole Role, string? Email, int? ExpiresInDays, Guid? MemberId);
    public record InvitationResponse(Guid Id, string Code, string Role, string? Email, string Status, DateTimeOffset ExpiresAt, Guid? MemberId);

    public static void MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        // Managing invitations is Parent-only. Invitees accept by sending the code to POST /auth/session.
        var group = app.MapGroup("/invitations").RequireAuthorization(AppClaims.ParentPolicy);

        group.MapPost("/", async (CreateInvitationRequest request, ClaimsPrincipal user, ExpensesDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();

            // A targeted invitation must point at an existing member of this household who cannot yet sign
            // in (so an adult added for tagging can later be invited without creating a duplicate member).
            if (request.MemberId is Guid memberId)
            {
                var target = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId && m.HouseholdId == householdId, ct);
                if (target is null)
                {
                    return Results.NotFound();
                }
                if (target.ExternalId is not null)
                {
                    return Results.Conflict(new { message = "This member can already sign in." });
                }
            }

            var days = request.ExpiresInDays is > 0 and <= 90 ? request.ExpiresInDays.Value : 14;
            var invitation = new Invitation
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
                Role = request.Role,
                MemberId = request.MemberId,
                Code = GenerateCode(),
                Status = InvitationStatus.Pending,
                ExpiresAt = clock.GetUtcNow().AddDays(days),
                CreatedByMemberId = user.GetMemberId(),
            };
            db.Invitations.Add(invitation);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/invitations/{invitation.Id}", ToResponse(invitation));
        });

        group.MapGet("/", async (ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var invitations = await db.Invitations
                .Where(i => i.HouseholdId == householdId)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync(ct);
            return Results.Ok(invitations.Select(ToResponse));
        });

        group.MapPost("/{id:guid}/revoke", async (Guid id, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id && i.HouseholdId == householdId, ct);
            if (invitation is null)
            {
                return Results.NotFound();
            }

            if (invitation.Status == InvitationStatus.Pending)
            {
                invitation.Status = InvitationStatus.Revoked;
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        });
    }

    private static string GenerateCode()
    {
        // URL-safe, unguessable code for the shareable invite link.
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static InvitationResponse ToResponse(Invitation i) =>
        new(i.Id, i.Code, i.Role.ToString(), i.Email, i.Status.ToString(), i.ExpiresAt, i.MemberId);
}
