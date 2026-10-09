using Expenses.Api.TaxRates;

namespace Expenses.Api.Endpoints;

public static class TaxRateEndpoints
{
    public static void MapTaxRateEndpoints(this IEndpointRouteBuilder app)
    {
        // Sales-tax rate lookup (Washington). Any household member can use it to estimate tax for a
        // purchase; it never writes anything.
        var group = app.MapGroup("/tax-rate").RequireAuthorization();

        group.MapGet("/", async (string? zip, string? addr, string? city, ISalesTaxRateLookup lookup, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(zip))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["zip"] = ["A ZIP code is required."] });
            }

            var rate = await lookup.LookupAsync(addr, city, zip.Trim(), ct);
            return rate is null
                ? Results.NotFound(new { message = "No Washington sales-tax rate found for that location." })
                : Results.Ok(rate);
        });
    }
}
