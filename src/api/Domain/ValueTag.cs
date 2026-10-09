namespace Expenses.Api.Domain;

// A parent's own judgement of a purchase, e.g. "Splurge" or "Needed" (SPEC feature 4). There are no
// predefined tags; a tag is created the first time it is typed and reused on later line items.
// Names are unique per household, case-insensitive.
public class ValueTag
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required string Name { get; set; }
    public Guid CreatedByMemberId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
