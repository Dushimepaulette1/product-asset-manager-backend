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
            Variants = product.Variants.Select(v => new VariantResponse
            {
                Id = v.Id,
                Name = v.Name,
                Price = v.Price,
                Sku = v.SKU,
                Quantity = v.Quantity,
                IsActive = v.IsActive
            }).ToList()
        };

        return new CreateProductResult(true, false, null, response);
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
}
