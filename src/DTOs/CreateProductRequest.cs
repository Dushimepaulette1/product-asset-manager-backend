using System.ComponentModel.DataAnnotations;

namespace ProductAssetManager.Api.DTOs;

public record CreateProductRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string Description { get; init; } = string.Empty;

    public decimal BasePrice { get; init; }

    [Required]
    public string Material { get; init; } = string.Empty;

    public Guid CategoryId { get; init; }

    public List<CreateVariantRequest> Variants { get; init; } = new();
}
