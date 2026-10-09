using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.Property(v => v.Name).HasMaxLength(100).IsRequired();
        builder.Property(v => v.Make).HasMaxLength(60);
        builder.Property(v => v.Model).HasMaxLength(60);
        builder.Property(v => v.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        // Vehicle names are unique within a household.
        builder.HasIndex(v => new { v.HouseholdId, v.Name }).IsUnique();
    }
}
