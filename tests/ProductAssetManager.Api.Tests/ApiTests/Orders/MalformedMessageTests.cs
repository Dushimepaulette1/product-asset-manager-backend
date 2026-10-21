using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Messaging;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

[TestFixture]
public class MalformedMessageTests : ApiTestBase
{
    [TestCase("this is not json")]
    [TestCase("{}")]
    public async Task When_MessageBodyIsUnusable_Should_DeadLetterItRatherThanLoopOrDropIt(string body)
    {
        var client = Factory.Services.GetRequiredKeyedService<ServiceBusClient>(ServiceBusQueues.Orders);
        var sender = Factory.Services.GetRequiredKeyedService<ServiceBusSender>(ServiceBusQueues.Orders);
        var queueName = Factory.Services.GetRequiredService<IConfiguration>()["ServiceBus:QueueName"]!;

        var messageId = Guid.NewGuid().ToString();
        await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromString(body))
        {
            MessageId = messageId,
            SessionId = $"MALFORMED-{messageId}"
        });

        await ResumeConsumerAsync();

        await using var deadLetterReceiver = client.CreateReceiver(
            queueName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        var deadLettered = await WaitForDeadLetterAsync(deadLetterReceiver, messageId, TimeSpan.FromSeconds(30));

        Assert.That(deadLettered, Is.Not.Null, "an unusable message can never succeed, so it must be dead-lettered for inspection");
        Assert.That(deadLettered!.DeadLetterReason, Is.EqualTo("InvalidMessageBody"));
    }

    private static async Task<ServiceBusReceivedMessage?> WaitForDeadLetterAsync(
        ServiceBusReceiver receiver,
        string messageId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var match = await PeekForMessageAsync(receiver, messageId);

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return null;
    }

    private static async Task<ServiceBusReceivedMessage?> PeekForMessageAsync(ServiceBusReceiver receiver, string messageId)
    {
        long fromSequenceNumber = 0;

        while (true)
        {
            var page = await receiver.PeekMessagesAsync(100, fromSequenceNumber);

            if (page.Count == 0)
            {
                return null;
            }

            var match = page.FirstOrDefault(message => message.MessageId == messageId);

            if (match is not null)
            {
                return match;
            }

            fromSequenceNumber = page[^1].SequenceNumber + 1;
        }
    }
}
