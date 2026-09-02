using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public record AddVariantResult(bool Succeeded, bool ProductNotFound, string? ValidationError, VariantResponse? Variant);
