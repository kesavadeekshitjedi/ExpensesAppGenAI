namespace Expenses.Api.Receipts;

// Registered when DocumentIntelligence:Endpoint is not configured (local dev without Azure). Scanning
// fails clearly rather than the app failing to start; manual entry is unaffected.
public sealed class NullReceiptReader : IReceiptReader
{
    public Task<ExtractedReceipt> AnalyzeAsync(BinaryData image, CancellationToken ct = default) =>
        throw new InvalidOperationException("Receipt reading is not configured (set DocumentIntelligence:Endpoint).");
}
