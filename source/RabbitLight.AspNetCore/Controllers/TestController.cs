using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RabbitLight.AspNetCore.Consumers.Context;
using RabbitLight.AspNetCore.Models;
using RabbitLight.Publisher;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        [HttpGet]
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
            await _publisher.PublishJson("test-exchange1", "test3", body);
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

            while (true)
                await Publish(body);
        }

        [HttpGet("infinite2")]
        public void Infinite2()
        {
            var body = new Test { Text = "Hello, World!" };

            While(new ParallelOptions { MaxDegreeOfParallelism = 10 }, () => true, x => Publish(body).Wait());
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

        public static void While(ParallelOptions parallelOptions, Func<bool> condition, Action<ParallelLoopState> body)
        {
            Parallel.ForEach(new InfinitePartitioner(), parallelOptions,
                (ignored, loopState) =>
                {
                    if (condition()) body(loopState);
                    else loopState.Stop();
                });
        }
    }

    public class InfinitePartitioner : Partitioner<bool>
    {
        public override IList<IEnumerator<bool>> GetPartitions(int partitionCount)
        {
            if (partitionCount < 1)
                throw new ArgumentOutOfRangeException("partitionCount");
            return (from i in Enumerable.Range(0, partitionCount)
                    select InfiniteEnumerator()).ToArray();
        }

        public override bool SupportsDynamicPartitions { get { return true; } }

        public override IEnumerable<bool> GetDynamicPartitions()
        {
            return new InfiniteEnumerators();
        }

        private static IEnumerator<bool> InfiniteEnumerator()
        {
            while (true) yield return true;
        }

        private class InfiniteEnumerators : IEnumerable<bool>
        {
            public IEnumerator<bool> GetEnumerator()
            {
                return InfiniteEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
