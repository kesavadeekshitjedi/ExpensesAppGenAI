using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Tests;

public class ItemCatalogTests
{
    private static ExpensesDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static readonly Guid Household = Guid.NewGuid();
    private static readonly Guid Costco = Guid.NewGuid();

    [Fact]
    public async Task Resolve_CreatesItemAndFiguresOutShortForm()
    {
        using var db = NewDb();
        var catalog = new ItemCatalog(db);

        var result = await catalog.ResolveAsync(Household, Costco, "Kirkland Signature Organic Eggs", defaultCategoryId: null, explicitShortForm: null);
        await db.SaveChangesAsync();

        Assert.Equal("KIRKL SIGNA ORGAN EGGS", result.ShortForm);
        Assert.Single(db.Items);
        Assert.Single(db.ItemReceiptDescriptions);
        Assert.Equal(result.Item.Id, db.ItemReceiptDescriptions.Single().ItemId);
    }

    [Fact]
    public async Task Resolve_ReusesItemAndShortForm_ForTheSameNameAndMerchant()
    {
        using var db = NewDb();
        var catalog = new ItemCatalog(db);

        var first = await catalog.ResolveAsync(Household, Costco, "Organic Eggs", null, null);
        await db.SaveChangesAsync();
        var second = await catalog.ResolveAsync(Household, Costco, "Organic Eggs", null, null);
        await db.SaveChangesAsync();

        Assert.Equal(first.Item.Id, second.Item.Id);
        Assert.Equal(first.ShortForm, second.ShortForm);
        Assert.Single(db.Items);
        Assert.Single(db.ItemReceiptDescriptions);
    }

    [Fact]
    public async Task Resolve_DisambiguatesWhenTwoNamesShareAShortFormAtOneMerchant()
    {
        using var db = NewDb();
        var catalog = new ItemCatalog(db);

        var a = await catalog.ResolveAsync(Household, Costco, "Apple", null, null);
        await db.SaveChangesAsync();
        var b = await catalog.ResolveAsync(Household, Costco, "Apple", null, explicitShortForm: null);
        // Different item, same generated short form -> force a clash via an explicit matching short form.
        var c = await catalog.ResolveAsync(Household, Costco, "Apples Gala", null, explicitShortForm: "APPLE");
        await db.SaveChangesAsync();

        Assert.Equal("APPLE", a.ShortForm);
        Assert.Equal("APPLE 2", c.ShortForm);
        Assert.NotEqual(a.Item.Id, c.Item.Id);
    }

    [Fact]
    public async Task Resolve_HonorsAnExplicitShortForm()
    {
        using var db = NewDb();
        var catalog = new ItemCatalog(db);

        var result = await catalog.ResolveAsync(Household, Costco, "Kirkland Organic Eggs", null, explicitShortForm: "ks org eggs");
        await db.SaveChangesAsync();

        Assert.Equal("KS ORG EGGS", result.ShortForm);
    }
}
