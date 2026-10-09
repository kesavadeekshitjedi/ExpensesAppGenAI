using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.IsTaxable).HasDefaultValue(true);
        builder.Property(c => c.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(c => c.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // A category name is unique within a household (case-insensitive by the database's default collation).
        builder.HasIndex(c => new { c.HouseholdId, c.Name }).IsUnique();
    }
}
