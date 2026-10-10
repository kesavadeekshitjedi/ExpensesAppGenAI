using System.Net;
using System.Net.Http.Json;
using Expenses.Api.Domain;

namespace Expenses.Api.Tests;

// Drives the member endpoints: adding a non-sign-in child and removing one (e.g. a duplicate),
// including the guards that stop a removal from destroying referenced data.
public class MemberApiTests
{
    private record MemberDto(Guid id, string displayName, string role, string? email, bool canSignIn);
    private record CreateMember(string displayName, string role);
    private record LinePayload(string description, Guid categoryId, decimal? amount, Guid? forMemberId);
    private record ExpensePayload(string merchant, Guid paymentMethodId, string? date, decimal? tax, string? notes, object[] lineItems);

    [Fact]
    public async Task AddChild_ThenRemove_LeavesOnlyTheParent()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/members", new CreateMember("Siddharth", "Child"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var child = await created.Content.ReadFromJsonAsync<MemberDto>();

        var removed = await client.DeleteAsync($"/members/{child!.id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        var list = await client.GetFromJsonAsync<List<MemberDto>>("/members");
        var only = Assert.Single(list!);
        Assert.Equal("Parent", only.displayName);
    }

    [Fact]
    public async Task Remove_MemberWithTaggedExpense_IsRejected()
    {
        var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        var child = await (await client.PostAsJsonAsync("/members", new CreateMember("Siddharth", "Child")))
            .Content.ReadFromJsonAsync<MemberDto>();

        var expense = await client.PostAsJsonAsync("/expenses", new ExpensePayload("Target", method.Id, null, null, null,
            [new LinePayload("Toy", category.Id, 10m, child!.id)]));
        expense.EnsureSuccessStatusCode();

        var removed = await client.DeleteAsync($"/members/{child.id}");
        Assert.Equal(HttpStatusCode.Conflict, removed.StatusCode);
    }

    [Fact]
    public async Task Child_CannotRemoveMember()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        var child = await (await factory.CreateClient().PostAsJsonAsync("/members", new CreateMember("Snigdha", "Child")))
            .Content.ReadFromJsonAsync<MemberDto>();

        factory.Role = MemberRole.Child;
        var res = await factory.CreateClient().DeleteAsync($"/members/{child!.id}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
