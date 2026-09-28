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

        await deadLetterReceiver.CompleteMessageAsync(deadLettered);
    }

    private static async Task<ServiceBusReceivedMessage?> WaitForDeadLetterAsync(
        ServiceBusReceiver receiver,
        string messageId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var messages = await receiver.ReceiveMessagesAsync(20, TimeSpan.FromSeconds(3));
            ServiceBusReceivedMessage? match = null;

            foreach (var message in messages)
            {
                if (message.MessageId == messageId)
                {
                    match = message;
                }
                else
                {
                    await receiver.AbandonMessageAsync(message);
                }
            }

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
