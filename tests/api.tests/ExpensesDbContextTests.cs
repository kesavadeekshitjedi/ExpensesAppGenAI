using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Tests;

public class ExpensesDbContextTests
{
    // Building the model needs no database connection, so this runs in CI. It guards against a
    // context/configuration change that would break migrations before a deploy tries to apply them.
    [Fact]
    public void Model_MapsHousehold()
    {
        using var context = new ExpensesDbContextFactory().CreateDbContext([]);

        var household = context.Model.FindEntityType(typeof(Household));

        Assert.NotNull(household);
        Assert.Equal("Households", household.GetTableName());
        Assert.Equal(200, household.FindProperty(nameof(Household.Name))!.GetMaxLength());
    }

    [Fact]
    public void Model_MapsCategoryAndPaymentMethod()
    {
        using var context = new ExpensesDbContextFactory().CreateDbContext([]);

        Assert.Equal("Categories", context.Model.FindEntityType(typeof(Category))!.GetTableName());
        Assert.Equal("PaymentMethods", context.Model.FindEntityType(typeof(PaymentMethod))!.GetTableName());
    }
}
