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

    // A default value tag applied automatically when this item is recognized on a receipt (SPEC
    // feature 4), changeable per line. Null when the family hasn't tagged the item.
    public Guid? DefaultValueTagId { get; set; }

    // Name of the blob in the "item-pictures" container holding a photo of the actual item, or null.
    public string? PictureBlobName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
