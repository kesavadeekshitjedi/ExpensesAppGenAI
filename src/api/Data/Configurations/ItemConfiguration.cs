using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.Property(i => i.FullName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(i => i.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: a category in use as an item's default cannot be hard-deleted (categories archive).
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(i => i.DefaultCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.HouseholdId, i.FullName }).IsUnique();
    }
}
