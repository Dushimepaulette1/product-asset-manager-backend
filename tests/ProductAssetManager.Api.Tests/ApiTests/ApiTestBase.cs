using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Messaging;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Services;

namespace ProductAssetManager.Api.Tests.ApiTests;

public abstract class ApiTestBase
{
    protected CustomWebApplicationFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    [SetUp]
    public async Task ApiTestBaseSetUp()
    {
        await TestDatabase.ResetAsync(TestDatabase.ApiTestsConnectionString);
        Factory = new CustomWebApplicationFactory();
        Client = Factory.CreateClient();
    }

    [TearDown]
    public async Task ApiTestBaseTearDown()
    {
        Client.Dispose();
        await Factory.DisposeAsync();

        await TestDatabase.DeleteAsync(TestDatabase.ApiTestsConnectionString);
    }

    protected async Task<string> CreateAdminTokenAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        if (!await roleManager.RoleExistsAsync(IdentitySeeder.AdminRole))
        {
            await roleManager.CreateAsync(new IdentityRole(IdentitySeeder.AdminRole));
        }

        var email = $"admin-test-{Guid.NewGuid()}@example.com";
        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Test Admin"
        };

        await userManager.CreateAsync(admin, "AdminPassw0rd!");
        await userManager.AddToRoleAsync(admin, IdentitySeeder.AdminRole);

        return tokenService.GenerateToken(admin, new List<string> { IdentitySeeder.AdminRole });
    }

    protected async Task<string> CreateUserTokenAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        if (!await roleManager.RoleExistsAsync(IdentitySeeder.UserRole))
        {
            await roleManager.CreateAsync(new IdentityRole(IdentitySeeder.UserRole));
        }

        var email = $"user-test-{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Test User"
        };

        await userManager.CreateAsync(user, "UserPassw0rd!");
        await userManager.AddToRoleAsync(user, IdentitySeeder.UserRole);

        return tokenService.GenerateToken(user, new List<string> { IdentitySeeder.UserRole });
    }

    protected async Task<int> GetVariantQuantityAsync(string sku)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Variants
            .Where(v => v.SKU == sku)
            .Select(v => v.Quantity)
            .FirstAsync();
    }

    protected async Task<OrderStatus> GetOrderStatusAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Orders
            .Where(o => o.Id == orderId)
            .Select(o => o.Status)
            .FirstAsync();
    }

    protected async Task SetOrderStatusAsync(Guid orderId, OrderStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = await dbContext.Orders.FirstAsync(o => o.Id == orderId);
        order.Status = status;

        await dbContext.SaveChangesAsync();
    }

    protected async Task<Guid> PlacePendingOrderAsync(Guid variantId, int quantity)
    {
        var response = await Client.PostAsJsonAsync("/api/orders", new CreateOrderRequest { VariantId = variantId, Quantity = quantity });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var accepted = await response.Content.ReadFromJsonAsync<OrderAcceptedResponse>(JsonTestOptions.Default);
        return accepted!.OrderId;
    }

    protected Task<HttpResponseMessage> CancelOrderAsync(Guid orderId, HttpClient? client = null) =>
        (client ?? Client).PostAsync($"/api/orders/{orderId}/cancel", null);

    protected ProcessedOrdersListener ListenToProcessedOrders() =>
        new(Factory.Services.GetRequiredService<IMeterFactory>());

    protected static bool IsSavingOrderAs(DbContext context, Guid orderId, OrderStatus status) =>
        context.ChangeTracker.Entries<Order>().Any(entry =>
            entry.Entity.Id == orderId && entry.State == EntityState.Modified && entry.Entity.Status == status);

    protected List<Activity> ProcessSpansFor(Guid orderId) =>
        Factory.ExportedActivities.Snapshot()
            .Where(span => span.DisplayName == OrderTelemetry.ProcessSpanName && span.GetTagItem("order.id") as string == orderId.ToString())
            .ToList();

    protected bool HasLog(string template, Guid orderId) =>
        Factory.CapturedLogs.Snapshot().Any(log =>
            log.Body == template && Equals(log.Attributes.GetValueOrDefault("OrderId"), orderId));

    protected async Task<ServiceBusReceivedMessage?> FindStockDecrementedAsync(Guid orderId, TimeSpan wait)
    {
        var client = Factory.Services.GetRequiredKeyedService<ServiceBusClient>(ServiceBusQueues.StockEvents);
        var queueName = Factory.Services.GetRequiredService<IConfiguration>()["ServiceBus:StockEventsQueueName"]!;
        var messageId = $"{orderId}-stock-decremented";
        var deadline = DateTime.UtcNow + wait;

        await using var receiver = client.CreateReceiver(queueName);

        while (true)
        {
            long fromSequenceNumber = 0;
            IReadOnlyList<ServiceBusReceivedMessage> page;

            do
            {
                page = await receiver.PeekMessagesAsync(100, fromSequenceNumber);

                var match = page.FirstOrDefault(message => message.MessageId == messageId);
                if (match is not null)
                {
                    return match;
                }

                fromSequenceNumber = page.Count > 0 ? page[^1].SequenceNumber + 1 : fromSequenceNumber;
            }
            while (page.Count > 0);

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    protected async Task<Guid> SeedVariantAsync(string sku, int quantity)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = new Category { Id = Guid.NewGuid(), Name = $"Category for {sku}" };
        var variant = new Variant { Id = Guid.NewGuid(), Name = "Standard", SKU = sku, Quantity = quantity };

        dbContext.Categories.Add(category);
        dbContext.Products.Add(new Product
        {
            Id = Guid.NewGuid(),
            Name = $"Product for {sku}",
            Description = "Seeded directly for a test",
            BasePrice = 10.00m,
            Material = "Test",
            CategoryId = category.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Variants = { variant }
        });

        await dbContext.SaveChangesAsync();

        return variant.Id;
    }

    protected async Task<Activity?> WaitForSpanAsync(Func<Activity, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTime.UtcNow < deadline)
        {
            var match = Factory.ExportedActivities.Snapshot().FirstOrDefault(predicate);

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(200);
        }

        return null;
    }

    protected async Task SignInAsShopperAndStartConsumerAsync()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());
        await ResumeConsumerAsync();
    }

    protected async Task<Guid> PlaceOrderAndWaitForStatusAsync(Guid variantId, int quantity, OrderStatus expectedStatus)
    {
        var response = await Client.PostAsJsonAsync("/api/orders", new CreateOrderRequest { VariantId = variantId, Quantity = quantity });
        var accepted = await response.Content.ReadFromJsonAsync<OrderAcceptedResponse>();

        var resolved = await PollUntilResolvedAsync(accepted!.OrderId, TimeSpan.FromSeconds(30));
        Assert.That(resolved.Status, Is.EqualTo(expectedStatus));

        return accepted.OrderId;
    }

    protected Task<Activity?> WaitForOrderSpanAsync(string spanName, Guid orderId) =>
        WaitForSpanAsync(span => span.DisplayName == spanName && span.GetTagItem("order.id") as string == orderId.ToString());

    protected Activity PurchaseRequestSpan() =>
        Factory.ExportedActivities.Snapshot().Single(span =>
            span.Kind == ActivityKind.Server && span.DisplayName == "POST api/orders");

    protected Task ResumeConsumerAsync()
    {
        var consumer = Factory.Services.GetRequiredService<OrderConsumer>();
        return consumer.ResumeProcessingAsync();
    }

    protected async Task<OrderResponse> PollUntilResolvedAsync(Guid orderId, TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(200);

        while (true)
        {
            var response = await Client.GetAsync($"/api/orders/{orderId}");
            var order = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonTestOptions.Default);

            if (order!.Status != OrderStatus.Pending || DateTime.UtcNow >= deadline)
            {
                return order;
            }

            await Task.Delay(interval);
        }
    }
}
