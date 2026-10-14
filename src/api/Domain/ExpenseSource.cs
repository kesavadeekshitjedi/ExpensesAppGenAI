namespace Expenses.Api.Domain;

public enum ExpenseSource
{
    // Typed in by a parent.
    Manual,

    // Created from a photographed receipt (step 11).
    Receipt,

    // Imported from a vendor invoice/order-summary file (e.g. an Amazon PDF).
    Invoice,
}
