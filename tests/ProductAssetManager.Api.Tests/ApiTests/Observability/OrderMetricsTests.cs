using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Observability;

[TestFixture]
public class OrderMetricsTests : ApiTestBase
{
    [Test]
    public async Task When_OnePurchaseSucceedsAndOneLacksStock_Should_CountOneConfirmedAndOneRejectedForInsufficientStock()
    {
        var measurements = new ConcurrentQueue<IReadOnlyDictionary<string, object?>>();
        var meterFactory = Factory.Services.GetRequiredService<IMeterFactory>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Scope == meterFactory
                && instrument.Meter.Name == OrderMetrics.MeterName
                && instrument.Name == OrderMetrics.OrdersProcessedName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            for (var i = 0; i < value; i++)
            {
                measurements.Enqueue(tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value));
            }
        });
        listener.Start();

        var variantId = await SeedVariantAsync("ORDER-METRICS-SKU", 1);
        await SignInAsShopperAndStartConsumerAsync();

        await PlaceOrderAndWaitForStatusAsync(variantId, 1, OrderStatus.Confirmed);
        await PlaceOrderAndWaitForStatusAsync(variantId, 1, OrderStatus.Rejected);

        await WaitUntilAsync(() => measurements.Count >= 2);

        var recorded = measurements.ToList();
        Assert.That(recorded, Has.Count.EqualTo(2));

        var confirmed = recorded.Where(tags => Equals(tags["outcome"], OrderTelemetry.OutcomeConfirmed)).ToList();
        var rejected = recorded.Where(tags => Equals(tags["outcome"], OrderTelemetry.OutcomeRejected)).ToList();

        Assert.That(confirmed, Has.Count.EqualTo(1));
        Assert.That(confirmed[0].ContainsKey("rejection_reason"), Is.False);
        Assert.That(rejected, Has.Count.EqualTo(1));
        Assert.That(rejected[0]["rejection_reason"], Is.EqualTo(OrderTelemetry.RejectionReasonInsufficientStock));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
    }
}
