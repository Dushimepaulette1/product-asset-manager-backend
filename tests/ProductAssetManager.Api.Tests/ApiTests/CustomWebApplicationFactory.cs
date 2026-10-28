using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using ProductAssetManager.Api.Data;

namespace ProductAssetManager.Api.Tests.ApiTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public ExportedActivities ExportedActivities { get; } = new();

    public CapturedLogs CapturedLogs { get; } = new();

    public BeforeSaveHook BeforeSaveHook { get; } = new();

    private readonly IReadOnlyDictionary<string, string?> _settings;

    public CustomWebApplicationFactory(IReadOnlyDictionary<string, string?>? settings = null)
    {
        _settings = settings ?? new Dictionary<string, string?>();

        Environment.SetEnvironmentVariable("Jwt__Issuer", "TestIssuer");
        Environment.SetEnvironmentVariable("Jwt__Audience", "TestAudience");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "this-is-a-test-only-signing-key-not-used-anywhere-real");
        Environment.SetEnvironmentVariable("Jwt__ExpiryMinutes", "60");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddUserSecrets<Program>(optional: true);
            var userSecretsSource = config.Sources[^1];
            config.Sources.RemoveAt(config.Sources.Count - 1);
            config.Sources.Insert(0, userSecretsSource);

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceBus:AutoStartConsumer"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(TestDatabase.ApiTestsConnectionString).AddInterceptors(BeforeSaveHook));

            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(ExportedActivities));
            services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddProcessor(CapturedLogs));
        });
    }
}
