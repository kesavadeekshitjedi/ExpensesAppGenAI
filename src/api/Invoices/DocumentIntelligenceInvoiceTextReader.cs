using Azure;
using Azure.AI.DocumentIntelligence;

namespace Expenses.Api.Invoices;

// Extracts a document's text with Document Intelligence's prebuilt-read model. We only need the raw
// text layer; the vendor-specific parser turns it into a ParsedInvoice. prebuilt-read handles PDFs
// natively, so an Amazon "Order Summary" print-to-PDF is read without any conversion.
public sealed class DocumentIntelligenceInvoiceTextReader(DocumentIntelligenceClient client) : IInvoiceTextReader
{
    public async Task<string> ReadTextAsync(BinaryData document, CancellationToken ct = default)
    {
        Operation<AnalyzeResult> operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed, "prebuilt-read", document, ct);
        return operation.Value.Content ?? string.Empty;
    }
}
