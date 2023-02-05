using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using System;

namespace RabbitLight.Config
{
    public class ConnectionConfig
    {
        /// <summary>
        /// Username to use when authenticating to the server
        /// </summary>
        public string UserName { get; set; }
        /// <summary>
        /// Password to use when authenticating to the server
        /// </summary>
        public string Password { get; set; }
        /// <summary>
        /// Virtual host to access during this connection 
        /// </summary>
        public string VirtualHost { get; set; }
        /// <summary>
        /// The host to connect to
        /// </summary>
        public string HostName { get; set; }
        /// <summary>
        /// The port to connect on
        /// </summary>
        public ushort Port { get; set; }
        /// <summary>
        /// The amqp protocol to use (amqp or amqps)
        /// </summary>
        public string AmqpProtocol { get; set; } = "amqp";
        /// <summary>
        /// The api protocol to use (http or https)
        /// </summary>
        public string ApiProtocol { get; set; } = "http";
        /// <summary>
        /// Port where RabbitMQ management UI plugin is available
        /// </summary>
        public ushort PortApi { get; set; }
        /// <summary>
        /// Minimum number of parallel channels
        /// <br>https://www.rabbitmq.com/consumers.html#concurrency</br>
        /// </summary>
        public ushort MinChannels { get; set; } = 10;
        /// <summary>
        /// Maximum number of parallel channels
        /// <br>https://www.rabbitmq.com/consumers.html#concurrency</br>
        /// </summary>
        public ushort MaxChannels { get; set; } = 50;
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
        public ushort PrefetchCount { get; set; } = 10;
        /// <summary>
        /// Number of channels per connection (RabbitMQ's IConnection).
        /// <br>Should be equal to or smaller than the one configured on the server.</br>
        /// </summary>
        public ushort ChannelsPerConnection { get; set; } = 25;
        /// <summary>
        /// Delay for when Nacking a message for requeue or null to none
        /// </summary>
        public TimeSpan? RequeueDelay { get; set; }
        /// <summary>
        /// Interval regarding channel monitoring tasks (health check and scalling)
        /// </summary>
        public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromSeconds(60);
        /// <summary>
        /// Skip VHost creation and other configs
        /// </summary>
        public bool SkipVHostConfig { get; set; } = false;
        /// <summary>
        /// Skip queue and exchange declaration and bind
        /// </summary>
        public bool SkipDeclarations { get; set; } = false;

        private ConnectionConfig()
        {
        }

        public ConnectionConfig(string userName, string password, string vhost, string hostname, ushort port, ushort portApi,
            string amqpProtocol = null, string apiProtocol = null, ushort? minChannels = null, ushort? maxChannels = null,
            ushort? scallingThreshold = null, ushort? prefetchCount = null, ushort? channelsPerConnection = null,
            TimeSpan? requeueDelay = null, TimeSpan? monitoringInterval = null, bool? skipVHostConfig = null, bool? skipDeclarations = null)
        {
            UserName = userName;
            Password = password;
            VirtualHost = vhost;
            HostName = hostname;
            Port = port;
            AmqpProtocol = amqpProtocol;
            ApiProtocol = apiProtocol;

            PortApi = portApi;
            MinChannels = minChannels ?? MinChannels;
            MaxChannels = maxChannels ?? MaxChannels;
            ScallingThreshold = scallingThreshold;
            PrefetchCount = prefetchCount ?? PrefetchCount;
            ChannelsPerConnection = channelsPerConnection ?? ChannelsPerConnection;
            RequeueDelay = requeueDelay;
            MonitoringInterval = monitoringInterval ?? MonitoringInterval;
            SkipVHostConfig = skipVHostConfig ?? SkipVHostConfig;
            SkipDeclarations = skipDeclarations ?? SkipDeclarations;

            Validate(this);
        }

        public ConnectionFactory CreateConnectionFactory()
        {
            var uri = new Uri($"{AmqpProtocol}://{HostName}:{Port}");

            return new ConnectionFactory()
            {
                UserName = UserName,
                Password = Password,
                VirtualHost = VirtualHost,
                Uri = uri,
                RequestedChannelMax = ChannelsPerConnection,
                DispatchConsumersAsync = true,
                // https://www.rabbitmq.com/dotnet-api-guide.html#recovery
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                // https://www.rabbitmq.com/dotnet-api-guide.html#topology-recovery
                TopologyRecoveryEnabled = true
            };
        }

        public static ConnectionConfig FromConfig(IConfiguration configuration)
        {
            var config = new ConnectionConfig();
            configuration.Bind(config);

            Validate(config);

            return config;
        }

        private static void Validate(ConnectionConfig config)
        {
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
        }
    }
}
