using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ProductAssetManager.Api.Observability;

public static class ObservabilityExtensions
{
    private const string DefaultServiceName = "product-asset-manager";

    public static WebApplicationBuilder AddApplicationObservability(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? DefaultServiceName;
        var serviceVersion = configuration["Build:CommitSha"] ?? "unknown";
        var deploymentEnvironment = configuration["OpenTelemetry:DeploymentEnvironment"]
            ?? builder.Environment.EnvironmentName.ToLowerInvariant();

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment", deploymentEnvironment)]))
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
