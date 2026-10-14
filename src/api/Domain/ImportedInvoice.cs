namespace Expenses.Api.Domain;

// Identity of an invoice/order file that was imported into an expense, so the same order is not
// imported twice — even if the file is renamed or re-downloaded (its bytes differ). Dedup keys, in
// order of reliability: the vendor OrderNumber (e.g. an Amazon order id, stable across re-downloads),
// then the ContentHash (SHA-256 of the exact file bytes, catches an identical re-upload). The stored
// PDF lives in Blob Storage; this row ties it and the order identity to the expense it produced.
public class ImportedInvoice
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid ExpenseId { get; set; }

    // The vendor as parsed (e.g. "Amazon"); free text, not a Merchant FK.
    public required string Vendor { get; set; }

    // The vendor's order/invoice number when we could parse one (the primary dedup key); may be null.
    public string? OrderNumber { get; set; }

    // SHA-256 (hex) of the uploaded file bytes (the secondary dedup key).
    public required string ContentHash { get; set; }

    public DateOnly PurchaseDate { get; set; }
    public decimal GrandTotal { get; set; }
    public required string BlobName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
