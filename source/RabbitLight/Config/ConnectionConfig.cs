using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using System;

namespace RabbitLight.Config
{
    public class ConnectionConfig : ConnectionFactory
    {
        /// <summary>
        /// Port where RabbitMQ management UI plugin is available
        /// </summary>
        public ushort PortApi { get; set; }
        /// <summary>
        /// Minimum number of parallel channels
        /// <br>https://www.rabbitmq.com/consumers.html#concurrency</br>
        /// </summary>
        public ushort MinChannels { get; set; }
        /// <summary>
        /// Maximum number of parallel channels
        /// <br>https://www.rabbitmq.com/consumers.html#concurrency</br>
        /// </summary>
        public ushort MaxChannels { get; set; }
        /// <summary>
        /// Number of messages required to scale a new channel (e.g. 1000 messages)
        /// </summary>
        public ushort? ScallingThreshold { get; set; }
        /// <summary>
        /// Number of messages that will be cached by each channel at once.
        /// <br>As RabbitMQ directs the channel's worker to the consumers with Round Robin,</br>
        /// <br>it should be a number smaller enough to prevent blocking other queues for a long time,</br>
        /// <br>and big enough to prevent multipe fetchs to the server (e.g. 10).</br>
        /// </summary>
        public ushort PrefetchCount { get; set; }
        /// <summary>
        /// Number of channels per connection (RabbitMQ's IConnection).
        /// <br>Should be equal to or smaller than the one configured on the server.</br>
        /// </summary>
        public ushort ChannelsPerConnection { get; set; }
        /// <summary>
        /// Delay for when Nacking a message for requeue
        /// </summary>
        public TimeSpan? RequeueInterval { get; set; }

        private ConnectionConfig()
        {
        }

        public static ConnectionConfig FromConfig(IConfiguration configuration)
        {
            var config = new ConnectionConfig();
            configuration.Bind(config);

            if (config.MinChannels < 1)
                throw new ArgumentException("Should be bigger than 0", nameof(config.MinChannels));

            if (config.MaxChannels < 1)
                throw new ArgumentException("Should be bigger than 0", nameof(config.MaxChannels));

            if (config.MinChannels > config.MaxChannels)
                throw new ArgumentException($"{nameof(config.MaxChannels)} should be bigger than {nameof(config.MinChannels)}", nameof(config.MaxChannels));

            if (config.ScallingThreshold < 1)
                throw new ArgumentException("Should be bigger than 0", nameof(config.ScallingThreshold));

            if (config.PrefetchCount < 0)
                throw new ArgumentException("Should be equal to or bigger than 0", nameof(config.PrefetchCount));

            if (config.ChannelsPerConnection < 1)
                throw new ArgumentException("Should be bigger than 0", nameof(config.ChannelsPerConnection));

            config.PortApi = config.PortApi;
            config.MinChannels = config.MinChannels == default ? (ushort)10 : config.MinChannels;
            config.MaxChannels = config.MaxChannels == default ? (ushort)50 : config.MaxChannels;
            config.ScallingThreshold = config.ScallingThreshold;
            config.PrefetchCount = config.PrefetchCount == default ? (ushort)10 : config.PrefetchCount;
            config.ChannelsPerConnection = config.ChannelsPerConnection == default ? (ushort)20 : config.ChannelsPerConnection;
            config.RequeueInterval = config.RequeueInterval;

            return config;
        }
    }
}
