using System.Diagnostics.Metrics;
using OpenTelemetry;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Observability;

public class PendingOrdersMetrics
{
    public const string PendingName = "orders.pending";
    public const string OldestPendingAgeName = "orders.pending.oldest_age";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingOrdersMetrics> _logger;

    public PendingOrdersMetrics(IMeterFactory meterFactory, IServiceScopeFactory scopeFactory, ILogger<PendingOrdersMetrics> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var meter = meterFactory.Create(OrderMetrics.MeterName);

        meter.CreateObservableGauge(
            PendingName,
            ObservePendingCount,
            unit: "{order}",
            description: "Orders currently waiting for the consumer.");

        meter.CreateObservableGauge(
            OldestPendingAgeName,
            ObserveOldestPendingAge,
            unit: "s",
            description: "How long the oldest Pending order has been waiting; 0 when none are pending.");
    }

    private IEnumerable<Measurement<long>> ObservePendingCount()
    {
        if (!TryQuery(dbContext => dbContext.Orders.LongCount(o => o.Status == OrderStatus.Pending), out var count))
        {
            return [];
        }

        return [new Measurement<long>(count)];
    }

    private IEnumerable<Measurement<double>> ObserveOldestPendingAge()
    {
        var found = TryQuery(
            dbContext => dbContext.Orders
                .Where(o => o.Status == OrderStatus.Pending)
                .Select(o => (DateTime?)o.OrderDate)
                .Min(),
            out var oldestOrderDate);

        if (!found)
        {
            return [];
        }

        var ageSeconds = oldestOrderDate is null ? 0 : Math.Max((DateTime.UtcNow - oldestOrderDate.Value).TotalSeconds, 0);
        return [new Measurement<double>(ageSeconds)];
    }

    private bool TryQuery<T>(Func<ApplicationDbContext, T> query, out T result)
    {
        using var suppressTracing = SuppressInstrumentationScope.Begin();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            result = query(dbContext);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read pending orders for the {MetricName} gauges", PendingName);
            result = default!;
            return false;
        }
    }
}
