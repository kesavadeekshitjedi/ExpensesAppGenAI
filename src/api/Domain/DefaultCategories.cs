namespace Expenses.Api.Domain;

// The starter category list a new household gets (SPEC feature 5). Parents can add, rename, and
// archive from here. Kept in one place so household bootstrap and the "seed if empty" backfill agree.
public static class DefaultCategories
{
    public static readonly string[] Names =
    [
        "Groceries",
        "Dining out",
        "Utilities",
        "Household",
        "Transportation",
        "Entertainment",
        "Health",
        "Kids",
        "Subscriptions",
    ];

    public static IEnumerable<Category> For(Guid householdId) =>
        Names.Select(name => new Category { Id = Guid.NewGuid(), HouseholdId = householdId, Name = name });
}
