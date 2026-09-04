using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public class CollectionService : ICollectionService
{
    private readonly ApplicationDbContext _dbContext;

    public CollectionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CollectionResponse> CreateAsync(CreateCollectionRequest request)
    {
        var collection = new Collection
        {
            Name = request.Name,
            Description = request.Description
        };

        _dbContext.Collections.Add(collection);
        await _dbContext.SaveChangesAsync();

        return new CollectionResponse
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            Products = new List<PublicProductResponse>()
        };
    }

    public async Task<CollectionResponse?> GetByIdAsync(Guid id)
    {
        var collection = await _dbContext.Collections
            .AsNoTracking()
            .Include(c => c.ProductCollections)
                .ThenInclude(pc => pc.Product)
                    .ThenInclude(p => p.Category)
            .Include(c => c.ProductCollections)
                .ThenInclude(pc => pc.Product)
                    .ThenInclude(p => p.Variants)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (collection is null)
        {
            return null;
        }

        return new CollectionResponse
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            Products = collection.ProductCollections
                .Select(pc => ProductMapper.ToPublicResponse(pc.Product))
                .ToList()
        };
    }

    public async Task<AddProductToCollectionResult> AddProductAsync(Guid collectionId, Guid productId)
    {
        var collectionExists = await _dbContext.Collections.AnyAsync(c => c.Id == collectionId);

        if (!collectionExists)
        {
            return new AddProductToCollectionResult(false, true, false, false, null);
        }

        var productExists = await _dbContext.Products.AnyAsync(p => p.Id == productId);

        if (!productExists)
        {
            return new AddProductToCollectionResult(false, false, true, false, null);
        }

        var alreadyMember = await _dbContext.ProductCollections
            .AnyAsync(pc => pc.CollectionId == collectionId && pc.ProductId == productId);

        if (!alreadyMember)
        {
            _dbContext.ProductCollections.Add(new ProductCollection
            {
                CollectionId = collectionId,
                ProductId = productId
            });

            await _dbContext.SaveChangesAsync();
        }

        var collectionResponse = await GetByIdAsync(collectionId);

        return new AddProductToCollectionResult(true, false, false, alreadyMember, collectionResponse);
    }

    public async Task<RemoveProductFromCollectionResult> RemoveProductAsync(Guid collectionId, Guid productId)
    {
        var collectionExists = await _dbContext.Collections.AnyAsync(c => c.Id == collectionId);

        if (!collectionExists)
        {
            return new RemoveProductFromCollectionResult(false, true, false);
        }

        var productExists = await _dbContext.Products.AnyAsync(p => p.Id == productId);

        if (!productExists)
        {
            return new RemoveProductFromCollectionResult(false, false, true);
        }

        var membership = await _dbContext.ProductCollections
            .FirstOrDefaultAsync(pc => pc.CollectionId == collectionId && pc.ProductId == productId);

        if (membership is not null)
        {
            _dbContext.ProductCollections.Remove(membership);
            await _dbContext.SaveChangesAsync();
        }

        return new RemoveProductFromCollectionResult(true, false, false);
    }
}
