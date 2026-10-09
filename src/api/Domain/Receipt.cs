namespace Expenses.Api.Domain;

// A photographed receipt behind a saved expense (SPEC feature 3). The image lives in Blob Storage;
// this row ties it to the expense it produced. The extracted text is shown to the user at scan time
// for review and is not persisted (the reviewed line items on the expense are the source of truth).
public class Receipt
{
    public Guid Id { get; set; }
    public Guid ExpenseId { get; set; }
    public required string ImageBlobName { get; set; }
    public ReceiptExtractionStatus ExtractionStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
