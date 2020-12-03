using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitLight.Api;
using RabbitLight.Config;
using RabbitLight.ConnectionPool;
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
        private readonly IServiceProvider _sp;
        private readonly IConsumerManager _consumerManager;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ILogger<RabbitLightContext> _logger;
        private bool _registered = false;

        public readonly IPublisher Publisher;
        public readonly IRabbitApi Api;

        public RabbitLightContext(IServiceProvider sp, ContextConfig config)
        {
            _config = config;
            _config.Validate();
            _sp = sp;
            _logger = CreateLogger<RabbitLightContext>();

            var consumerPool =  new ConsumerConnectionPool(_config, CreateLogger<ConsumerConnectionPool>());
            _consumerManager = new ConsumerManager(sp, consumerPool, _config);

            var publisherPool = new PublisherConnectionPool(_config, sp, CreateLogger<PublisherConnectionPool>());
            Publisher = new Publisher.Publisher(publisherPool);

            Api = new RabbitApi(_config);

            ILogger<T> CreateLogger<T>() => sp.GetService<ILoggerFactory>()?.CreateLogger<T>();
        }

        public async Task Register()
        {
            if (!_registered)
            {
                try
                {
                    if (!_config.ConnConfig.SkipVHostConfig)
                        await RabbitHttpClient.CreateVHostAndConfigs(_config.ConnConfig);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RabbitLight] Unable to create VHost");
                }

                StartMonitor();

                await _consumerManager.Register();
                _registered = true;

                if (_config.OnConfig != null)
                    await _config.OnConfig(_sp);
            }
        }

        public void Dispose() => _consumerManager.Dispose();

        private void StartMonitor()
        {
            Helpers.Monitor.Run(async () => {
                if (!_config.ConnConfig.SkipVHostConfig)
                    await RabbitHttpClient.CreateVHostAndConfigs(_config.ConnConfig);
            },
            _config.ConnConfig.MonitoringInterval, _config.ConnConfig.MonitoringInterval, _cts.Token,
            ex => Task.Run(() => _logger?.LogError(ex, "[RabbitLight] Error while ensuring VHost and configs")));
        }
    }
}
