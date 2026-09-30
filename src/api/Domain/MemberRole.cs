namespace Expenses.Api.Domain;

public enum MemberRole
{
    // Full access: enter/edit expenses, manage members, categories, tags, budgets.
    Parent,

    // View-only: can read all household data, but every write is rejected by the API.
    Child,
}
