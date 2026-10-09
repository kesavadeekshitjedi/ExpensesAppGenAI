namespace Expenses.Api.Domain;

public enum ReceiptExtractionStatus
{
    // Document Intelligence read the receipt and the user reviewed and saved it.
    Extracted,

    // The image was kept but extraction failed; the user entered the lines by hand.
    Failed,
}
