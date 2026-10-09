using System.Net;
using System.Net.Http.Json;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Tests;

// Drives the expense endpoints through the real HTTP pipeline (see TestApiFactory).
public class ExpenseApiTests
{
    private record LinePayload(string description, Guid categoryId, Guid? forMemberId, decimal? quantity,
        decimal? unitPrice, decimal? amount, string? valueTag, string? notes, string? shortForm);
    private record ExpensePayload(string merchant, Guid paymentMethodId, string? date, decimal? tax,
        string? notes, object[] lineItems);

    [Fact]
    public async Task PostExpense_SavesLines_ComputesTotal_AndFiguresOutShortForm()
    {
        await using var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        var payload = new ExpensePayload(
            merchant: "Costco",
            paymentMethodId: method.Id,
            date: "2026-10-05",
            tax: 1.50m,
            notes: "weekly run",
            lineItems:
            [
                new LinePayload("Kirkland Organic Eggs", category.Id, null, 2, 3.50m, null, "Needed", "for breakfast", null),
                new LinePayload("Bananas", category.Id, null, 1, null, 1.25m, null, null, null),
            ]);

        var response = await client.PostAsJsonAsync("/expenses", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ExpenseDto>();
        Assert.NotNull(body);
        Assert.Equal(8.25m, body!.total);                    // 2 x 3.50 + 1.25
        Assert.Equal("Costco", body.merchant);
        Assert.Equal(2, body.lineItems.Count);

        var eggs = body.lineItems.First(l => l.description == "Kirkland Organic Eggs");
        Assert.Equal("KIRKL ORGAN EGGS", eggs.shortForm);    // app figured out the short form
        Assert.Equal("Family", eggs.@for);
        Assert.Equal("Needed", eggs.valueTag);

        // The item database was populated.
        using var db = factory.NewDbContext();
        Assert.Equal(2, await db.Items.CountAsync());
        Assert.Equal(2, await db.ItemReceiptDescriptions.CountAsync());
        Assert.Single(await db.ValueTags.ToListAsync());
    }

    [Fact]
    public async Task PostExpense_WithNoLineItems_IsRejected()
    {
        await using var factory = new TestApiFactory();
        var (_, method) = await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        var payload = new ExpensePayload("Costco", method.Id, null, null, null, []);
        var response = await client.PostAsJsonAsync("/expenses", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostExpense_AsChild_IsForbidden()
    {
        await using var factory = new TestApiFactory { Role = MemberRole.Child };
        var (category, method) = await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        var payload = new ExpensePayload("Costco", method.Id, null, null, null,
            [new LinePayload("Milk", category.Id, null, 1, 2m, null, null, null, null)]);
        var response = await client.PostAsJsonAsync("/expenses", payload);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetExpenses_ReturnsWhatWasPosted()
    {
        await using var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Target", method.Id, null, null, null,
            [new LinePayload("Notebook", category.Id, null, 1, 4m, null, null, null, null)]));

        var list = await client.GetFromJsonAsync<List<ExpenseDto>>("/expenses");
        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal("Target", list![0].merchant);
    }

    private record ExpenseDto(Guid id, string merchant, decimal total, List<LineDto> lineItems);
    private record LineDto(string description, string category, string @for, decimal amount, string? valueTag, string? shortForm);
}
