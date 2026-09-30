using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.Property(m => m.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Email).HasMaxLength(320);
        builder.Property(m => m.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.ExternalId).HasMaxLength(200);
        builder.Property(m => m.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(m => m.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // One member per provider identity. Filtered so the many children with no login (NULL
        // ExternalId) are not treated as duplicates.
        builder.HasIndex(m => new { m.Provider, m.ExternalId })
            .IsUnique()
            .HasFilter("[ExternalId] IS NOT NULL");
    }
}
