using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Expenses.Api.Receipts;

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
        Guid? suggestedVehicleId, bool possiblyDuplicate, Guid? duplicateExpenseId);
    private record DuplicateInfo(Guid expenseId, string vendor, string? orderNumber, decimal grandTotal, string reason);
    private record MatchDto(Guid id, string merchant, DateOnly date, decimal total, string source, int lineCount);
    private record ImportResult(string invoiceBlobName, string contentHash, string vendor, string? orderNumber,
        DateOnly? date, decimal? subtotal, decimal? tax, decimal? grandTotal, decimal lineSum, decimal difference,
        DuplicateInfo? duplicate, MatchDto? possibleMatch, Guid? suggestedPaymentMethodId, List<ImportLine> lines);

    private static MultipartFormDataContent PdfForm(byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { file, "file", "order.pdf" } };
    }

    private static MultipartFormDataContent JpgForm(byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { file, "file", "receipt.jpg" } };
    }

    // A minimal valid PDF header ("%PDF-") plus arbitrary trailing bytes so IsPdf passes and the hash varies.
    private static byte[] FakePdf(string tail) => "%PDF-1.4\n"u8.ToArray().Concat(System.Text.Encoding.UTF8.GetBytes(tail)).ToArray();

    // A JPEG magic-number header (0xFF 0xD8 0xFF) plus arbitrary trailing bytes so the image is accepted.
    private static byte[] FakeJpg(string tail) => new byte[] { 0xFF, 0xD8, 0xFF }.Concat(System.Text.Encoding.UTF8.GetBytes(tail)).ToArray();

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

    private const string CostcoGasText = """
        Redmond #1225
        Member 112053396512
        Invoice# 96308
        Date: 10/07/26
        Pump Gallons Price
        18 15.868 $5.799
        Product Amount
        Premium $92.02
        Total Sale $92.02
        TranID# 628018096308
        Visit Costco.com
        """;

    private record CategoryDto(Guid id, string name);
    private record VehicleDto(Guid id, string name);

    [Fact]
    public async Task Import_CostcoGas_SuggestsGasCategoryAndPremiumVehicle()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        factory.Invoices.Text = CostcoGasText;
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/categories", new { name = "Gas", isTaxable = false });
        await client.PostAsJsonAsync("/vehicles", new { name = "GLA 250" });
        await client.PostAsJsonAsync("/vehicles", new { name = "Honda Pilot" });
        var gasId = (await client.GetFromJsonAsync<List<CategoryDto>>("/categories"))!.First(c => c.name == "Gas").id;
        var glaId = (await client.GetFromJsonAsync<List<VehicleDto>>("/vehicles"))!.First(v => v.name == "GLA 250").id;

        var draft = await (await client.PostAsync("/invoices/import", PdfForm(FakePdf("gas")))).Content.ReadFromJsonAsync<ImportResult>();

        var line = Assert.Single(draft!.lines);
        Assert.Equal("Costco Premium Fuel", line.printedDescription);
        Assert.Equal(gasId, line.suggestedCategoryId);      // mapped the "Gas" hint to the household category
        Assert.Equal(glaId, line.suggestedVehicleId);       // premium fuel -> GLA 250
    }

    // A photographed grocery receipt (JPEG) is read by the structured receipt model and each printed line
    // becomes its own draft line, so reports can later drill down to the individual item.
    [Fact]
    public async Task Import_ImageReceipt_ItemizesIntoSeparateLines()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        factory.Receipts.Result = new ExtractedReceipt(
            "Swagath", new DateOnly(2026, 10, 14), 6.44m, 0m,
            [
                new ExtractedLine("Dosakai - lb", 0.73m, 2.99m, 2.18m),
                new ExtractedLine("Banana Burro Green - lb", 1.86m, 2.29m, 4.26m),
            ],
            RawText: "Swagath RECEIPT Total $6.44");
        var client = factory.CreateClient();

        var draft = await (await client.PostAsync("/invoices/import", JpgForm(FakeJpg("a")))).Content.ReadFromJsonAsync<ImportResult>();

        Assert.Equal("Swagath", draft!.vendor);
        Assert.Equal(new DateOnly(2026, 10, 14), draft.date);
        Assert.Equal(2, draft.lines.Count);
        Assert.Equal("Dosakai - lb", draft.lines[0].printedDescription);
        Assert.Equal(4.26m, draft.lines[1].amount);
    }

    // A Costco warehouse receipt PDF prints "SUBTOTAL/TAX/TOTAL" (not the gas-only "Total Sale"), so no
    // text parser recognizes it; the import falls back to the structured receipt model to itemize it.
    [Fact]
    public async Task Import_UnrecognizedPdf_FallsBackToReceiptItemization()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        factory.Invoices.Text = "REDMOND #1225 Costco Wholesale SUBTOTAL 157.96 TAX 0.00 TOTAL 157.96";
        factory.Receipts.Result = new ExtractedReceipt(
            "Costco", new DateOnly(2026, 10, 4), 157.96m, 0m,
            [
                new ExtractedLine("KS ORG A2 4 C", 1m, 34.98m, 34.98m),
                new ExtractedLine("BHUJIA MIX", 1m, 7.99m, 7.99m),
                new ExtractedLine("ORG ASPARAGUS", 1m, 11.99m, 11.99m),
            ],
            RawText: "COSTCO WHOLESALE");
        var client = factory.CreateClient();

        var draft = await (await client.PostAsync("/invoices/import", PdfForm(FakePdf("warehouse")))).Content.ReadFromJsonAsync<ImportResult>();

        Assert.Equal("Costco", draft!.vendor);
        Assert.Equal(3, draft.lines.Count);
        Assert.Null(draft.orderNumber); // warehouse receipts have no stable order id; dedup falls back to the file hash
    }

    private record PaymentMethodDto(Guid id, string label);

    // The card printed on the bill selects the matching household payment method (by last-4 in its label).
    [Fact]
    public async Task Import_SuggestsPaymentMethodFromPrintedCard()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        // A Costco gas receipt that prints the card: "COSTCO VISA ... XXXXXXXX7039".
        factory.Invoices.Text = CostcoGasText + "\nVI Acct #XXXXXXXXXXXX7039\nCOSTCO VISA\n";
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/payment-methods", new { label = "Costco Citi 7039", type = "CreditCard" });
        var costcoCardId = (await client.GetFromJsonAsync<List<PaymentMethodDto>>("/payment-methods"))!
            .First(p => p.label == "Costco Citi 7039").id;

        var draft = await (await client.PostAsync("/invoices/import", PdfForm(FakePdf("gascard")))).Content.ReadFromJsonAsync<ImportResult>();

        Assert.Equal(costcoCardId, draft!.suggestedPaymentMethodId);
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

    private record ExpenseLineDto(string description, decimal amount, string? vehicle);
    private record ExpenseDto(Guid id, string source, decimal total, List<ExpenseLineDto> lineItems);

    [Fact]
    public async Task Import_MatchingAManualEntry_OffersUpdateAndReplacesIt()
    {
        var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        await using var _f = factory;
        factory.Invoices.Text = AmazonText;
        var client = factory.CreateClient();

        // A manual entry for the same day and total ($24.34) the Amazon order comes to.
        var manual = await client.PostAsJsonAsync("/expenses", new
        {
            merchant = "Amazon",
            paymentMethodId = method.Id,
            date = new DateOnly(2026, 10, 12),
            lineItems = new[] { new { description = "Stuff", categoryId = category.Id, amount = 24.34m } },
        });
        manual.EnsureSuccessStatusCode();
        var manualExpense = await manual.Content.ReadFromJsonAsync<ExpenseDto>();

        // Importing the invoice detects the manual entry as a possible match.
        var draft = await (await client.PostAsync("/invoices/import", PdfForm(FakePdf("x")))).Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(draft!.possibleMatch);
        Assert.Equal(manualExpense!.id, draft.possibleMatch!.id);

        // Updating that entry replaces its lines and marks it an invoice (no new expense created).
        var put = await client.PutAsJsonAsync($"/expenses/{manualExpense.id}", new
        {
            merchant = draft.vendor,
            paymentMethodId = method.Id,
            date = draft.date,
            tax = draft.tax,
            lineItems = draft.lines.Select(l => new { description = l.printedDescription, categoryId = category.Id, quantity = l.quantity, unitPrice = l.unitPrice, amount = l.amount, fullName = l.printedDescription }),
            source = "Invoice",
            invoiceBlobName = draft.invoiceBlobName,
            orderNumber = draft.orderNumber,
            invoiceContentHash = draft.contentHash,
        });
        put.EnsureSuccessStatusCode();
        var updated = await put.Content.ReadFromJsonAsync<ExpenseDto>();

        Assert.Equal(manualExpense.id, updated!.id);  // same expense, replaced
        Assert.Equal("Invoice", updated.source);
        Assert.Equal(2, updated.lineItems.Count);

        var all = await client.GetFromJsonAsync<List<ExpenseDto>>("/expenses");
        Assert.Single(all!); // still one expense, not a duplicate
    }
}
