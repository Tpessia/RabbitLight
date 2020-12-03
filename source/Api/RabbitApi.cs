using RabbitLight.Config;
using RabbitLight.Extensions;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RabbitLight.Api
{
    internal class RabbitApi : IRabbitApi
    {
        private readonly ContextConfig _config;

        public RabbitApi(ContextConfig config)
        {
            if (config == null)
                throw new ArgumentException("Invalid null value", nameof(config));

            _config = config;
        }

        public async Task CreateExchange(string exchange, string type = ExchangeType.Topic, bool durable = true, bool autoDelete = false, IDictionary<string, object> arguments = null)
        {
            var exchangeTypes = ExchangeType.All();
            if (!exchangeTypes.Any(x => x == type))
                throw new Exception($"Exchange \"{exchange}\" type should be one of: {string.Join(", ", exchangeTypes)}");

            await RunChannel((conn, channel) => Task.Run(() =>
                channel.ExchangeDeclare(exchange: exchange, type: type, durable: durable, autoDelete: autoDelete,   arguments: arguments)));
        }

        public async Task CreateQueue(string queue, bool durable = true, bool exclusive = false, bool autoDelete = false, IDictionary<string, object> arguments = null)
        {
            await RunChannel((conn, channel) => Task.Run(() =>
                channel.QueueDeclare(queue: queue, durable: durable, exclusive: exclusive, autoDelete: autoDelete, arguments: arguments)));
        }

        public async Task CreateBind(string queue, string exchange, string routingKey = "#",  IDictionary<string, object> arguments = null)
        {
            await RunChannel((conn, channel) => Task.Run(() =>
                channel.QueueBind(queue: queue, exchange: exchange, routingKey: routingKey, arguments: arguments)));
        }

        private async Task RunChannel(Func<IConnection, IModel, Task> func)
        {
            var connFactory = _config.ConnConfig.CreateConnectionFactory();
            var conn = await connFactory.CreateConnectionAsync();
            var channel = await conn.CreateModelAsync();

            using (conn)
            {
                using (channel)
                {
                    await func(conn, channel);
                }
            }
        }
    }
}
