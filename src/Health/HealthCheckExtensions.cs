using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.Messaging;

namespace ProductAssetManager.Api.Health;

public static class HealthCheckExtensions
{
    private const int DefaultTimeoutSeconds = 5;

    public static IServiceCollection AddApplicationHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration,
        bool consumerEnabled)
    {
        var timeoutSeconds = configuration.GetValue("HealthChecks:TimeoutSeconds", DefaultTimeoutSeconds);

        if (timeoutSeconds <= 0)
        {
            throw new InvalidOperationException("HealthChecks:TimeoutSeconds must be greater than zero.");
        }

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        var healthChecks = services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>("database", tags: [HealthCheckTags.Ready])
            .Add(ServiceBusQueueCheck("orders-queue", ServiceBusQueues.Orders));

        if (consumerEnabled)
        {
            healthChecks.Add(ServiceBusQueueCheck("stock-events-queue", ServiceBusQueues.StockEvents));
        }

        services.Configure<HealthCheckServiceOptions>(options =>
        {
            foreach (var registration in options.Registrations)
            {
                registration.Timeout = timeout;
            }
        });

        return services;
    }

    public static IEndpointRouteBuilder MapApplicationHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthCheckTags.Ready)
        });

        return endpoints;
    }

    private static HealthCheckRegistration ServiceBusQueueCheck(string name, string senderKey) =>
        new(
            name,
            sp => new ServiceBusHealthCheck(sp.GetRequiredKeyedService<ServiceBusSender>(senderKey)),
            failureStatus: null,
            tags: [HealthCheckTags.Ready]);
}
