namespace ProductAssetManager.Api.Models;

public class Product
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal BasePrice { get; set; }

    public string Material { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Variant> Variants { get; set; } = new List<Variant>();

    public ICollection<ProductCollection> ProductCollections { get; set; } = new List<ProductCollection>();
}
