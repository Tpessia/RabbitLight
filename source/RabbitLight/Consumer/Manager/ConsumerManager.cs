using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitLight.Config;
using RabbitLight.Exceptions;
using RabbitLight.Helpers;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight.Consumer.Manager
{
    internal class ConsumerManager : IConsumerManager
    {
        // Connection
        private readonly ConnectionConfig _connConfig;
        private readonly IConnectionPool _connPool;

        // Consumers
        private readonly IServiceProvider _sp;
        private readonly IEnumerable<Type> _consumerTypes;
        private readonly List<ConsumerMetadata> _consumers = new List<ConsumerMetadata>();

        // Consumer Events
        private readonly Func<IServiceProvider, Type, BasicDeliverEventArgs, Task> _onStart;
        private readonly Func<IServiceProvider, Type, BasicDeliverEventArgs, Task> _onEnd;

        // Helpers
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public ConsumerManager(IServiceProvider sp, ConnectionConfig connConfig,
            IConnectionPool connManager, IEnumerable<Type> consumerTypes = null,
            Func<IServiceProvider, Type, BasicDeliverEventArgs, Task> onStart = null,
            Func<IServiceProvider, Type, BasicDeliverEventArgs, Task> onEnd = null)
        {
            if (sp == null)
                throw new ArgumentException("Invalid null value", nameof(sp));

            if (connConfig == null)
                throw new ArgumentException("Invalid null value", nameof(connConfig));

            if (connManager == null)
                throw new ArgumentException("Invalid null value", nameof(connManager));

            _sp = sp;

            _connConfig = connConfig;
            _connPool = connManager;

            consumerTypes ??= Assembly.GetEntryAssembly().GetTypes();
            _consumerTypes = consumerTypes.Where(x => typeof(ConsumerBase).IsAssignableFrom(x)
                && !x.IsInterface && !x.IsAbstract);

            _onStart = onStart;
            _onEnd = onEnd;

            _loggerFactory = sp.GetService<ILoggerFactory>();
            _logger = _loggerFactory?.CreateLogger<ConsumerManager>();
        }

        #region Public

        public void Register()
        {
            // TODO: Prevent overusage of resources before full app startup??? needs testing
            Task.Delay(2000, _cts.Token).Wait();

            _logger?.LogInformation($"[RabbitLight] Registering consumers");

            RegisterConsumers().Wait();
            RegisterListeners(_connConfig.MinChannels).Wait();

            StartListenersMonitor();

            _logger?.LogInformation($"[RabbitLight] Successfully registered all consumers");
        }

        public void Dispose()
        {
            _cts.Cancel();
            _connPool.Dispose();
        }

        #endregion

        #region Private

        private async Task RegisterConsumers()
        {
            ValidateExchanges(_consumerTypes);

            _consumers.Clear();
            foreach (var consumerType in _consumerTypes)
            {
                var logger = _loggerFactory?.CreateLogger(consumerType);

                // Get Exchange
                var (exchanges, methodsInfo) = GetExchangeMetadata(consumerType);

                foreach (var methodInfo in methodsInfo)
                {
                    // Get Queues
                    var queuesMetadata = GetQueuesMetadata(methodInfo);
                    if (!queuesMetadata.HasValue) continue;
                    var (queues, consumerParamType) = queuesMetadata.Value;

                    // Declare Queues
                    using var channel = await _connPool.CreateUnmanagedChannel();
                    foreach (var exchange in exchanges)
                    {
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
        }

        private async Task RegisterListeners(int channelCount)
        {
            for (int i = 0; i < channelCount; i++)
            {
                var overMax = _connPool.TotalChannels >= _connConfig.MaxChannels;
                if (overMax) break;

                var channel = await _connPool.CreateConsumerChannel();
                foreach (var consumer in _consumers)
                    RegisterListener(consumer, channel);
            }
        }

        private IModel RegisterListener(ConsumerMetadata consumer, IModel channel)
        {
            var consumerEvent = new AsyncEventingBasicConsumer(channel);
            consumerEvent.Received += async (ch, ea) =>
            {
                // Create New DI Scope
                using (var scope = _sp.CreateScope())
                {
                    try
                    {
                        // Start Callback
                        if (_onStart != null)
                            await _onStart.Invoke(scope.ServiceProvider, consumer.Type, ea);

                        // Invoke Consumer
                        await consumer.Invoke(scope.ServiceProvider, ea);

                        // End Callback
                        if (_onEnd != null)
                            await _onEnd.Invoke(scope.ServiceProvider, consumer.Type, ea);

                        // ACK
                        // TODO: tentar fazer ACK de multiple?
                        Ack(channel, ea.DeliveryTag);
                    }
                    catch (DiscardMessageException ex)
                    {
                        // Error: Discard
                        consumer.Logger?.LogWarning(ex, $"[RabbitLight] Message discarded");
                        Nack(channel, ea.DeliveryTag);
                    }
                    catch (Exception ex)
                    {
                        // Error: Requeue
                        consumer.Logger?.LogError(ex, "[RabbitLight] Error while consuming, requeueing message");
                        if (_connConfig.RequeueDelay.HasValue)
                        {
                            var delay = (int)_connConfig.RequeueDelay.Value.TotalMilliseconds;
                            _ = Task.Delay(delay, _cts.Token)
                                .ContinueWith(t => Nack(channel, ea.DeliveryTag, requeue: true), _cts.Token);
                        }
                        else
                        {
                            Nack(channel, ea.DeliveryTag, requeue: true);
                        }
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

            void Ack(IModel channel, ulong deliveryTag, bool multiple = false) {
                if (channel.IsOpen) channel.BasicAck(deliveryTag, multiple);
            };

            void Nack(IModel channel, ulong deliveryTag, bool multiple = false, bool requeue = false)
            {
                if (channel.IsOpen) channel.BasicNack(deliveryTag, multiple, requeue);
            };
        }

        private void StartListenersMonitor()
        {
            Monitoring.Run(async () =>
            {
                int expected = _connConfig.MinChannels;

                if (_connConfig.ScallingThreshold.HasValue)
                {
                    var tasks = _consumers.Select(x => _connPool.GetMessageCount(x.Queue.Name));
                    var requests = await Task.WhenAll(tasks);
                    var messageCount = requests.Sum();

                    expected += (int)Math.Ceiling((double)messageCount / _connConfig.ScallingThreshold.Value);
                }

                expected = expected > _connConfig.MaxChannels ? _connConfig.MaxChannels : expected;
                var diff = expected - _connPool.TotalChannels;

                if (diff != 0)
                    _logger?.LogWarning($"[RabbitLight] Scalling ({_connPool.TotalChannels} -> {expected})");

                if (diff > 0)
                    await RegisterListeners(diff);
                else if (diff < 0)
                    _connPool.DeleteChannels(-diff);
            }, _connConfig.MonitoringInterval, _cts.Token,
            ex => Task.Run(() => _logger?.LogError(ex, "[RabbitLight] Error while scalling")));
        }

        private void ValidateExchanges(IEnumerable<Type> consumerTypes)
        {
            var exchanges = consumerTypes.Select(x => x.GetCustomAttributes<ExchangeAttribute>(false)).SelectMany(x => x);
            var duplicateExchanges = exchanges.GroupBy(x => x.Name).Where(x => x.Count() > 1);

            var invalidExchanges = new Dictionary<string, IEnumerable<string>>();
            foreach (var dups in duplicateExchanges)
            {
                var types = dups.Select(x => x.Type);
                var uniqueTypes = types.Distinct();
                if (uniqueTypes.Count() > 1) invalidExchanges.Add(dups.Key, uniqueTypes);
            }

            if (invalidExchanges.Any())
                throw new Exception("Multiple exchange declaration should be of a same type: "
                    + $"{string.Join("\r\n", invalidExchanges.Select(x => $"{x.Key}: {string.Join(", ", x.Value)}"))}");
        }

        private (IEnumerable<ExchangeAttribute>, IEnumerable<MethodInfo>) GetExchangeMetadata(Type consumerType)
        {
            var exchanges = consumerType.GetCustomAttributes<ExchangeAttribute>(false);

            if (!exchanges.Any())
                throw new Exception($"Missing {nameof(ExchangeAttribute)} on consumer: {consumerType.FullName}");

            var methodsInfo = consumerType.GetMethods();
            var hasQueue = methodsInfo.Any(x => x.GetCustomAttributes<QueueAttribute>(false).Any());

            if (!hasQueue)
                throw new Exception($"Missing {nameof(QueueAttribute)} on consumer: {consumerType.FullName}");

            return (exchanges, methodsInfo);
        }

        private (IEnumerable<QueueAttribute>, Type)? GetQueuesMetadata(MethodInfo methodInfo)
        {
            // Get Queue
            ParameterInfo[] paramsInfo = methodInfo.GetParameters();

            if (paramsInfo.Count() != 1) return null;

            // Get Queue Parameter
            Type consumerParamType = paramsInfo[0].ParameterType;
            var isBusContext = consumerParamType.IsGenericType
                && consumerParamType.GetGenericTypeDefinition() == typeof(MessageContext<>);

            if (!isBusContext) return null;

            var queues = methodInfo.GetCustomAttributes<QueueAttribute>();

            if (!queues.Any()) return null;

            return (queues, consumerParamType);
        }

        private void DeclareQueue(IModel channel, ExchangeAttribute exchange, QueueAttribute queue)
        {
            channel.ExchangeDeclare(exchange: exchange.Name, type: exchange.Type, durable: true, autoDelete: false, arguments: null);
            channel.QueueDeclare(queue: queue.Name, durable: true, exclusive: false, autoDelete: false, arguments: null);

            foreach (var routingKey in queue.RoutingKeys)
                channel.QueueBind(queue: queue.Name, exchange: exchange.Name, routingKey: routingKey, arguments: null);
        }

        #endregion
    }
}