using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class MobileRefreshTokenConfiguration : IEntityTypeConfiguration<MobileRefreshToken>
{
    public void Configure(EntityTypeBuilder<MobileRefreshToken> builder)
    {
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.MemberId);

        // One cascade path: Household -> Member -> MobileRefreshToken.
        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(t => t.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
