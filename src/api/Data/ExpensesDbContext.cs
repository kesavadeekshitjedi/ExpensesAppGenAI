using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Data;

public class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Invitation> Invitations => Set<Invitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExpensesDbContext).Assembly);
    }
}
