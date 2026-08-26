namespace ProductAssetManager.Api.Models;

public class Variant
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public decimal? Price { get; set; }

    public string SKU { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
