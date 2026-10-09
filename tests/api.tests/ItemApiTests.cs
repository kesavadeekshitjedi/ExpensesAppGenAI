using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Tests;

// Drives the item-database endpoints (step 10) through the real HTTP pipeline (see TestApiFactory).
public class ItemApiTests
{
    private record LinePayload(string description, Guid categoryId, Guid? forMemberId, decimal? quantity,
        decimal? unitPrice, decimal? amount, string? valueTag, string? notes, string? shortForm);
    private record ExpensePayload(string merchant, Guid paymentMethodId, string? date, decimal? tax,
        string? notes, object[] lineItems);

    private record ItemDto(Guid id, string fullName, Guid? defaultCategoryId, string? defaultCategory,
        Guid? defaultValueTagId, string? defaultValueTag, bool hasPicture, List<ReceiptDescDto> receiptDescriptions);
    private record ReceiptDescDto(string merchant, string printedDescription);

    // Enters one expense so the item database has a single item with a Costco receipt description.
    private static async Task<(HttpClient client, TestApiFactory factory, Category category, PaymentMethod method)>
        SeedWithOneItemAsync()
    {
        var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Costco", method.Id, null, null, null,
            [new LinePayload("Kirkland Organic Eggs", category.Id, null, 1, 3.50m, null, null, null, null)]));
        return (client, factory, category, method);
    }

    [Fact]
    public async Task GetItems_ReturnsItemBuiltByExpenseEntry_WithReceiptDescription()
    {
        var (client, factory, _, _) = await SeedWithOneItemAsync();
        await using var _f = factory;

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/items");
        Assert.NotNull(items);
        var item = Assert.Single(items!);
        Assert.Equal("Kirkland Organic Eggs", item.fullName);
        var desc = Assert.Single(item.receiptDescriptions);
        Assert.Equal("Costco", desc.merchant);
        Assert.Equal("KIRKL ORGAN EGGS", desc.printedDescription);
        Assert.False(item.hasPicture);
    }

    [Fact]
    public async Task GetItems_Search_FiltersByFullName()
    {
        var (client, factory, _, _) = await SeedWithOneItemAsync();
        await using var _f = factory;

        Assert.Single((await client.GetFromJsonAsync<List<ItemDto>>("/items?search=kirkland"))!);
        Assert.Empty((await client.GetFromJsonAsync<List<ItemDto>>("/items?search=notthere"))!);
    }

    [Fact]
    public async Task PatchItem_RenamesAndSetsDefaults_CreatingValueTag()
    {
        var (client, factory, category, _) = await SeedWithOneItemAsync();
        await using var _f = factory;
        var item = (await client.GetFromJsonAsync<List<ItemDto>>("/items"))![0];

        var res = await client.PatchAsJsonAsync($"/items/{item.id}", new
        {
            fullName = "Kirkland Signature Organic Eggs, 24 ct",
            defaultCategoryId = category.Id,
            defaultValueTag = "Needed",
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var updated = await res.Content.ReadFromJsonAsync<ItemDto>();
        Assert.Equal("Kirkland Signature Organic Eggs, 24 ct", updated!.fullName);
        Assert.Equal(category.Id, updated.defaultCategoryId);
        Assert.Equal("Needed", updated.defaultValueTag);

        using var db = factory.NewDbContext();
        Assert.Single(await db.ValueTags.ToListAsync());
    }

    [Fact]
    public async Task PatchItem_AsChild_IsForbidden()
    {
        var (client, factory, _, _) = await SeedWithOneItemAsync();
        await using var _f = factory;
        var item = (await client.GetFromJsonAsync<List<ItemDto>>("/items"))![0];

        factory.Role = MemberRole.Child;
        using var childClient = factory.CreateClient();
        var res = await childClient.PatchAsJsonAsync($"/items/{item.id}", new { fullName = "Hacked" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task UploadAndGetPicture_RoundTrips_AndRejectsNonImages()
    {
        var (client, factory, _, _) = await SeedWithOneItemAsync();
        await using var _f = factory;
        var item = (await client.GetFromJsonAsync<List<ItemDto>>("/items"))![0];

        // A JPEG starts with FF D8 FF; the endpoint stores the original bytes.
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
        var ok = await client.PostAsync($"/items/{item.id}/picture", ImagePart(jpeg, "photo.jpg", "image/jpeg"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True((await ok.Content.ReadFromJsonAsync<ItemDto>())!.hasPicture);

        var picture = await client.GetAsync($"/items/{item.id}/picture");
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal(jpeg, await picture.Content.ReadAsByteArrayAsync());

        var notImage = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // "%PDF"
        var rejected = await client.PostAsync($"/items/{item.id}/picture", ImagePart(notImage, "doc.pdf", "application/pdf"));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task MergeItems_MovesReceiptDescriptionsAndLines_ThenDeletesSource()
    {
        var (client, factory, _, _) = await SeedWithOneItemAsync();
        await using var _f = factory;
        var source = (await client.GetFromJsonAsync<List<ItemDto>>("/items"))![0]; // "Kirkland Organic Eggs" + a line

        // A second item for the same merchant, printed differently, is the merge target.
        Guid merchantId;
        Guid targetId = Guid.NewGuid();
        using (var db = factory.NewDbContext())
        {
            merchantId = (await db.Merchants.FirstAsync()).Id;
            db.Items.Add(new Item { Id = targetId, HouseholdId = factory.HouseholdId, FullName = "KS Eggs" });
            db.ItemReceiptDescriptions.Add(new ItemReceiptDescription
            {
                Id = Guid.NewGuid(), ItemId = targetId, MerchantId = merchantId, PrintedDescription = "KS EGGS",
            });
            await db.SaveChangesAsync();
        }

        var res = await client.PostAsJsonAsync($"/items/{targetId}/merge", new { sourceItemId = source.id });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var merged = await res.Content.ReadFromJsonAsync<ItemDto>();
        Assert.Equal("KS Eggs", merged!.fullName);
        Assert.Equal(2, merged.receiptDescriptions.Count); // both printed forms now point here

        using var check = factory.NewDbContext();
        Assert.Null(await check.Items.FirstOrDefaultAsync(i => i.Id == source.id)); // source gone
        Assert.True(await check.LineItems.AllAsync(l => l.ItemId == targetId));     // line moved
        Assert.Equal(2, await check.ItemReceiptDescriptions.CountAsync(d => d.ItemId == targetId));
    }

    private static MultipartFormDataContent ImagePart(byte[] bytes, string fileName, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }
}
