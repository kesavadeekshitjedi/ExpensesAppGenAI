namespace Expenses.Api.Domain;

// A place money was spent, e.g. "Costco". Created on demand as expenses are entered and reused by
// name within a household, so receipt descriptions and price history can be grouped by merchant.
public class Merchant
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
