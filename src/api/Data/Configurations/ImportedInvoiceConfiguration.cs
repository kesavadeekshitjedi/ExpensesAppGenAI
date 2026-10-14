using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class ImportedInvoiceConfiguration : IEntityTypeConfiguration<ImportedInvoice>
{
    public void Configure(EntityTypeBuilder<ImportedInvoice> builder)
    {
        builder.Property(i => i.Vendor).HasMaxLength(100).IsRequired();
        builder.Property(i => i.OrderNumber).HasMaxLength(100);
        builder.Property(i => i.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(i => i.BlobName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.GrandTotal).HasPrecision(18, 2);
        builder.Property(i => i.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        // Deleting an expense removes its import record (single cascade path: Household -> Expense -> ImportedInvoice).
        builder.HasOne<Expense>()
            .WithMany()
            .HasForeignKey(i => i.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Dedup lookups: by order number and by content hash, scoped to the household.
        builder.HasIndex(i => new { i.HouseholdId, i.OrderNumber });
        builder.HasIndex(i => new { i.HouseholdId, i.ContentHash });
    }
}
