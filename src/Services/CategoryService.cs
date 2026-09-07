using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public class CategoryService : ICategoryService
{
    private readonly ApplicationDbContext _dbContext;

    public CategoryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CreateCategoryResult> CreateAsync(CreateCategoryRequest request)
    {
        if (request.ParentCategoryId.HasValue)
        {
            var parentExists = await _dbContext.Categories
                .AnyAsync(c => c.Id == request.ParentCategoryId.Value);

            if (!parentExists)
            {
                return new CreateCategoryResult(false, true, false, null);
            }
        }

        var duplicateExists = await _dbContext.Categories
            .AnyAsync(c => c.ParentCategoryId == request.ParentCategoryId && c.Name == request.Name);

        if (duplicateExists)
        {
            return new CreateCategoryResult(false, false, true, null);
        }

        var category = new Category
        {
            Name = request.Name,
            ParentCategoryId = request.ParentCategoryId
        };

        _dbContext.Categories.Add(category);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return new CreateCategoryResult(false, false, true, null);
        }

        var response = new CategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            ParentCategoryId = category.ParentCategoryId
        };

        return new CreateCategoryResult(true, false, false, response);
    }

    public async Task<List<CategoryResponse>> GetAllAsync()
    {
        var allCategories = await _dbContext.Categories
            .AsNoTracking()
            .ToListAsync();

        var byParent = allCategories
            .Where(c => c.ParentCategoryId.HasValue)
            .ToLookup(c => c.ParentCategoryId!.Value);

        CategoryResponse Map(Category category) => new()
        {
            Id = category.Id,
            Name = category.Name,
            ParentCategoryId = category.ParentCategoryId,
            Children = byParent[category.Id].Select(Map).ToList()
        };

        return allCategories
            .Where(c => c.ParentCategoryId is null)
            .Select(Map)
            .ToList();
    }

    public async Task<CategoryResponse?> GetByIdAsync(Guid id)
    {
        var category = await _dbContext.Categories
            .AsNoTracking()
            .Include(c => c.ParentCategory)
            .Include(c => c.ChildCategories)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null)
        {
            return null;
        }

        return new CategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            ParentCategoryId = category.ParentCategoryId,
            ParentCategoryName = category.ParentCategory?.Name,
            Children = category.ChildCategories
                .Select(child => new CategoryResponse
                {
                    Id = child.Id,
                    Name = child.Name,
                    ParentCategoryId = child.ParentCategoryId
                })
                .ToList()
        };
    }

    public async Task<bool> IsTerminalAsync(Guid categoryId)
    {
        return !await _dbContext.Categories.AnyAsync(c => c.ParentCategoryId == categoryId);
    }
}
