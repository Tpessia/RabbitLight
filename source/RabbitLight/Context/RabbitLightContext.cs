using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitLight.Config;
using RabbitLight.Consumer.Manager;
using RabbitLight.Helpers;
using RabbitLight.Publisher;
using System;

namespace RabbitLight.Context
{
    public abstract class RabbitLightContext : IDisposable
    {
        private readonly ContextConfig _config;
        private readonly IConsumerManager _consumerManager;
        private bool _registered = false;

        public readonly IPublisher Publisher;

        public RabbitLightContext(IServiceProvider sp, ContextConfig config)
        {
            _config = config;
            _config.Validate();

            var publisherPool = CreateConnPool(sp, _config.ConnConfig);
            Publisher = new Publisher.Publisher(publisherPool);

            var consumerPool = CreateConnPool(sp, _config.ConnConfig);
            _consumerManager = new ConsumerManager(sp, consumerPool, _config);

            IConnectionPool CreateConnPool(IServiceProvider sp, ConnectionConfig config) =>
                new ConnectionPool(config, sp.GetService<ILoggerFactory>()?.CreateLogger<ConnectionPool>());
        }

        public void Register()
        {
            if (!_registered)
            {
                _consumerManager.Register();
                _registered = true;
            }
        }

        public void Dispose() => _consumerManager.Dispose();
    }
}
