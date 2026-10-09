namespace Expenses.Api.Domain;

// How one item is printed on a given merchant's receipt, e.g. Costco prints "KS ORG EGGS" for
// "Kirkland Signature Organic Eggs". This is the "short form". During manual entry the app figures
// it out from the full name; from a receipt (step 11) it is matched to fill the line in.
// Unique per merchant + printed description.
public class ItemReceiptDescription
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public Guid MerchantId { get; set; }
    public required string PrintedDescription { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
