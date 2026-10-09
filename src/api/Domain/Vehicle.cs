namespace Expenses.Api.Domain;

// A household vehicle, e.g. "Honda Pilot". Gas and other per-car costs are attached to a vehicle on the
// expense line (SPEC feature 11 granularity), so reports can show true cost per car. Parents manage the
// list; archived vehicles stay on past lines but are hidden from new-expense pickers.
public class Vehicle
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required string Name { get; set; }

    // Optional descriptive fields.
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }

    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
