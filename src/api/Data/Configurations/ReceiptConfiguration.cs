using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expenses.Api.Data.Configurations;

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.Property(r => r.ImageBlobName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ExtractionStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        // Deleting an expense removes its receipt (the single cascade path: Household -> Expense -> Receipt).
        builder.HasOne<Expense>()
            .WithMany()
            .HasForeignKey(r => r.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.ExpenseId);
    }
}
