using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Observability;

[TestFixture]
public class PendingOrdersMetricsTests : ApiTestBase
{
    [Test]
    public async Task When_AnOrderIsCancelled_Should_LeaveItOutOfThePendingGauges()
    {
        var variantId = await SeedVariantAsync("PENDING-GAUGE-SKU", 5);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());

        var cancelledOrderId = await PlacePendingOrderAsync(variantId, 1);
        await PlacePendingOrderAsync(variantId, 1);

        await SetOrderStatusAsync(cancelledOrderId, OrderStatus.Cancelled);
        await BackdateOrderAsync(cancelledOrderId, TimeSpan.FromHours(1));

        var (pending, oldestPendingAgeSeconds) = ReadPendingGauges();

        Assert.That(pending, Is.EqualTo(1), "only the order still waiting for the consumer is Pending");
        Assert.That(oldestPendingAgeSeconds, Is.LessThan(TimeSpan.FromMinutes(10).TotalSeconds), "the hour-old cancelled order must not count as the oldest Pending order");
    }

    private (long Pending, double OldestPendingAgeSeconds) ReadPendingGauges()
    {
        var meterFactory = Factory.Services.GetRequiredService<IMeterFactory>();
        long? pending = null;
        double? oldestAge = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Scope == meterFactory
                && instrument.Meter.Name == OrderMetrics.MeterName
                && instrument.Name is PendingOrdersMetrics.PendingName or PendingOrdersMetrics.OldestPendingAgeName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => pending = value);
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => oldestAge = value);
        listener.Start();

        listener.RecordObservableInstruments();

        Assert.That(pending, Is.Not.Null, "the orders.pending gauge must report a value");
        Assert.That(oldestAge, Is.Not.Null, "the orders.pending.oldest_age gauge must report a value");

        return (pending!.Value, oldestAge!.Value);
    }

    private async Task BackdateOrderAsync(Guid orderId, TimeSpan age)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var orderDate = DateTime.UtcNow - age;
        await dbContext.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(setters => setters.SetProperty(o => o.OrderDate, orderDate));
    }
}
