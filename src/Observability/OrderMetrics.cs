using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ProductAssetManager.Api.Observability;

public class OrderMetrics
{
    public const string MeterName = "ProductAssetManager.Orders";

    public const string OrdersPlacedName = "orders.placed";
    public const string OrdersProcessedName = "orders.processed";
    public const string ProcessingDurationName = "orders.processing.duration";

    private readonly Counter<long> _ordersPlaced;
    private readonly Counter<long> _ordersProcessed;
    private readonly Histogram<double> _processingDuration;

    public OrderMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _ordersPlaced = meter.CreateCounter<long>(
            OrdersPlacedName,
            unit: "{order}",
            description: "Orders accepted by the API and published to the queue.");

        _ordersProcessed = meter.CreateCounter<long>(
            OrdersProcessedName,
            unit: "{order}",
            description: "Orders the consumer moved from Pending to a final status.");

        _processingDuration = meter.CreateHistogram(
            ProcessingDurationName,
            unit: "ms",
            description: "Time from an order being saved as Pending to its final status.",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = [10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 30000, 60000, 120000, 300000]
            });

        _ordersPlaced.Add(0);
        _ordersProcessed.Add(0, new TagList { { "outcome", OrderTelemetry.OutcomeConfirmed } });
        _ordersProcessed.Add(0, new TagList
        {
            { "outcome", OrderTelemetry.OutcomeRejected },
            { "rejection_reason", OrderTelemetry.RejectionReasonInsufficientStock }
        });
    }

    public void OrderPlaced() => _ordersPlaced.Add(1);

    public void OrderProcessed(string outcome, string? rejectionReason, TimeSpan timeSincePlaced)
    {
        var tags = new TagList { { "outcome", outcome } };

        if (rejectionReason is not null)
        {
            tags.Add("rejection_reason", rejectionReason);
        }

        _ordersProcessed.Add(1, tags);
        _processingDuration.Record(timeSincePlaced.TotalMilliseconds, new TagList { { "outcome", outcome } });
    }
}
