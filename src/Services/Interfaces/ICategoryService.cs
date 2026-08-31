using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public interface ICategoryService
{
    Task<CreateCategoryResult> CreateAsync(CreateCategoryRequest request);

    Task<List<CategoryResponse>> GetAllAsync();

    Task<CategoryResponse?> GetByIdAsync(Guid id);
}
