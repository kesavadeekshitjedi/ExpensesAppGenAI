namespace Expenses.Api.Domain;

// One line of an expense (SPEC feature 2). Each line says what was bought, its category, who it was
// "for", and optional value tag and notes. Notes are kept permanently and feed later predictions.
public class LineItem
{
    public Guid Id { get; set; }
    public Guid ExpenseId { get; set; }

    // The description as entered (or, later, as printed on a receipt).
    public required string Description { get; set; }

    // The matched item in the household item database, when known.
    public Guid? ItemId { get; set; }

    public Guid CategoryId { get; set; }

    // Who the item was for: a specific household member, or Family when null.
    public Guid? ForMemberId { get; set; }

    // The vehicle this line is a cost for (e.g. a gas fill-up), or null when it is not a per-car cost.
    public Guid? VehicleId { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }

    public Guid? ValueTagId { get; set; }
    public string? Notes { get; set; }
}
