using Expenses.Api.FuelPrices;

namespace Expenses.Api.Endpoints;

public static class FuelPriceEndpoints
{
    public static void MapFuelPriceEndpoints(this IEndpointRouteBuilder app)
    {
        // Reference fuel price (a sanity-check hint). Any household member can read it; it writes nothing.
        // 204 No Content when unavailable or not configured, so the web app simply hides the hint.
        var group = app.MapGroup("/fuel-price").RequireAuthorization();

        group.MapGet("/", async (string? area, IFuelPriceProvider provider, CancellationToken ct) =>
        {
            var price = await provider.GetLatestAsync(string.IsNullOrWhiteSpace(area) ? "SWA" : area.Trim(), ct);
            return price is null ? Results.NoContent() : Results.Ok(price);
        });
    }
}
