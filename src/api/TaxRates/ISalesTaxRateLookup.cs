namespace Expenses.Api.TaxRates;

// A combined sales-tax rate for a location, as fractions (0.104 = 10.4%).
public record SalesTaxRate(string Location, decimal CombinedRate, decimal StateRate, decimal LocalRate);

// Looks up the sales-tax rate for an address/ZIP. Abstracted so the endpoint can be tested with a stub.
public interface ISalesTaxRateLookup
{
    Task<SalesTaxRate?> LookupAsync(string? address, string? city, string zip, CancellationToken ct = default);
}
