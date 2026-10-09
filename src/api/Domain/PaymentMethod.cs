namespace Expenses.Api.Domain;

// A household payment method, e.g. "Discover card" or "Cash". A label and a type only —
// never a card number, account number, or balance (SPEC feature 1, decision #12).
public class PaymentMethod
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required string Label { get; set; }
    public PaymentMethodType Type { get; set; }

    // Archived methods stay for historical expenses but are hidden from new-expense pickers.
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
