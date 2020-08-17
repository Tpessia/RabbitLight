using RabbitLight.Attributes;
using RabbitLight.Interfaces;
using RabbitLight.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight
{
    public class RabbitClient : IDisposable
    {
        // Connection
        private readonly ChannelConfig _channelConfig;
        private readonly ConnFactory _connFactory;
        private readonly RabbitConnectionManager _connManager;

        // Consumers
        private readonly IEnumerable<Assembly> _assemblies;
        private readonly IServiceProvider _serviceProvider;
        private readonly List<ConsumerMetadata> _consumers = new List<ConsumerMetadata>();

        // Consumer Events
        private readonly Func<Type, IServiceProvider, Task> _onStart;
        private readonly Func<Type, IServiceProvider, Task> _onEnd;

        // Helpers
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _clientLogger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public RabbitClient(IServiceProvider serviceProvider, ConnFactory connFactory,
            IEnumerable<Assembly> assemblies = null, ChannelConfig channelConfig = null,
            Func<Type, IServiceProvider, Task> onStart = null, Func<Type, IServiceProvider, Task> onEnd = null)
        {
            if (serviceProvider == null)
                throw new ArgumentException("Invalid null value", nameof(serviceProvider));

            if (connFactory == null)
                throw new ArgumentException("Invalid null value", nameof(connFactory));

            _channelConfig = channelConfig ?? new ChannelConfig();
            _connFactory = connFactory;
            _connFactory.DispatchConsumersAsync = true;
            _connFactory.RequestedChannelMax = _channelConfig.ChannelsPerConnection;
            _connManager = new RabbitConnectionManager(connFactory, channelConfig);

            _serviceProvider = serviceProvider;
            _assemblies = assemblies ?? new Assembly[] { Assembly.GetEntryAssembly() };

            _onStart = onStart;
            _onEnd = onEnd;

            _loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
            _clientLogger = _loggerFactory?.CreateLogger<RabbitClient>();
        }

        #region Public

        public void Register()
        {
            _clientLogger?.LogWarning($"[RabbitMQ] Registrando consumers\r\n{_channelConfig}");

            RegisterConsumers().Wait();
            RegisterListeners(_channelConfig.MinChannels).Wait();

            StartListenersMonitor();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _connManager.Dispose();
        }

        #endregion

        #region Private

        private async Task RegisterConsumers()
        {
            var consumerTypes = _assemblies.Select(a => a.GetTypes()
                .Where(x => typeof(IConsumer).IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract))
                .SelectMany(x => x);

            _consumers.Clear();
            foreach (var consumerType in consumerTypes)
            {
                var logger = _loggerFactory?.CreateLogger(consumerType);

                // Get Exchange
                var (exchange, methodsInfo) = GetExchangeMetadata(consumerType);

                foreach (var methodInfo in methodsInfo)
                {
                    // Get Queues
                    var queuesMetadata = GetQueuesMetadata(methodInfo);
                    if (!queuesMetadata.HasValue) continue;
                    var (queues, consumerParamType) = queuesMetadata.Value;

                    // Declare Queues
                    using var channel = await _connManager.CreateSingleChannel();
                    foreach (var queue in queues)
                    {
                        DeclareQueue(channel, exchange, queue);
                        _consumers.Add(new ConsumerMetadata
                        {
                            Type = consumerType,
                            ParamType = consumerParamType,
                            MethodInfo = methodInfo,
                            Exchange = exchange,
                            Queue = queue,
                            Logger = logger
                        });
                    }
                }
            }
        }

        private async Task RegisterListeners(int channelCount)
        {
            var bellowMin = _channelConfig.MinChannels > _connManager.TotalChannels;
            var overMax = _connManager.TotalChannels >= _channelConfig.MaxChannels;
            var canCreate = bellowMin || !overMax;
            if (!canCreate) return;

            for (int i = 0; i < channelCount; i++)
            {
                var channel = await _connManager.CreateChannel();
                foreach (var consumer in _consumers)
                    RegisterListeners(consumer, channel);
            }
        }

        private IModel RegisterListeners(ConsumerMetadata consumer, IModel channel)
        {
            var consumerEvent = new AsyncEventingBasicConsumer(channel);
            consumerEvent.Received += async (ch, ea) =>
            {
                // Create New DI Scope
                using (var scope = _serviceProvider.CreateScope())
                {
                    try
                    {
                        // Start Callback
                        if (_onStart != null)
                            await _onStart.Invoke(consumer.Type, scope.ServiceProvider);

                        // Invoke Consumer
                        await consumer.Invoke(scope.ServiceProvider, ea);

                        // End Callback
                        if (_onEnd != null)
                            await _onEnd.Invoke(consumer.Type, scope.ServiceProvider);

                        // ACK
                        // TODO: tentar fazer ACK de multiple?
                        channel.BasicAck(ea.DeliveryTag, false);
                    }
                    catch (DiscardMessageException ex)
                    {
                        // Error: Discard
                        consumer.Logger?.LogWarning(ex, $"[RabbitMQ] Message discarded");
                        channel.BasicNack(ea.DeliveryTag, false, false);
                    }
                    catch (Exception ex)
                    {
                        // Error: Requeue
                        consumer.Logger?.LogError(ex, "[RabbitMQ] Error while consuming, requeueing message");
                        var delay = (int)_channelConfig.RequeueInterval.TotalMilliseconds;
                        _ = Task.Delay(delay).ContinueWith(t => channel.BasicNack(ea.DeliveryTag, false, true));
                    }
                    finally
                    {
                        await Task.Yield();
                    }
                }
            };

            // TODO: tentar cancelar consumer antes de fechar o channel
            // the consumer tag identifies the subscription when it has to be cancelled
            string consumerTag = channel.BasicConsume(consumer.Queue.Name, false, consumerEvent);

            return channel;
        }

        private void StartListenersMonitor()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        _connManager.DisposeClosedChannels();

                        var tasks = _consumers.Select(x => GetMessageCount(x.Queue.Name));
                        var requests = await Task.WhenAll(tasks);
                        var messageCount = requests.Sum();

                        var expected = _channelConfig.ScallingThreshold.HasValue
                            ? _channelConfig.MinChannels + (int)Math.Ceiling((double)messageCount / _channelConfig.ScallingThreshold.Value)
                            : _channelConfig.MinChannels;
                        expected = expected > _channelConfig.MaxChannels ? _channelConfig.MaxChannels : expected;
                        var current = _connManager.TotalChannels;

                        var diff = expected - current;

                        if (diff != 0)
                            _clientLogger?.LogWarning($"[RabbitMQ] Scalling ({current} -> {expected})");

                        if (diff > 0)
                            await RegisterListeners(diff);
                        else if (diff < 0)
                            _connManager.DeleteChannels(-diff);
                    }
                    catch (Exception ex)
                    {
                        _clientLogger?.LogError(ex, "[RabbitMQ] Error while scalling");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(60), _cts.Token);
                }
            }, _cts.Token);

            async Task<int> GetMessageCount(string queue)
            {
                var vhost = _connFactory.VirtualHost == "/" ? "%2F" : _connFactory.VirtualHost;
                using var client = RabbitConnectionManager.CreateHttpClient(_connFactory);
                var request = await client.GetAsync($"queues/{vhost}/{queue}");
                request.EnsureSuccessStatusCode();
                var result = JObject.Parse(await request.Content.ReadAsStringAsync());
                return result["messages"].ToObject<int>();
            }
        }

        private (ExchangeAttribute, IEnumerable<MethodInfo>) GetExchangeMetadata(Type consumerType)
        {
            // TODO: iterar sobre varias exchanges ao inves de first or default
            var exchange = consumerType.GetCustomAttributes<ExchangeAttribute>(false).FirstOrDefault();

            if (exchange == null)
                throw new Exception($"Missing {nameof(ExchangeAttribute)} on consumer: {consumerType.FullName}");

            var methodsInfo = consumerType.GetMethods();
            var hasQueue = methodsInfo.Any(x => x.GetCustomAttributes<QueueAttribute>(false).Any());

            if (!hasQueue)
                throw new Exception($"Missing {nameof(QueueAttribute)} on consumer: {consumerType.FullName}");

            return (exchange, methodsInfo);
        }

        private (IEnumerable<QueueAttribute>, Type)? GetQueuesMetadata(MethodInfo methodInfo)
        {
            // Get Queue
            ParameterInfo[] paramsInfo = methodInfo.GetParameters();

            if (paramsInfo.Count() != 1) return null;

            // Get Queue Parameter
            Type consumerParamType = paramsInfo[0].ParameterType;
            var isBusContext = consumerParamType.IsGenericType
                && consumerParamType.GetGenericTypeDefinition() == typeof(BusContext<>);

            if (!isBusContext) return null;

            var queues = methodInfo.GetCustomAttributes<QueueAttribute>();

            if (!queues.Any()) return null;

            return (queues, consumerParamType);
        }

        private void DeclareQueue(IModel channel, ExchangeAttribute exchange, QueueAttribute queue)
        {
            channel.ExchangeDeclare(exchange: exchange.Name, type: ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);
            channel.QueueDeclare(queue: queue.Name, durable: true, exclusive: false, autoDelete: false, arguments: null);
            channel.QueueBind(queue: queue.Name, exchange: exchange.Name, routingKey: queue.RoutingKey, arguments: null);
        }

        #endregion
    }
}