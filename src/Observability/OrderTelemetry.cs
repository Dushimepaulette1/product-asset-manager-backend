using System.Diagnostics;

namespace ProductAssetManager.Api.Observability;

public static class OrderTelemetry
{
    public const string SourceName = "ProductAssetManager.Orders";

    public const string PublishSpanName = "OrderPlaced publish";
    public const string ProcessSpanName = "OrderPlaced process";

    public static readonly ActivitySource ActivitySource = new(SourceName);
}
