namespace ProductAssetManager.Api.DTOs;

public record CategoryResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public Guid? ParentCategoryId { get; init; }

    public string? ParentCategoryName { get; init; }

    public List<CategoryResponse> Children { get; init; } = new();
}
