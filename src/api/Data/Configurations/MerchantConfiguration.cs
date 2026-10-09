using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.Property(m => m.Name).HasMaxLength(200).IsRequired();
        builder.Property(m => m.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(m => m.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // Reused by name within a household (case-insensitive by the database's default collation).
        builder.HasIndex(m => new { m.HouseholdId, m.Name }).IsUnique();
    }
}
