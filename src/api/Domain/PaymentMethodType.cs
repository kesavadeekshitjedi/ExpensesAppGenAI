namespace Expenses.Api.Domain;

// A payment method is a label only — no card or account numbers are ever stored (SPEC feature 1).
// The type is for grouping and reporting.
public enum PaymentMethodType
{
    CreditCard,
    BankAccount,
    Cash,
    Other,
}
