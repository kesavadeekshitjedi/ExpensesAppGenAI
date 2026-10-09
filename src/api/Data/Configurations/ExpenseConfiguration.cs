using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.Property(e => e.Total).HasPrecision(18, 2);
        builder.Property(e => e.Tax).HasPrecision(18, 2);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        // Household is the single cascade path; deleting a household removes its expenses (and their
        // line items, below). Merchant, payment method and the entering member are references only.
        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(e => e.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PaymentMethod>()
            .WithMany()
            .HasForeignKey(e => e.PaymentMethodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(e => e.EnteredByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.LineItems)
            .WithOne()
            .HasForeignKey(l => l.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.HouseholdId, e.Date });
    }
}
