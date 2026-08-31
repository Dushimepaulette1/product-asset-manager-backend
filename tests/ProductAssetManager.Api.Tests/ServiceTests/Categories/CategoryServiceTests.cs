using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Services;
using ProductAssetManager.Api.Tests.ServiceTests;

namespace ProductAssetManager.Api.Tests.ServiceTests.Categories;

[TestFixture]
public class CategoryServiceTests : ServiceTestBase
{
    [Test]
    public async Task When_CategoryHasNoChildren_Should_BeTerminal()
    {
        var leaf = new Category { Name = "Dresses" };
        DbContext.Categories.Add(leaf);
        await DbContext.SaveChangesAsync();

        var categoryService = new CategoryService(DbContext);

        var isTerminal = await categoryService.IsTerminalAsync(leaf.Id);

        Assert.That(isTerminal, Is.True);
    }

    [Test]
    public async Task When_CategoryHasChildren_Should_NotBeTerminal()
    {
        var parent = new Category { Name = "Clothing" };
        DbContext.Categories.Add(parent);
        await DbContext.SaveChangesAsync();

        var child = new Category { Name = "Dresses", ParentCategoryId = parent.Id };
        DbContext.Categories.Add(child);
        await DbContext.SaveChangesAsync();

        var categoryService = new CategoryService(DbContext);

        var isTerminal = await categoryService.IsTerminalAsync(parent.Id);

        Assert.That(isTerminal, Is.False);
    }
}
