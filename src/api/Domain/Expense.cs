namespace Expenses.Api.Domain;

// A single purchase with one or more line items (SPEC feature 2). "For" is per line item, so one
// expense (e.g. a Costco trip) can cover several people. Only parents enter or edit expenses.
public class Expense
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid MerchantId { get; set; }
    public Guid PaymentMethodId { get; set; }
    public Guid EnteredByMemberId { get; set; }

    public DateOnly Date { get; set; }

    // Total is the sum of the line item amounts (the API computes it, not the client). Tax is optional.
    public decimal Total { get; set; }
    public decimal? Tax { get; set; }

    public string? Notes { get; set; }
    public ExpenseSource Source { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<LineItem> LineItems { get; set; } = [];
}
