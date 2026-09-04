using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public record AddVariantResult(bool Succeeded, bool ProductNotFound, string? ValidationError, VariantResponse? Variant);

public record UpdateStockResult(bool Succeeded, bool VariantNotFound, string? ValidationError, VariantResponse? Variant);
