using RabbitLight.ConsoleApp.Consumers.Routes;
using RabbitLight.Consumer;
using System;

namespace RabbitLight.ConsoleApp.Consumers
{
    [Exchange(Exchanges.TestExchange)]
    public class SimpleConsumer : ConsumerBase
    {
        [Queue(Queues.TestQueue)]
        public void Test(MessageContext<string> context)
        {
            var msg = context.MessageAsString();
            Console.WriteLine(msg);
        }
    }
}
