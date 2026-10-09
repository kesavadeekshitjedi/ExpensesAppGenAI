namespace Expenses.Api.FuelPrices;

// The latest weekly average retail price for a fuel, in dollars per gallon, for an area.
public record FuelPrice(string Area, string Period, decimal DollarsPerGallon);

// Provides a reference fuel price (a sanity-check hint; not what the user actually paid).
public interface IFuelPriceProvider
{
    // Latest weekly regular-gasoline price for an EIA "duoarea" code (e.g. "SWA" = Washington,
    // "R50" = PADD 5 / West Coast, "NUS" = U.S. average). Null when unavailable or not configured.
    Task<FuelPrice?> GetLatestAsync(string area, CancellationToken ct = default);
}
