using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.Property(i => i.Email).HasMaxLength(320);
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Code).HasMaxLength(64).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(i => i.Code).IsUnique();

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(i => i.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict (not cascade) so deleting a household has a single cascade path into Invitations
        // (via HouseholdId), which SQL Server requires.
        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(i => i.CreatedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(i => i.AcceptedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // The existing member a targeted invitation attaches to. Restrict (single cascade path rule).
        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(i => i.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
