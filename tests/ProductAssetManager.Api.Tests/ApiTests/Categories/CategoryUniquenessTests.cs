using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Categories;

[TestFixture]
public class CategoryUniquenessTests : ApiTestBase
{
    [SetUp]
    public async Task AuthorizeAsAdmin()
    {
        var token = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Test]
    public async Task When_CreatingTwoRootCategoriesWithSameName_Should_ReturnBadRequest()
    {
        var first = await Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Women" });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var second = await Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Women" });

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task When_CreatingTwoSiblingCategoriesWithSameNameUnderSameParent_Should_ReturnBadRequest()
    {
        var parentResponse = await Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Women" });
        var parent = await parentResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var firstChild = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Clothing", ParentCategoryId = parent!.Id });
        Assert.That(firstChild.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var secondChild = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Clothing", ParentCategoryId = parent.Id });

        Assert.That(secondChild.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task When_CreatingSameNameUnderDifferentParents_Should_ReturnCreated()
    {
        var womenResponse = await Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Women" });
        var women = await womenResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var menResponse = await Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Men" });
        var men = await menResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var womenShoes = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Shoes", ParentCategoryId = women!.Id });
        var menShoes = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Shoes", ParentCategoryId = men!.Id });

        Assert.That(womenShoes.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(menShoes.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }
}
