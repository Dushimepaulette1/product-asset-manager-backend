using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public class ProductService : IProductService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICategoryService _categoryService;

    public ProductService(ApplicationDbContext dbContext, ICategoryService categoryService)
    {
        _dbContext = dbContext;
        _categoryService = categoryService;
    }

    public async Task<CreateProductResult> CreateAsync(CreateProductRequest request)
    {
        var category = await _dbContext.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId);

        if (category is null)
        {
            return new CreateProductResult(false, true, null, null);
        }

        var isTerminal = await _categoryService.IsTerminalAsync(category.Id);

        if (!isTerminal)
        {
            return Fail("The assigned category must be terminal (have no child categories).");
        }

        if (request.BasePrice <= 0)
        {
            return Fail("BasePrice must be a positive number.");
        }

        if (request.Variants.Count == 0)
        {
            return Fail("At least one variant is required.");
        }

        foreach (var variant in request.Variants)
        {
            if (variant.Quantity < 0)
            {
                return Fail($"Variant '{variant.Sku}' has an invalid quantity; it must be zero or greater.");
            }
        }

        var requestedSkus = request.Variants.Select(v => v.Sku).ToList();
        var duplicatesInRequest = requestedSkus
            .GroupBy(sku => sku)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicatesInRequest.Count > 0)
        {
            return Fail($"Duplicate SKU(s) within the request: {string.Join(", ", duplicatesInRequest)}.");
        }

        var existingSkus = await _dbContext.Variants
            .Where(v => requestedSkus.Contains(v.SKU))
            .Select(v => v.SKU)
            .ToListAsync();

        if (existingSkus.Count > 0)
        {
            return Fail($"SKU(s) already in use: {string.Join(", ", existingSkus)}.");
        }

        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            BasePrice = request.BasePrice,
            Material = request.Material,
            CategoryId = request.CategoryId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Variants = request.Variants.Select(v => new Variant
            {
                Name = v.Name,
                Price = v.Price,
                SKU = v.Sku,
                Quantity = v.Quantity,
                IsActive = true
            }).ToList()
        };

        _dbContext.Products.Add(product);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Fail("Could not create product - one or more SKUs may already be in use.");
        }

        var response = new ProductResponse
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            BasePrice = product.BasePrice,
            Material = product.Material,
            CategoryId = product.CategoryId,
            CategoryName = category.Name,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            Variants = product.Variants.Select(VariantMapper.ToResponse).ToList()
        };

        return new CreateProductResult(true, false, null, response);
    }

    public async Task<AddVariantResult> AddVariantAsync(Guid productId, CreateVariantRequest request)
    {
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == productId);

        if (product is null)
        {
            return new AddVariantResult(false, true, null, null);
        }

        if (request.Quantity < 0)
        {
            return new AddVariantResult(false, false, "Quantity must be zero or greater.", null);
        }

        var duplicateExists = await _dbContext.Variants.AnyAsync(v => v.SKU == request.Sku);

        if (duplicateExists)
        {
            return new AddVariantResult(false, false, $"SKU '{request.Sku}' is already in use.", null);
        }

        var variant = new Variant
        {
            ProductId = productId,
            Name = request.Name,
            Price = request.Price,
            SKU = request.Sku,
            Quantity = request.Quantity,
            IsActive = true
        };

        _dbContext.Variants.Add(variant);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return new AddVariantResult(false, false, $"Could not add variant - SKU '{request.Sku}' may already be in use.", null);
        }

        return new AddVariantResult(true, false, null, VariantMapper.ToResponse(variant));
    }

    public async Task<UpdateProductResult> UpdateAsync(Guid id, UpdateProductRequest request)
    {
        var product = await _dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null)
        {
            return new UpdateProductResult(false, true, false, null, null);
        }

        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            return FailUpdate("Name cannot be empty.");
        }

        if (request.Description is not null && string.IsNullOrWhiteSpace(request.Description))
        {
            return FailUpdate("Description cannot be empty.");
        }

        if (request.Material is not null && string.IsNullOrWhiteSpace(request.Material))
        {
            return FailUpdate("Material cannot be empty.");
        }

        if (request.BasePrice.HasValue && request.BasePrice.Value <= 0)
        {
            return FailUpdate("BasePrice must be a positive number.");
        }

        Category? newCategory = null;

        if (request.CategoryId.HasValue)
        {
            newCategory = await _dbContext.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId.Value);

            if (newCategory is null)
            {
                return new UpdateProductResult(false, false, true, null, null);
            }

            var isTerminal = await _categoryService.IsTerminalAsync(newCategory.Id);

            if (!isTerminal)
            {
                return FailUpdate("The assigned category must be terminal (have no child categories).");
            }
        }

        if (request.Name is not null)
        {
            product.Name = request.Name;
        }

        if (request.Description is not null)
        {
            product.Description = request.Description;
        }

        if (request.BasePrice.HasValue)
        {
            product.BasePrice = request.BasePrice.Value;
        }

        if (request.Material is not null)
        {
            product.Material = request.Material;
        }

        if (newCategory is not null)
        {
            product.Category = newCategory;
            product.CategoryId = newCategory.Id;
        }

        product.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        var response = new ProductResponse
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            BasePrice = product.BasePrice,
            Material = product.Material,
            CategoryId = product.CategoryId,
            CategoryName = product.Category.Name,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            Variants = product.Variants.Select(VariantMapper.ToResponse).ToList()
        };

        return new UpdateProductResult(true, false, false, null, response);
    }

    public async Task<List<PublicProductResponse>> SearchAsync(string? keyword, decimal? maxPrice)
    {
        var query = _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(p => p.Name.Contains(keyword));
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(p => p.BasePrice <= maxPrice.Value);
        }

        var products = await query.ToListAsync();

        return products.Select(MapToPublicResponse).ToList();
    }

    public async Task<PublicProductResponse?> GetByIdAsync(Guid id)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id);

        return product is null ? null : MapToPublicResponse(product);
    }

    private static PublicProductResponse MapToPublicResponse(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        BasePrice = p.BasePrice,
        Material = p.Material,
        CategoryId = p.CategoryId,
        CategoryName = p.Category.Name,
        Variants = p.Variants
            .Where(v => v.IsActive)
            .Select(v => new PublicVariantResponse
            {
                Id = v.Id,
                Name = v.Name,
                Price = v.Price,
                Sku = v.SKU,
                StockStatus = GetStockStatus(v.Quantity)
            })
            .ToList()
    };

    private static string GetStockStatus(int quantity)
    {
        if (quantity == 0)
        {
            return "OUT_OF_STOCK";
        }

        return quantity < 5 ? "LOW_STOCK" : "IN_STOCK";
    }

    private static CreateProductResult Fail(string message) => new(false, false, message, null);

    private static UpdateProductResult FailUpdate(string message) => new(false, false, false, message, null);
}
