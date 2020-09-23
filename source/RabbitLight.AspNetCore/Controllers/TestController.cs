using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RabbitLight.AspNetCore.Consumers.Context;
using RabbitLight.AspNetCore.Models;
using RabbitLight.Publisher;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RabbitLight.AspNetCore.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TestController : ControllerBase
    {
        private readonly ILogger<TestController> _logger;
        private readonly IPublisher _publisher;

        private static int Count = 0;
        private static DateTime Start = DateTime.Now;

        public TestController(ILogger<TestController> logger,
            TestContext busContext)
        {
            _logger = logger;
            _publisher = busContext.Publisher;
        }

        [HttpGet("send")]
        public async Task<string> SendMessage()
        {
            var body = new Test { Text = "Hello, World!" };
            await _publisher.PublishJson("test-exchange1", "test2", body);
            await _publisher.PublishJson("test-exchange2", "test1", body);
            return "Message published!";
        }

        [HttpGet("discard")]
        public async Task<string> SendDiscard()
        {
            var body = new Test { Text = "Hello, World!" };
            await _publisher.PublishJson("test-exchange1", "discard", body);
            return "Message published!";
        }

        [HttpGet("error")]
        public async Task<string> SendError()
        {
            var body = new Test { Text = "Hello, World!" };
            await _publisher.PublishJson("test-exchange1", "error", body);
            return "Message published!";
        }

        [HttpGet("reset")]
        public async Task<string> Reset()
        {
            _logger.LogInformation("Reseting Publisher...");
            Count = 0;
            Start = DateTime.Now;

            await _publisher.PublishJson("test-exchange1", "reset", "");

            return "Message published!";
        }

        [HttpGet("infinite")]
        public async Task<string> Infinite()
        {
            var body = new Test { Text = "Hello, World!" };
            while (true) await Publish(body);
        }

        private async Task Publish(Test body)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            Count++;

            // Start
            await _publisher.PublishJson("test-exchange1", "test1", body);
            // Finish

            stopWatch.Stop();
            var duration = DateTime.Now - Start;
            _logger.LogInformation("\r\n----------"
                + "\r\nPUBLISHING"
                + "\r\n----------"
                + $"\r\nElapsed: {stopWatch.Elapsed}"
                + $"\r\nCount: {Count}"
                + $"\r\nDuration: {duration}"
                + $"\r\nAvg: {duration / Count}"
                + $"\r\nMsg/s: {TimeSpan.FromSeconds(1) / (duration / Count)}\r\n");
        }
    }
}
