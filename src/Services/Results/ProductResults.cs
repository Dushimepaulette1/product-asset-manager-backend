using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public record CreateProductResult(bool Succeeded, bool CategoryNotFound, string? ValidationError, ProductResponse? Product);
