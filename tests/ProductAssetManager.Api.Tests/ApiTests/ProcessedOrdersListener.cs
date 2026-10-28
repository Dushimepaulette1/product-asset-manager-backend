using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ProductAssetManager.Api.Observability;

namespace ProductAssetManager.Api.Tests.ApiTests;

// Counts orders.processed measurements by outcome for one test host (filtered by its IMeterFactory).
public sealed class ProcessedOrdersListener : IDisposable
{
    private readonly ConcurrentQueue<string?> _outcomes = new();
    private readonly MeterListener _listener = new();

    public ProcessedOrdersListener(IMeterFactory meterFactory)
    {
        _listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Scope == meterFactory
                && instrument.Meter.Name == OrderMetrics.MeterName
                && instrument.Name == OrderMetrics.OrdersProcessedName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var outcome = tags.ToArray().FirstOrDefault(tag => tag.Key == "outcome").Value as string;

            for (var i = 0; i < value; i++)
            {
                _outcomes.Enqueue(outcome);
            }
        });
        _listener.Start();
    }

    public int Total => _outcomes.Count;

    public int Count(string outcome) => _outcomes.Count(recorded => recorded == outcome);

    public async Task WaitForTotalAsync(int total, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (Total < total && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
    }

    public void Dispose() => _listener.Dispose();
}
