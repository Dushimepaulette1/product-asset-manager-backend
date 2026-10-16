using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Observability;

[TestFixture]
public class StructuredLogTests : ApiTestBase
{
    private const string ConfirmedTemplate = "Order {OrderId} confirmed for SKU {Sku}, {Remaining} unit(s) remaining";

    [Test]
    public async Task When_ConsumerLogsAConfirmedOrder_Should_KeepTheTemplateAndCarryTheTraceAndSpanIds()
    {
        var variantId = await SeedVariantAsync("STRUCTURED-LOG-SKU", 5);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());
        await ResumeConsumerAsync();

        var response = await Client.PostAsJsonAsync("/api/orders", new CreateOrderRequest { VariantId = variantId, Quantity = 1 });
        var accepted = await response.Content.ReadFromJsonAsync<OrderAcceptedResponse>();
        var resolved = await PollUntilResolvedAsync(accepted!.OrderId, TimeSpan.FromSeconds(30));
        Assert.That(resolved.Status, Is.EqualTo(OrderStatus.Confirmed));

        var processSpan = await WaitForSpanAsync(span =>
            span.DisplayName == OrderTelemetry.ProcessSpanName
            && span.GetTagItem("order.id") as string == accepted.OrderId.ToString());
        var purchaseRequestSpan = Factory.ExportedActivities.Snapshot().Single(span =>
            span.Kind == ActivityKind.Server && span.DisplayName == "POST api/orders");

        var confirmedLog = Factory.CapturedLogs.Snapshot().SingleOrDefault(log =>
            log.Body == ConfirmedTemplate && Equals(log.Attributes.GetValueOrDefault("OrderId"), accepted.OrderId));

        Assert.That(confirmedLog, Is.Not.Null, "the consumer should log the confirmation with its message template");
        Assert.That(confirmedLog!.TraceId, Is.EqualTo(purchaseRequestSpan.TraceId), "the log belongs to the purchase's trace");
        Assert.That(confirmedLog.SpanId, Is.EqualTo(processSpan!.SpanId), "the log was written inside the processing span");
        Assert.That(confirmedLog.Attributes["Sku"], Is.EqualTo("STRUCTURED-LOG-SKU"));
    }
}
