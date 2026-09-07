using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Collections;

[TestFixture]
public class CollectionMembershipTests : ApiTestBase
{
    private Guid _productId;
    private Guid _collectionId;

    [SetUp]
    public async Task AuthorizeAsAdminAndSeedProductAndCollection()
    {
        var token = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var categoryResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Dresses" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var productResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Collection Test Dress",
            Description = "For testing collection membership",
            BasePrice = 34.99m,
            Material = "Cotton",
            CategoryId = category!.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "One Size", Sku = "COLLECTION-TEST-SKU", Quantity = 5 }
            }
        });
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponse>();
        _productId = product!.Id;

        var collectionResponse = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest { Name = "Summer Sale", Description = "Seasonal deals" });
        var collection = await collectionResponse.Content.ReadFromJsonAsync<CollectionResponse>();
        _collectionId = collection!.Id;
    }

    [Test]
    public async Task When_AddingProductToCollection_Should_AppearInCollectionsProductList()
    {
        var response = await Client.PostAsJsonAsync(
            $"/api/collections/{_collectionId}/products",
            new AddProductToCollectionRequest { ProductId = _productId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var collection = await Client.GetFromJsonAsync<CollectionResponse>($"/api/collections/{_collectionId}");

        Assert.That(collection, Is.Not.Null);
        Assert.That(collection!.Products.Select(p => p.Id), Does.Contain(_productId));
    }

    [Test]
    public async Task When_RemovingProductFromCollection_Should_NoLongerAppearInProductList()
    {
        await Client.PostAsJsonAsync(
            $"/api/collections/{_collectionId}/products",
            new AddProductToCollectionRequest { ProductId = _productId });

        var removeResponse = await Client.DeleteAsync($"/api/collections/{_collectionId}/products/{_productId}");

        Assert.That(removeResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var collection = await Client.GetFromJsonAsync<CollectionResponse>($"/api/collections/{_collectionId}");

        Assert.That(collection, Is.Not.Null);
        Assert.That(collection!.Products.Select(p => p.Id), Does.Not.Contain(_productId));
    }

    [Test]
    public async Task When_AddingSameProductTwice_Should_BeIdempotentAndReturnOk()
    {
        var first = await Client.PostAsJsonAsync(
            $"/api/collections/{_collectionId}/products",
            new AddProductToCollectionRequest { ProductId = _productId });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var second = await Client.PostAsJsonAsync(
            $"/api/collections/{_collectionId}/products",
            new AddProductToCollectionRequest { ProductId = _productId });
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var collection = await Client.GetFromJsonAsync<CollectionResponse>($"/api/collections/{_collectionId}");
        Assert.That(collection!.Products.Count(p => p.Id == _productId), Is.EqualTo(1));
    }

    [Test]
    public async Task When_AddingProductToMultipleCollections_Should_BelongToBothWithoutConflict()
    {
        var secondCollectionResponse = await Client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest { Name = "New Arrivals", Description = "Fresh stock" });
        var secondCollection = await secondCollectionResponse.Content.ReadFromJsonAsync<CollectionResponse>();

        await Client.PostAsJsonAsync(
            $"/api/collections/{_collectionId}/products",
            new AddProductToCollectionRequest { ProductId = _productId });
        await Client.PostAsJsonAsync(
            $"/api/collections/{secondCollection!.Id}/products",
            new AddProductToCollectionRequest { ProductId = _productId });

        var firstCollection = await Client.GetFromJsonAsync<CollectionResponse>($"/api/collections/{_collectionId}");
        var second = await Client.GetFromJsonAsync<CollectionResponse>($"/api/collections/{secondCollection.Id}");

        Assert.That(firstCollection!.Products.Select(p => p.Id), Does.Contain(_productId));
        Assert.That(second!.Products.Select(p => p.Id), Does.Contain(_productId));
    }
}
