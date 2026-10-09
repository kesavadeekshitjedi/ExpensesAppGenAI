using Azure;
using Azure.AI.DocumentIntelligence;

namespace Expenses.Api.Receipts;

// Runs the prebuilt-receipt model and normalizes the fields we use into an ExtractedReceipt. Only
// text is extracted (SPEC decision #16); matching printed descriptions to items is done by the app.
public sealed class DocumentIntelligenceReceiptReader(DocumentIntelligenceClient client) : IReceiptReader
{
    public async Task<ExtractedReceipt> AnalyzeAsync(BinaryData image, CancellationToken ct = default)
    {
        Operation<AnalyzeResult> operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed, "prebuilt-receipt", image, ct);

        var document = operation.Value.Documents.FirstOrDefault();
        if (document is null)
        {
            return new ExtractedReceipt(null, null, null, null, []);
        }

        var fields = document.Fields;
        var lines = new List<ExtractedLine>();
        if (Get(fields, "Items")?.ValueList is { } items)
        {
            foreach (var item in items)
            {
                var itemFields = item.ValueDictionary;
                if (itemFields is null)
                {
                    continue;
                }
                var description = Get(itemFields, "Description")?.ValueString?.Trim();
                if (string.IsNullOrWhiteSpace(description))
                {
                    continue;
                }
                lines.Add(new ExtractedLine(
                    description,
                    Number(Get(itemFields, "Quantity")),
                    Money(Get(itemFields, "Price")),
                    Money(Get(itemFields, "TotalPrice"))));
            }
        }

        return new ExtractedReceipt(
            Get(fields, "MerchantName")?.ValueString?.Trim(),
            Date(Get(fields, "TransactionDate")),
            Money(Get(fields, "Total")),
            Money(Get(fields, "TotalTax")),
            lines);
    }

    private static DocumentField? Get(IReadOnlyDictionary<string, DocumentField> fields, string key) =>
        fields.TryGetValue(key, out var field) ? field : null;

    private static decimal? Money(DocumentField? field)
    {
        if (field is null) return null;
        if (field.ValueCurrency is not null) return (decimal)field.ValueCurrency.Amount;
        if (field.ValueDouble is double d) return (decimal)d;
        return null;
    }

    private static decimal? Number(DocumentField? field)
    {
        if (field is null) return null;
        if (field.ValueDouble is double d) return (decimal)d;
        if (field.ValueInt64 is long l) return l;
        return null;
    }

    private static DateOnly? Date(DocumentField? field) =>
        field?.ValueDate is DateTimeOffset dto ? DateOnly.FromDateTime(dto.Date) : null;
}
