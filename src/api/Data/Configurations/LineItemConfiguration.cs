using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class LineItemConfiguration : IEntityTypeConfiguration<LineItem>
{
    public void Configure(EntityTypeBuilder<LineItem> builder)
    {
        builder.Property(l => l.Description).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 2);
        builder.Property(l => l.Amount).HasPrecision(18, 2);
        builder.Property(l => l.AllocatedTax).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(l => l.Notes).HasMaxLength(2000);

        // The Expense -> LineItem relationship (with cascade delete) is configured on ExpenseConfiguration.
        // Every other reference is Restrict so a line item has exactly one cascade path (via its expense).
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(l => l.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(l => l.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // The member a line was "for" (null = Family).
        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(l => l.ForMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ValueTag>()
            .WithMany()
            .HasForeignKey(l => l.ValueTagId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(l => l.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
