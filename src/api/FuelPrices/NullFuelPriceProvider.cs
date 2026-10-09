namespace Expenses.Api.FuelPrices;

// Used when no EIA key is configured (local dev, tests). The fuel-price hint is simply unavailable.
public class NullFuelPriceProvider : IFuelPriceProvider
{
    public Task<FuelPrice?> GetLatestAsync(string area, CancellationToken ct = default) => Task.FromResult<FuelPrice?>(null);
}
