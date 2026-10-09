using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

public static class CategoryEndpoints
{
    public record CategoryResponse(Guid Id, string Name, bool Archived, bool IsTaxable);
    public record CreateCategoryRequest(string Name, bool? IsTaxable);
    public record UpdateCategoryRequest(string? Name, bool? Archived, bool? IsTaxable);

    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories").RequireAuthorization();

        // Everyone in the household can read categories. A parent reading an empty list backfills the
        // defaults once, so a household created before step 7 still gets the starter list (idempotent).
        group.MapGet("/", async (ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            if (user.GetRole() == MemberRole.Parent && !await db.Categories.AnyAsync(c => c.HouseholdId == householdId, ct))
            {
                db.Categories.AddRange(DefaultCategories.For(householdId));
                await db.SaveChangesAsync(ct);
            }

            var categories = await db.Categories
                .Where(c => c.HouseholdId == householdId)
                .OrderBy(c => c.Name)
                .ToListAsync(ct);
            return Results.Ok(categories.Select(ToResponse));
        });

        group.MapPost("/", async (CreateCategoryRequest request, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            }

            var householdId = user.GetHouseholdId();
            if (await db.Categories.AnyAsync(c => c.HouseholdId == householdId && c.Name == name, ct))
            {
                return Results.Conflict(new { message = $"A category named \"{name}\" already exists." });
            }

            var category = new Category { Id = Guid.NewGuid(), HouseholdId = householdId, Name = name, IsTaxable = request.IsTaxable ?? true };
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/categories/{category.Id}", ToResponse(category));
        }).RequireAuthorization(AppClaims.ParentPolicy);

        // Rename and/or archive. Archiving keeps the category on past line items but hides it from pickers.
        group.MapPatch("/{id:guid}", async (Guid id, UpdateCategoryRequest request, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.HouseholdId == householdId, ct);
            if (category is null)
            {
                return Results.NotFound();
            }

            if (request.Name is not null)
            {
                var name = request.Name.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name cannot be blank."] });
                }
                if (await db.Categories.AnyAsync(c => c.HouseholdId == householdId && c.Name == name && c.Id != id, ct))
                {
                    return Results.Conflict(new { message = $"A category named \"{name}\" already exists." });
                }
                category.Name = name;
            }

            if (request.Archived is bool archived)
            {
                category.Archived = archived;
            }

            if (request.IsTaxable is bool isTaxable)
            {
                category.IsTaxable = isTaxable;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(category));
        }).RequireAuthorization(AppClaims.ParentPolicy);
    }

    private static CategoryResponse ToResponse(Category c) => new(c.Id, c.Name, c.Archived, c.IsTaxable);
}
