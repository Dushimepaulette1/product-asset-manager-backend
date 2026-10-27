using System.Diagnostics;

namespace ProductAssetManager.Api.Observability;

public static class OrderTelemetry
{
    public const string SourceName = "ProductAssetManager.Orders";

    public const string PublishSpanName = "OrderPlaced publish";
    public const string ProcessSpanName = "OrderPlaced process";
    public const string StockCheckSpanName = "order.stock_check";

    public const string OutcomeConfirmed = "confirmed";
    public const string OutcomeRejected = "rejected";
    public const string RejectionReasonInsufficientStock = "insufficient_stock";
    public const string OutcomeSkippedCancelled = "skipped_cancelled";

    public static readonly ActivitySource ActivitySource = new(SourceName);
}
