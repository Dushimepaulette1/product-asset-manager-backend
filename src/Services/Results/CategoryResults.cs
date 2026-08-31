using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public record CreateCategoryResult(bool Succeeded, bool ParentNotFound, CategoryResponse? Category);
