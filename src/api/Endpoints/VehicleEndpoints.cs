using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

public static class VehicleEndpoints
{
    public record VehicleResponse(Guid Id, string Name, string? Make, string? Model, int? Year, bool Archived);
    public record CreateVehicleRequest(string Name, string? Make, string? Model, int? Year);
    public record UpdateVehicleRequest(string? Name, string? Make, string? Model, int? Year, bool? Archived);

    public static void MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/vehicles").RequireAuthorization();

        // Everyone in the household can read the vehicle list (needed for the "for which car" picker).
        group.MapGet("/", async (ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var vehicles = await db.Vehicles
                .Where(v => v.HouseholdId == householdId)
                .OrderBy(v => v.Name)
                .ToListAsync(ct);
            return Results.Ok(vehicles.Select(ToResponse));
        });

        group.MapPost("/", async (CreateVehicleRequest request, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            }

            var householdId = user.GetHouseholdId();
            if (await db.Vehicles.AnyAsync(v => v.HouseholdId == householdId && v.Name == name, ct))
            {
                return Results.Conflict(new { message = $"A vehicle named \"{name}\" already exists." });
            }

            var vehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                Name = name,
                Make = Trimmed(request.Make),
                Model = Trimmed(request.Model),
                Year = request.Year,
            };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/vehicles/{vehicle.Id}", ToResponse(vehicle));
        }).RequireAuthorization(AppClaims.ParentPolicy);

        // Rename / edit details / archive. Archiving hides the vehicle from pickers but keeps it on past lines.
        group.MapPatch("/{id:guid}", async (Guid id, UpdateVehicleRequest request, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id && v.HouseholdId == householdId, ct);
            if (vehicle is null)
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
                if (await db.Vehicles.AnyAsync(v => v.HouseholdId == householdId && v.Name == name && v.Id != id, ct))
                {
                    return Results.Conflict(new { message = $"A vehicle named \"{name}\" already exists." });
                }
                vehicle.Name = name;
            }

            if (request.Make is not null) vehicle.Make = Trimmed(request.Make);
            if (request.Model is not null) vehicle.Model = Trimmed(request.Model);
            if (request.Year is not null) vehicle.Year = request.Year;
            if (request.Archived is bool archived) vehicle.Archived = archived;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(vehicle));
        }).RequireAuthorization(AppClaims.ParentPolicy);
    }

    private static VehicleResponse ToResponse(Vehicle v) => new(v.Id, v.Name, v.Make, v.Model, v.Year, v.Archived);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
