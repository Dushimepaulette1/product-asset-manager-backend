using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ProductAssetManager.Api.Observability;

public static class MessageTraceContext
{
    public static void Inject(Activity? activity, ServiceBusMessage message)
    {
        if (activity is null)
        {
            return;
        }

        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(activity.Context, Baggage.Current),
            message.ApplicationProperties,
            (properties, key, value) => properties[key] = value);
    }

    public static PropagationContext Extract(ServiceBusReceivedMessage message) =>
        Propagators.DefaultTextMapPropagator.Extract(
            default,
            message.ApplicationProperties,
            (properties, key) => properties.TryGetValue(key, out var value) && value is string text
                ? [text]
                : []);
}
