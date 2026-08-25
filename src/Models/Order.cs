namespace ProductAssetManager.Api.Models;

public class Order
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public Guid VariantId { get; set; }

    public Variant Variant { get; set; } = null!;

    public int QuantityPurchased { get; set; }

    public decimal UnitPriceAtPurchase { get; set; }

    public DateTime OrderDate { get; set; }
}
