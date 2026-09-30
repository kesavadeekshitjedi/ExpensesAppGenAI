namespace Expenses.Api.Domain;

// The root of every aggregate. All other data is scoped to a household, and every
// query the API runs is filtered to the signed-in user's household (SPEC data model).
public class Household
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
