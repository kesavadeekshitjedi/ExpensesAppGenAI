using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

public static class PaymentMethodEndpoints
{
    public record PaymentMethodResponse(Guid Id, string Label, string Type, bool Archived);
    public record CreatePaymentMethodRequest(string Label, PaymentMethodType Type);
    public record UpdatePaymentMethodRequest(string? Label, PaymentMethodType? Type, bool? Archived);

    public static void MapPaymentMethodEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payment-methods").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var methods = await db.PaymentMethods
                .Where(p => p.HouseholdId == householdId)
                .OrderBy(p => p.Label)
                .ToListAsync(ct);
            return Results.Ok(methods.Select(ToResponse));
        });

        group.MapPost("/", async (CreatePaymentMethodRequest request, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var label = request.Label?.Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["label"] = ["Label is required."] });
            }

            var householdId = user.GetHouseholdId();
            if (await db.PaymentMethods.AnyAsync(p => p.HouseholdId == householdId && p.Label == label, ct))
            {
                return Results.Conflict(new { message = $"A payment method named \"{label}\" already exists." });
            }

            var method = new PaymentMethod { Id = Guid.NewGuid(), HouseholdId = householdId, Label = label, Type = request.Type };
            db.PaymentMethods.Add(method);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/payment-methods/{method.Id}", ToResponse(method));
        }).RequireAuthorization(AppClaims.ParentPolicy);

        group.MapPatch("/{id:guid}", async (Guid id, UpdatePaymentMethodRequest request, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var method = await db.PaymentMethods.FirstOrDefaultAsync(p => p.Id == id && p.HouseholdId == householdId, ct);
            if (method is null)
            {
                return Results.NotFound();
            }

            if (request.Label is not null)
            {
                var label = request.Label.Trim();
                if (string.IsNullOrWhiteSpace(label))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["label"] = ["Label cannot be blank."] });
                }
                if (await db.PaymentMethods.AnyAsync(p => p.HouseholdId == householdId && p.Label == label && p.Id != id, ct))
                {
                    return Results.Conflict(new { message = $"A payment method named \"{label}\" already exists." });
                }
                method.Label = label;
            }

            if (request.Type is PaymentMethodType type)
            {
                method.Type = type;
            }

            if (request.Archived is bool archived)
            {
                method.Archived = archived;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(method));
        }).RequireAuthorization(AppClaims.ParentPolicy);
    }

    private static PaymentMethodResponse ToResponse(PaymentMethod p) => new(p.Id, p.Label, p.Type.ToString(), p.Archived);
}
