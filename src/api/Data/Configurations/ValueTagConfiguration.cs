using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class ValueTagConfiguration : IEntityTypeConfiguration<ValueTag>
{
    public void Configure(EntityTypeBuilder<ValueTag> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(50).IsRequired();
        builder.Property(t => t.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(t => t.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: Household already cascades to value tags; the creator member is just a reference.
        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(t => t.CreatedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique per household, case-insensitive (SPEC feature 4).
        builder.HasIndex(t => new { t.HouseholdId, t.Name }).IsUnique();
    }
}
