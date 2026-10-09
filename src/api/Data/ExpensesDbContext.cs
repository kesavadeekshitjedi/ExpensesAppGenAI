using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Data;

public class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<ValueTag> ValueTags => Set<ValueTag>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemReceiptDescription> ItemReceiptDescriptions => Set<ItemReceiptDescription>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<LineItem> LineItems => Set<LineItem>();
    public DbSet<Receipt> Receipts => Set<Receipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExpensesDbContext).Assembly);
    }
}
