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
    public abstract class RabbitLightContext : IRabbitLightContext
    {
        private bool _started = false;
        private bool _registered = false;

        private readonly IConsumerManager _consumerManager;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        internal readonly ContextConfig Config;
        internal readonly IServiceProvider ServiceProvider;
        internal readonly ILogger<RabbitLightContext> Logger;

        public IPublisher Publisher { get; private set; }
        public IRabbitApi Api { get; private set; }

        public RabbitLightContext(IServiceProvider sp, ContextConfig config)
        {
            Logger = sp.GetService<ILoggerFactory>()?.CreateLogger<RabbitLightContext>();

            if (config == null)
                throw new ArgumentNullException(nameof(config), $"[{GetType().Name}] No configuration found");

            ServiceProvider = sp;

            Config = config;
            Config.Validate();

            var consumerPool =  new ConsumerConnectionPool(this);
            _consumerManager = new ConsumerManager(sp, consumerPool, Config);

            var publisherPool = new PublisherConnectionPool(this);
            Publisher = new Publisher.Publisher(publisherPool);

            Api = new RabbitApi(Config);
        }

        public async Task Register()
        {
            if (!_started)
            {
                try
                {
                    await TryRegister();
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, $"[{Config.Alias}] Error while registering context, trying again");
                }

                StartMonitor();

                _started = true;
            }
        }

        public void Dispose() => _consumerManager.Dispose();

        private void StartMonitor()
        {
            Helpers.Monitor.Run(async () => {
                await TryRegister();
            },
            TimeSpan.FromSeconds(0), Config.ConnConfig.MonitoringInterval, _cts.Token,
            ex => Task.Run(() => Logger?.LogError(ex, $"[{Config.Alias}] Error while registering context, trying again")));
        }

        private async Task TryRegister()
        {
            if (!Config.ConnConfig.SkipVHostConfig)
                await RabbitHttpClient.CreateVHostAndConfigs(Config.ConnConfig);

            if (!_registered)
            {
                await _consumerManager.Register();
                _registered = true;

                if (Config.OnConfig != null)
                    await Config.OnConfig(ServiceProvider);
            }
        }
    }
}
