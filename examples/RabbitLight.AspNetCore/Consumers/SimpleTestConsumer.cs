using Microsoft.Extensions.Logging;
using RabbitLight.AspNetCore.Consumers.Routes;
using RabbitLight.AspNetCore.Models;
using RabbitLight.Consumer;
using RabbitLight.Exceptions;
using System.Threading.Tasks;

namespace RabbitLight.AspNetCore.Consumers
{
    [Exchange(Exchanges.Test1)]
    [Exchange(Exchanges.Test2)]
    public class SimpleTestConsumer : ConsumerBase
    {
        private readonly ILogger<SimpleTestConsumer> _logger;

        public SimpleTestConsumer(ILogger<SimpleTestConsumer> logger)
        {
            _logger = logger;
        }

        [Queue(Queues.Test1, RoutingKeys.Test1)]
        [Queue(Queues.Test2, RoutingKeys.Test2)]
        public async Task Test(MessageContext<TestMessage> context)
        {
            var msg = context.MessageFromJson();
            _logger.LogInformation($"Message received: {msg.Content}");
            await Task.Delay(500);
        }

        [Queue(Queues.Discard, RoutingKeys.Discard)]
        public void Discard(MessageContext<TestMessage> context)
        {
            throw new DiscardMessageException("Discard Test");
        }

        [Queue(Queues.Error, RoutingKeys.Error)]
        public void Error(MessageContext<TestMessage> context)
        {
            throw new SerializationException("Error Test");
        }
    }
}
