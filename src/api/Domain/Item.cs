namespace Expenses.Api.Domain;

// An entry in the household's item database (SPEC feature 3). Built up from what the user enters:
// the full name they give becomes an Item, and each merchant's printed/short form for it is stored
// as an ItemReceiptDescription so future receipts can be matched automatically.
public class Item
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required string FullName { get; set; }
    public Guid? DefaultCategoryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
