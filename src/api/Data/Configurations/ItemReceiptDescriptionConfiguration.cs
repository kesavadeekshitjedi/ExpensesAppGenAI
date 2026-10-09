using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class ItemReceiptDescriptionConfiguration : IEntityTypeConfiguration<ItemReceiptDescription>
{
    public void Configure(EntityTypeBuilder<ItemReceiptDescription> builder)
    {
        builder.Property(d => d.PrintedDescription).HasMaxLength(100).IsRequired();
        builder.Property(d => d.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        // Deleting an item removes its receipt descriptions (the single cascade path for this table).
        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(d => d.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict so there is only one cascade path into this table.
        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(d => d.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        // SPEC: unique per merchant + printed description.
        builder.HasIndex(d => new { d.MerchantId, d.PrintedDescription }).IsUnique();
    }
}
