namespace ProductAssetManager.Api.DTOs;

public record UpdateProductRequest
{
    public string? Name { get; init; }

    public string? Description { get; init; }

    public decimal? BasePrice { get; init; }

    public string? Material { get; init; }

    public Guid? CategoryId { get; init; }
}
