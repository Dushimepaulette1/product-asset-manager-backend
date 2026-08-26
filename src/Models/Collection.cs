namespace ProductAssetManager.Api.Models;

public class Collection
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ICollection<ProductCollection> ProductCollections { get; set; } = new List<ProductCollection>();
}
