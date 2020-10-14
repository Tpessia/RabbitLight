using Microsoft.Extensions.Logging;
using RabbitLight.AspNetCore.Consumers.Routes;
using RabbitLight.AspNetCore.Models;
using RabbitLight.Consumer;
using System.IO;

namespace RabbitLight.AspNetCore.Consumers
{
    [Exchange(Exchanges.Test1)]
    public class IntegrityTestConsumer : ConsumerBase
    {
        public const string InputPath = @"C:\Git\RabbitLight\input.txt";
        public const string OutputPath = @"C:\Git\RabbitLight\output.txt";

        private readonly ILogger<IntegrityTestConsumer> _logger;
        private static readonly object _lock = new object();

        public IntegrityTestConsumer(ILogger<IntegrityTestConsumer> logger)
        {
            _logger = logger;
        }

        [Queue(Queues.Integrity, RoutingKeys.Integrity)]
        public void Integrity(MessageContext<TestMessage> context)
        {
            var msg = context.MessageFromJson();
            lock (_lock)
            {
                _logger.LogInformation("Line: " + msg.Content);
                File.AppendAllText(OutputPath, msg.Content + ",");
            }
        }
    }
}
