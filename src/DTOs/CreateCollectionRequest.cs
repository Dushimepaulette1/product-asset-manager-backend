using System.ComponentModel.DataAnnotations;

namespace ProductAssetManager.Api.DTOs;

public record CreateCollectionRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string Description { get; init; } = string.Empty;
}
