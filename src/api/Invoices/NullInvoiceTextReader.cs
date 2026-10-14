namespace Expenses.Api.Invoices;

// Registered when DocumentIntelligence:Endpoint is not configured (local dev without Azure). Importing
// fails clearly rather than the app failing to start; manual entry is unaffected.
public sealed class NullInvoiceTextReader : IInvoiceTextReader
{
    public Task<string> ReadTextAsync(BinaryData document, CancellationToken ct = default) =>
        throw new InvalidOperationException("Invoice reading is not configured (set DocumentIntelligence:Endpoint).");
}
