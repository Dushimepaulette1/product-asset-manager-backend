using System.ComponentModel.DataAnnotations;

namespace ProductAssetManager.Api.DTOs;

public record CreateCategoryRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;

    public Guid? ParentCategoryId { get; init; }
}
