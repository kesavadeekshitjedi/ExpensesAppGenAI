namespace Expenses.Api.Receipts;

// Reads the printed text off a receipt image (SPEC feature 3). Backed by Azure AI Document
// Intelligence's prebuilt-receipt model in Azure; a stub is used in tests and a no-op stand-in when
// the service is not configured. There is no AI interpretation of what items ARE (SPEC decision #16)
// — only text extraction; the app matches the printed text against its own item database.
public interface IReceiptReader
{
    Task<ExtractedReceipt> AnalyzeAsync(BinaryData image, CancellationToken ct = default);
}

public record ExtractedReceipt(
    string? Merchant,
    DateOnly? Date,
    decimal? Total,
    decimal? Tax,
    List<ExtractedLine> Lines);

public record ExtractedLine(
    string Description,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? Amount);
