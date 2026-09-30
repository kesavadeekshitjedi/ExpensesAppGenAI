using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Expenses.Api.Data;

// Used by the EF Core tools (`dotnet ef migrations ...`, `migrations script`) at design time.
// The connection string here is only for building the model and generating SQL; it is never
// connected to. The deploy pipeline generates an idempotent SQL script and applies it against
// the real database with its own credentials.
public class ExpensesDbContextFactory : IDesignTimeDbContextFactory<ExpensesDbContext>
{
    public ExpensesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=expenses-dev;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new ExpensesDbContext(options);
    }
}
