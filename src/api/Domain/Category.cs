namespace Expenses.Api.Domain;

// A spending category, e.g. "Groceries". New households start with a default list (SPEC feature 5);
// parents can add, rename, and archive categories.
public class Category
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required string Name { get; set; }

    // Archived categories stay on past line items but are hidden from new-expense pickers.
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
