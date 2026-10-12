using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ProductAssetManager.Api.Health;

public class ServiceBusHealthCheck : IHealthCheck
{
    private readonly ServiceBusSender _sender;

    public ServiceBusHealthCheck(ServiceBusSender sender)
    {
        _sender = sender;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var batch = await _sender.CreateMessageBatchAsync(cancellationToken);
            return HealthCheckResult.Healthy($"Queue '{_sender.EntityPath}' is reachable.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"Queue '{_sender.EntityPath}' is not reachable.", ex);
        }
    }
}
