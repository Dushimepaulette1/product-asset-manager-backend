using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Observability;

[TestFixture]
public class OrderMetricsTests : ApiTestBase
{
    private const string SkippedTemplate = "Order {OrderId} was cancelled before processing; skipping without changing stock";

    // NUnit reuses one fixture instance for every test, so these are created fresh in SetUp.
    private ConcurrentQueue<IReadOnlyDictionary<string, object?>> _processed = null!;
    private ConcurrentQueue<IReadOnlyDictionary<string, object?>> _durations = null!;
    private MeterListener _listener = null!;

    [SetUp]
    public void ListenToOrderMetrics()
    {
        var meterFactory = Factory.Services.GetRequiredService<IMeterFactory>();
        _processed = new ConcurrentQueue<IReadOnlyDictionary<string, object?>>();
        _durations = new ConcurrentQueue<IReadOnlyDictionary<string, object?>>();

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Scope == meterFactory
                && instrument.Meter.Name == OrderMetrics.MeterName
                && instrument.Name is OrderMetrics.OrdersProcessedName or OrderMetrics.ProcessingDurationName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            for (var i = 0; i < value; i++)
            {
                _processed.Enqueue(tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value));
            }
        });
        _listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
            _durations.Enqueue(tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
        _listener.Start();
    }

    [TearDown]
    public void StopListening() => _listener.Dispose();

    [Test]
    public async Task When_OnePurchaseSucceedsAndOneLacksStock_Should_CountOneConfirmedAndOneRejectedForInsufficientStock()
    {
        var variantId = await SeedVariantAsync("ORDER-METRICS-SKU", 1);
        await SignInAsShopperAndStartConsumerAsync();

        await PlaceOrderAndWaitForStatusAsync(variantId, 1, OrderStatus.Confirmed);
        await PlaceOrderAndWaitForStatusAsync(variantId, 1, OrderStatus.Rejected);

        await WaitUntilAsync(() => _processed.Count >= 2);

        var recorded = _processed.ToList();
        Assert.That(recorded, Has.Count.EqualTo(2));

        var confirmed = recorded.Where(tags => Equals(tags["outcome"], OrderTelemetry.OutcomeConfirmed)).ToList();
        var rejected = recorded.Where(tags => Equals(tags["outcome"], OrderTelemetry.OutcomeRejected)).ToList();

        Assert.That(confirmed, Has.Count.EqualTo(1));
        Assert.That(confirmed[0].ContainsKey("rejection_reason"), Is.False);
        Assert.That(rejected, Has.Count.EqualTo(1));
        Assert.That(rejected[0]["rejection_reason"], Is.EqualTo(OrderTelemetry.RejectionReasonInsufficientStock));
    }

    [Test]
    public void When_ACancellationIsRecorded_Should_CountOneCancelledWithoutAReasonOrAProcessingDuration()
    {
        Factory.Services.GetRequiredService<OrderMetrics>().OrderCancelled();

        var recorded = _processed.ToList();
        Assert.That(recorded, Has.Count.EqualTo(1));
        Assert.That(recorded[0]["outcome"], Is.EqualTo(OrderTelemetry.OutcomeCancelled));
        Assert.That(recorded[0].ContainsKey("rejection_reason"), Is.False);
        Assert.That(_durations, Is.Empty, "a cancellation's timing is the shopper's choice, so it stays out of the processing-duration histogram");
    }

    [Test]
    public async Task When_TheConsumerSkipsACancelledOrder_Should_NotCountItAgain()
    {
        var variantId = await SeedVariantAsync("ORDER-METRICS-SKU", 5);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());

        var cancelledOrderId = await PlacePendingOrderAsync(variantId, 1);
        var laterOrderId = await PlacePendingOrderAsync(variantId, 1);
        await SetOrderStatusAsync(cancelledOrderId, OrderStatus.Cancelled);

        await ResumeConsumerAsync();

        var later = await PollUntilResolvedAsync(laterOrderId, TimeSpan.FromSeconds(30));
        Assert.That(later.Status, Is.EqualTo(OrderStatus.Confirmed));
        Assert.That(HasLog(SkippedTemplate, cancelledOrderId), Is.True, "the consumer must have handled the cancelled order's message before the later one");

        await WaitUntilAsync(() => _processed.Count >= 1);

        var recorded = _processed.ToList();
        Assert.That(recorded, Has.Count.EqualTo(1), "only the later order's confirmation counts; the cancellation is counted by whoever cancels, never by the consumer's skip");
        Assert.That(recorded[0]["outcome"], Is.EqualTo(OrderTelemetry.OutcomeConfirmed));
        Assert.That(_durations, Has.Count.EqualTo(1));
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
