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
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsHealthCheck(context.Request.Path))
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddConsoleExporter())
            .WithLogging(
                logging => logging.AddConsoleExporter(),
                options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                });

        return builder;
    }

    public static WebApplication UseApplicationObservability(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!IsHealthCheck(context.Request.Path))
            {
                await next(context);
                return;
            }

            using (SuppressInstrumentationScope.Begin())
            {
                await next(context);
            }
        });

        return app;
    }

    private static bool IsHealthCheck(PathString path) => path.StartsWithSegments("/health");
}
