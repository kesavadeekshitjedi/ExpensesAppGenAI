using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Expenses.Api.Domain;
using Expenses.Api.Receipts;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Tests;

// Drives receipt capture (step 11): scanning a receipt into a draft, matching known items, and saving
// the reviewed draft as a receipt-sourced expense. Document Intelligence is stubbed (see TestApiFactory).
public class ReceiptApiTests
{
    private record LinePayload(string description, Guid categoryId, Guid? forMemberId, decimal? quantity,
        decimal? unitPrice, decimal? amount, string? valueTag, string? notes, string? shortForm,
        Guid? itemId, string? fullName);
    private record ExpensePayload(string merchant, Guid paymentMethodId, string? date, decimal? tax,
        string? notes, object[] lineItems, string? source, string? receiptBlobName);

    private record ScanDto(string receiptBlobName, string? merchant, string? date, decimal? total, decimal? tax,
        decimal lineSum, decimal difference, List<ScanLineDto> lines);
    private record ScanLineDto(string printedDescription, decimal quantity, decimal unitPrice, decimal amount,
        bool matched, Guid? itemId, string? itemFullName, Guid? suggestedCategoryId, string? suggestedValueTag);
    private record ExpenseDto(Guid id, string merchant, decimal total, string source, List<LineDto> lineItems);
    private record LineDto(string description, decimal amount, Guid? itemId);

    private static MultipartFormDataContent Jpeg()
    {
        var content = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 });
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { content, "file", "receipt.jpg" } };
    }

    [Fact]
    public async Task Scan_WithUnknownMerchant_StoresImageAndReturnsUnmatchedLines()
    {
        await using var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        factory.Receipts.Result = new ExtractedReceipt("New Store", new DateOnly(2026, 10, 1), 7.50m, 0.50m,
            [new ExtractedLine("WIDGET", 1, 7m, 7m)]);
        var client = factory.CreateClient();

        var scan = await (await client.PostAsync("/receipts/scan", Jpeg())).Content.ReadFromJsonAsync<ScanDto>();
        Assert.NotNull(scan);
        Assert.Equal("New Store", scan!.merchant);
        Assert.Equal(7.00m, scan.lineSum);
        Assert.Equal(0.50m, scan.difference);              // total 7.50 - lines 7.00
        var line = Assert.Single(scan.lines);
        Assert.False(line.matched);
        Assert.NotNull(scan.receiptBlobName);

        Assert.Equal(1, factory.Blobs.Count);              // the image was stored
    }

    [Fact]
    public async Task Scan_MatchesAnItemAlreadyKnownAtThatMerchant()
    {
        await using var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        // A prior manual expense teaches the item database "KIRKL ORGAN EGGS" at Costco.
        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Costco", method.Id, null, null, null,
            [new LinePayload("Kirkland Organic Eggs", category.Id, null, 1, 3.50m, null, null, null, null, null, null)],
            null, null));

        factory.Receipts.Result = new ExtractedReceipt("Costco", null, 3.50m, null,
            [new ExtractedLine("KIRKL ORGAN EGGS", 1, 3.50m, 3.50m)]);

        var scan = await (await client.PostAsync("/receipts/scan", Jpeg())).Content.ReadFromJsonAsync<ScanDto>();
        var line = Assert.Single(scan!.lines);
        Assert.True(line.matched);
        Assert.Equal("Kirkland Organic Eggs", line.itemFullName);
        Assert.NotNull(line.itemId);
    }

    [Fact]
    public async Task Scan_AsChild_IsForbidden()
    {
        await using var factory = new TestApiFactory { Role = MemberRole.Child };
        await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        var res = await client.PostAsync("/receipts/scan", Jpeg());
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task SaveReviewedReceipt_CreatesReceiptExpense_AndBuildsItemDatabase()
    {
        await using var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        // Seed a known item at Costco so the first line can be matched by id.
        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Costco", method.Id, null, null, null,
            [new LinePayload("Kirkland Organic Eggs", category.Id, null, 1, 3.50m, null, null, null, null, null, null)],
            null, null));

        factory.Receipts.Result = new ExtractedReceipt("Costco", null, null, null,
        [
            new ExtractedLine("KIRKL ORGAN EGGS", 1, 3.50m, 3.50m),
            new ExtractedLine("ORG BANANAS", 1, 1.25m, 1.25m),
            new ExtractedLine("MYSTERY", 1, 2m, 2m),
        ]);
        var scan = await (await client.PostAsync("/receipts/scan", Jpeg())).Content.ReadFromJsonAsync<ScanDto>();
        var matchedId = scan!.lines.First(l => l.matched).itemId;

        var save = new ExpensePayload("Costco", method.Id, "2026-10-02", null, null,
        [
            // matched existing item (chosen by id)
            new LinePayload("KIRKL ORGAN EGGS", category.Id, null, 1, 3.50m, 3.50m, null, null, null, matchedId, null),
            // a new item named by the user
            new LinePayload("ORG BANANAS", category.Id, null, 1, 1.25m, 1.25m, null, null, null, null, "Organic Bananas"),
            // left unnamed — saved with just the printed description
            new LinePayload("MYSTERY", category.Id, null, 1, 2m, 2m, null, null, null, null, null),
        ], source: "Receipt", receiptBlobName: scan.receiptBlobName);

        var response = await client.PostAsJsonAsync("/expenses", save);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ExpenseDto>();
        Assert.Equal("Receipt", body!.source);
        Assert.Equal(6.75m, body.total);
        Assert.All(body.lineItems, l => Assert.NotNull(l.itemId));

        using var db = factory.NewDbContext();
        Assert.Equal(1, await db.Receipts.CountAsync(r => r.ImageBlobName == scan.receiptBlobName));
        Assert.Equal(3, await db.Items.CountAsync());                       // eggs + bananas + mystery
        Assert.True(await db.Items.AnyAsync(i => i.FullName == "Organic Bananas"));
        Assert.True(await db.Items.AnyAsync(i => i.FullName == "MYSTERY"));
        // Costco now prints three forms (eggs reused its existing one).
        var costco = await db.Merchants.FirstAsync(m => m.Name == "Costco");
        Assert.Equal(3, await db.ItemReceiptDescriptions.CountAsync(d => d.MerchantId == costco.Id));
    }
}
