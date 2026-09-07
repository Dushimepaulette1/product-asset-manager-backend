using System.ComponentModel.DataAnnotations;

namespace ProductAssetManager.Api.DTOs;

public record CreateVariantRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;

    public decimal? Price { get; init; }

    [Required]
    public string Sku { get; init; } = string.Empty;

    public int Quantity { get; init; }
}
