using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ProductAssetManager.Api.Observability;

public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddApplicationObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddConsoleExporter())
            .WithMetrics(metrics => metrics.AddConsoleExporter())
            .WithLogging(
                logging => logging.AddConsoleExporter(),
                options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                });

        return builder;
    }
}
