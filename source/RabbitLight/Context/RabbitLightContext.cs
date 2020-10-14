using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitLight.Config;
using RabbitLight.Consumer.Manager;
using RabbitLight.Helpers;
using RabbitLight.Publisher;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight.Context
{
    public abstract class RabbitLightContext : IDisposable
    {
        private readonly ContextConfig _config;
        private readonly IConsumerManager _consumerManager;
        private bool _registered = false;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ILogger<RabbitLightContext> _logger;

        public readonly IPublisher Publisher;

        public RabbitLightContext(IServiceProvider sp, ContextConfig config)
        {
            _config = config;
            _config.Validate();
            _logger = CreateLogger<RabbitLightContext>();

            StartMonitor();

            var consumerPool =  new ConsumerConnectionPool(_config.ConnConfig, CreateLogger<ConsumerConnectionPool>());
            _consumerManager = new ConsumerManager(sp, consumerPool, _config);

            var publisherPool = new PublisherConnectionPool(_config.ConnConfig, CreateLogger<PublisherConnectionPool>());
            Publisher = new Publisher.Publisher(publisherPool);

            ILogger<T> CreateLogger<T>() => sp.GetService<ILoggerFactory>()?.CreateLogger<T>();
        }

        public void StartMonitor()
        {
            Helpers.Monitor.Run(() => RabbitHttpClient.CreateVHostAndConfigs(_config.ConnConfig),
                _config.ConnConfig.MonitoringInterval, _config.ConnConfig.MonitoringInterval, _cts.Token,
                ex => Task.Run(() => _logger?.LogError(ex, "[RabbitLight] Error while ensuring VHost and configs")));
        }

        public async Task Register()
        {
            if (!_registered)
            {
                await RabbitHttpClient.CreateVHostAndConfigs(_config.ConnConfig);
                await _consumerManager.Register();
                _registered = true;
            }
        }

        public void Dispose() => _consumerManager.Dispose();
    }
}
