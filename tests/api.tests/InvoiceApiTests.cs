using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Expenses.Api.Tests;

// Drives the invoice import endpoint end to end with a stubbed text reader: parse an Amazon order into a
// draft, save it as an expense, and confirm re-importing the same order (even as a different file) is
// flagged as a duplicate at both the invoice and the line-item level.
public class InvoiceApiTests
{
    private const string AmazonText = """
        Order placed October 12, 2026   Order # 112-5242793-5269063
        Order Summary
        Item(s) Subtotal: $21.97
        Estimated tax to be
        collected:
        $0.87
        Grand Total: $24.34
        Delivered October 13
        Your package was left near the front door or porch.
        LEGO Creator 3 in 1 Space Shuttle Toys - Educational Gift - 31134
        Sold by: Amazon.com
        Supplied by: Other
        Return items: Eligible through November 12, 2026
        $7.99
        2
        Kraft Gluten Free Mac and Cheese, 4 Microwaveable Cups, Original, 1.9 oz
        Sold by: Amazon.com
        Supplied by: Other
        Return items: Eligible through November 12, 2026
        $6.99
        """;

    private record ImportLine(string printedDescription, decimal quantity, decimal unitPrice, decimal amount,
        bool matched, Guid? itemId, string? itemFullName, Guid? suggestedCategoryId, string? suggestedValueTag,
        bool possiblyDuplicate, Guid? duplicateExpenseId);
    private record DuplicateInfo(Guid expenseId, string vendor, string? orderNumber, decimal grandTotal, string reason);
    private record ImportResult(string invoiceBlobName, string contentHash, string vendor, string? orderNumber,
        DateOnly? date, decimal? subtotal, decimal? tax, decimal? grandTotal, decimal lineSum, decimal difference,
        DuplicateInfo? duplicate, List<ImportLine> lines);

    private static MultipartFormDataContent PdfForm(byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { file, "file", "order.pdf" } };
    }

    // A minimal valid PDF header ("%PDF-") plus arbitrary trailing bytes so IsPdf passes and the hash varies.
    private static byte[] FakePdf(string tail) => "%PDF-1.4\n"u8.ToArray().Concat(System.Text.Encoding.UTF8.GetBytes(tail)).ToArray();

    [Fact]
    public async Task Import_ParsesAmazonOrderIntoDraft()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        factory.Invoices.Text = AmazonText;
        var client = factory.CreateClient();

        var res = await client.PostAsync("/invoices/import", PdfForm(FakePdf("a")));
        res.EnsureSuccessStatusCode();
        var draft = await res.Content.ReadFromJsonAsync<ImportResult>();

        Assert.Equal("Amazon", draft!.vendor);
        Assert.Equal("112-5242793-5269063", draft.orderNumber);
        Assert.Equal(24.34m, draft.grandTotal);
        Assert.Equal(2, draft.lines.Count);
        Assert.Null(draft.duplicate);
        Assert.Equal(2m, draft.lines[1].quantity);
        Assert.Equal(13.98m, draft.lines[1].amount);
    }

    [Fact]
    public async Task Import_SameOrderDifferentFile_IsFlaggedAsDuplicate()
    {
        var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        await using var _f = factory;
        factory.Invoices.Text = AmazonText;
        var client = factory.CreateClient();

        var first = await (await client.PostAsync("/invoices/import", PdfForm(FakePdf("one")))).Content.ReadFromJsonAsync<ImportResult>();

        // Save the draft as an expense (Source = Invoice) so its order identity is recorded.
        var save = await client.PostAsJsonAsync("/expenses", new
        {
            merchant = first!.vendor,
            paymentMethodId = method.Id,
            date = first.date,
            tax = first.tax,
            lineItems = first.lines.Select(l => new
            {
                description = l.printedDescription,
                categoryId = category.Id,
                quantity = l.quantity,
                unitPrice = l.unitPrice,
                amount = l.amount,
                fullName = l.printedDescription,
            }),
            source = "Invoice",
            invoiceBlobName = first.invoiceBlobName,
            orderNumber = first.orderNumber,
            invoiceContentHash = first.contentHash,
        });
        save.EnsureSuccessStatusCode();

        // Re-import the SAME order as a DIFFERENT file (different bytes => different hash, same order number).
        var again = await (await client.PostAsync("/invoices/import", PdfForm(FakePdf("two")))).Content.ReadFromJsonAsync<ImportResult>();

        Assert.NotNull(again!.duplicate);
        Assert.Equal("112-5242793-5269063", again.duplicate!.orderNumber);
        Assert.Contains("already imported", again.duplicate.reason);
        // Every line is also flagged as already recorded on the same date.
        Assert.All(again.lines, l => Assert.True(l.possiblyDuplicate));
    }
}
