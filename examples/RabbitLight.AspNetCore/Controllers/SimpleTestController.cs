using Microsoft.AspNetCore.Mvc;
using RabbitLight.AspNetCore.Consumers.Context;
using RabbitLight.AspNetCore.Consumers.Routes;
using RabbitLight.AspNetCore.Models;
using RabbitLight.Publisher;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RabbitLight.AspNetCore.Controllers
{
    [ApiController]
    [Route("simple")]
    public class SimpleTestController : ControllerBase
    {
        private readonly IPublisher _publisher;

        public SimpleTestController(AspNetAppContext busContext)
        {
            _publisher = busContext.Publisher;
        }

        [HttpGet]
        public async Task<string> Test()
        {
            var body = new TestMessage { Content = "Test" };
            await _publisher.PublishJson(Exchanges.Test1, RoutingKeys.Test2, body);
            await _publisher.PublishJson(Exchanges.Test2, RoutingKeys.Test1, body);
            return "Message published!";
        }

        [HttpGet("batch")]
        public async Task<string> Batch(int size = 500)
        {
            var batch = new List<PublishBatch>();

            for (var i = 0; i < size; i++)
            {
                batch.Add(new PublishBatch(
                    Exchanges.Test1,
                    RoutingKeys.Test1,
                    MessageType.Json,
                    new TestMessage { Content = $"Batch: {i}" }
                ));
            }

            await _publisher.PublishBatch(batch);

            return "Messages published!";
        }

        [HttpGet("discard")]
        public async Task<string> Discard()
        {
            var body = new TestMessage { Content = "Discard test" };
            await _publisher.PublishJson(Exchanges.Test1, RoutingKeys.Discard, body);
            return "Message published!";
        }

        [HttpGet("error")]
        public async Task<string> Error()
        {
            var body = new TestMessage { Content = "Error test" };
            await _publisher.PublishJson(Exchanges.Test1, RoutingKeys.Error, body);
            return "Message published!";
        }

        [HttpGet("infinite")]
        public async Task Infinite()
        {
            var body = new TestMessage { Content = "Infinite test" };
            while (true)
            {
                await _publisher.PublishJson(
                    Exchanges.Test1,
                    RoutingKeys.Test1,
                    body
                );
            }
        }
    }
}
