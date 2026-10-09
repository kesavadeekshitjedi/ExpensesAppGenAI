using System.Net;
using System.Net.Http.Json;
using Expenses.Api.Domain;

namespace Expenses.Api.Tests;

// Drives the vehicle endpoints (per-car cost tracking) and the gas->vehicle link on expense lines.
public class VehicleApiTests
{
    private record VehicleDto(Guid id, string name, string? make, string? model, int? year, bool archived);
    private record CreateVehicle(string name, string? make = null, string? model = null, int? year = null);

    private record LinePayload(string description, Guid categoryId, decimal? amount, Guid? vehicleId);
    private record ExpensePayload(string merchant, Guid paymentMethodId, string? date, decimal? tax, string? notes, object[] lineItems);

    private record BucketDto(string key, decimal total, int count);
    private record SummaryDto(decimal total, List<BucketDto> byVehicle);
    private record LineItemDto(string description, decimal amount, Guid? vehicleId, string? vehicle);
    private record ExpenseDto(Guid id, List<LineItemDto> lineItems);

    [Fact]
    public async Task CreateAndList_ReturnsVehicle()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/vehicles", new CreateVehicle("Honda Pilot", "Honda", "Pilot", 2019));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var list = await client.GetFromJsonAsync<List<VehicleDto>>("/vehicles");
        var vehicle = Assert.Single(list!);
        Assert.Equal("Honda Pilot", vehicle.name);
        Assert.Equal(2019, vehicle.year);
    }

    [Fact]
    public async Task Child_CannotCreateVehicle()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        factory.Role = MemberRole.Child;
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/vehicles", new CreateVehicle("Benz"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task GasLineLinkedToVehicle_AppearsOnExpenseAndInReport()
    {
        var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/vehicles", new CreateVehicle("Honda Pilot"));
        var vehicle = await created.Content.ReadFromJsonAsync<VehicleDto>();

        var expenseRes = await client.PostAsJsonAsync("/expenses", new ExpensePayload("Shell", method.Id, null, null, null,
            [new LinePayload("Gas", category.Id, 52.40m, vehicle!.id)]));
        expenseRes.EnsureSuccessStatusCode();

        var expense = await expenseRes.Content.ReadFromJsonAsync<ExpenseDto>();
        var line = Assert.Single(expense!.lineItems);
        Assert.Equal(vehicle.id, line.vehicleId);
        Assert.Equal("Honda Pilot", line.vehicle);

        var summary = await client.GetFromJsonAsync<SummaryDto>("/reports/summary");
        var bucket = Assert.Single(summary!.byVehicle);
        Assert.Equal("Honda Pilot", bucket.key);
        Assert.Equal(52.40m, bucket.total);
    }

    [Fact]
    public async Task ExpenseWithUnknownVehicle_IsRejected()
    {
        var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/expenses", new ExpensePayload("Shell", method.Id, null, null, null,
            [new LinePayload("Gas", category.Id, 40m, Guid.NewGuid())]));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
