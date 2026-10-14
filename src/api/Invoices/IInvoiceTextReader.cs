namespace Expenses.Api.Invoices;

// Extracts the text layer of an uploaded invoice/order file (a PDF) so a vendor-specific parser can
// read it. Backed by Azure AI Document Intelligence's prebuilt-read model in Azure; a no-op stand-in
// is used when the service is not configured, and tests supply a stub that returns canned text.
public interface IInvoiceTextReader
{
    Task<string> ReadTextAsync(BinaryData document, CancellationToken ct = default);
}

// One line of a parsed invoice. Amount is the line's total (quantity x unit price); Quantity/UnitPrice
// are included when the vendor's format exposes them.
public record ParsedInvoiceLine(string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount);

// The normalized result of parsing one invoice's text. OrderNumber is the vendor's stable order id
// (the primary dedup key) when present.
public record ParsedInvoice(
    string Vendor,
    string? OrderNumber,
    DateOnly? OrderDate,
    decimal? Subtotal,
    decimal? Tax,
    decimal? GrandTotal,
    List<ParsedInvoiceLine> Lines);

// Turns extracted invoice text into a ParsedInvoice. An implementation returns null when it does not
// recognize the text as its vendor's format, so the endpoint can try the next parser.
public interface IInvoiceParser
{
    ParsedInvoice? TryParse(string text);
}
