using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public record CreateProductResult(bool Succeeded, bool CategoryNotFound, string? ValidationError, ProductResponse? Product);

public record UpdateProductResult(bool Succeeded, bool ProductNotFound, bool CategoryNotFound, string? ValidationError, ProductResponse? Product);
