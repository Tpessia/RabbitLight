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
        // Config
        private readonly ContextConfig _config;

        // Connection
        private readonly IConsumerConnectionPool _connPool;

        // Consumers
        private readonly IServiceProvider _sp;
        private readonly List<ConsumerMetadata> _consumers = new List<ConsumerMetadata>();

        // Helpers
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public ConsumerManager(IServiceProvider sp, IConsumerConnectionPool connPool, ContextConfig config)
        {
            if (sp == null)
                throw new ArgumentException("Invalid null value", nameof(sp));

            if (connPool == null)
                throw new ArgumentException("Invalid null value", nameof(connPool));

            if (config == null)
                throw new ArgumentException("Invalid null value", nameof(config));

            _sp = sp;
            _connPool = connPool;
            _config = config;

            _loggerFactory = sp.GetService<ILoggerFactory>();
            _logger = _loggerFactory?.CreateLogger<ConsumerManager>();
        }

        #region Public

        public async Task Register()
        {
            _logger?.LogInformation($"[RabbitLight] Registering consumers");

            RegisterConsumers();
            await RegisterListeners(_config.ConnConfig.MinChannels);

            StartMonitor();

            _logger?.LogInformation($"[RabbitLight] Successfully registered all consumers");
        }

        public void Dispose()
        {
            _cts.Cancel();
            _connPool.Dispose();
        }

        #endregion

        #region Private

        private void RegisterConsumers()
        {
            ValidateExchanges(_config.Consumers);
            ValidateConsumers(_config.Consumers);

            _consumers.Clear();
            foreach (var consumerType in _config.Consumers)
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
                    foreach (var exchange in exchanges)
                    {
                        foreach (var queue in queues)
                        {
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
            using (var channel = await _connPool.CreateUnmanagedChannel())
            {
                foreach (var consumer in _consumers)
                    DeclareQueue(channel, consumer.Exchange, consumer.Queue);
            }

            for (int i = 0; i < channelCount; i++)
            {
                var overMax = _connPool.TotalChannels >= _config.ConnConfig.MaxChannels;
                if (overMax) break;

                _logger?.LogDebug($"[RabbitLight] Creating listeners ({i + 1}/{channelCount})");

                var channel = await _connPool.CreateNewChannel();
                foreach (var consumer in _consumers)
                    RegisterListener(consumer, channel);
            }
        }

        private IModel RegisterListener(ConsumerMetadata consumer, IModel channel)
        {
            var consumerEvent = new AsyncEventingBasicConsumer(channel);
            consumerEvent.Received += async (ch, ea) =>
            {
                // Check if channel is available
                try
                {
                    ValidateChannel(channel);

                    // Will return false if channel has been deleted
                    var allowed = _connPool.NotifyConsumerStart(channel);
                    if (!allowed)
                    {
                        Nack(channel, ea.DeliveryTag, requeue: true);
                        await Task.Yield();
                        return;
                    }
                }
                catch (Exception)
                {
                    Nack(channel, ea.DeliveryTag, requeue: true);
                    await Task.Yield();
                    return;
                }

                // Create New DI Scope
                using (var scope = _sp.CreateScope())
                {
                    try
                    {
                        ValidateChannel(channel);

                        // Start Callback
                        if (_config.OnStart != null)
                            await _config.OnStart.Invoke(scope.ServiceProvider, consumer.Type, ea);

                        ValidateChannel(channel);

                        // Invoke Consumer
                        await consumer.InvokeConsumer(scope.ServiceProvider, ea);

                        ValidateChannel(channel);

                        // End Callback
                        if (_config.OnEnd != null)
                            await _config.OnEnd.Invoke(scope.ServiceProvider, consumer.Type, ea);

                        // Ack
                        Ack(channel, ea.DeliveryTag);

                        // Ack Callback
                        if (_config.OnEnd != null)
                            await _config.OnAck.Invoke(scope.ServiceProvider, consumer.Type, ea);
                    }
                    catch (DiscardMessageException ex)
                    {
                        // Error: Discard

                        consumer.Logger?.LogWarning(ex, $"[RabbitLight] Message discarded");
                        Nack(channel, ea.DeliveryTag, requeue: false);
                    }
                    catch (Exception ex)
                    {
                        // Error: Requeue or Discard

                        var requeue = _config.OnError == null ? true
                            : await _config.OnError.Invoke(scope.ServiceProvider, ex, consumer.Type, ea);

                        if (requeue)
                        {
                            consumer.Logger?.LogError(ex, $"[RabbitLight] Error while consuming, requeueing message");

                            if (_config.ConnConfig.RequeueDelay.HasValue)
                            {
                                var delay = (int)_config.ConnConfig.RequeueDelay.Value.TotalMilliseconds;
                                _ = Task.Delay(delay, _cts.Token)
                                    .ContinueWith(t => Nack(channel, ea.DeliveryTag, requeue: true), _cts.Token);
                            }
                            else
                            {
                                Nack(channel, ea.DeliveryTag, requeue: true);
                            }
                        }
                        else
                        {
                            consumer.Logger?.LogError(ex, "[RabbitLight] Error while consuming, discarding message");
                            Nack(channel, ea.DeliveryTag, requeue: false);
                        }
                    }
                    finally
                    {
                        _connPool.NotifyConsumerEnd(channel);
                        await Task.Yield();
                    }
                }
            };

            // the consumer tag identifies the subscription when it has to be cancelled
            string consumerTag = channel.BasicConsume(consumer.Queue.Name, false, consumerEvent);

            return channel;

            // TODO: during application shut down, the consumer might have runned,
            // but the ACK might be unable to run
            void Ack(IModel ch, ulong deliveryTag, bool multiple = false)
            {
                if (ch.IsOpen) ch.BasicAck(deliveryTag, multiple);
            };

            void Nack(IModel ch, ulong deliveryTag, bool multiple = false, bool requeue = false)
            {
                if (ch.IsOpen) ch.BasicNack(deliveryTag, multiple, requeue);
            };

            bool ValidateChannel(IModel ch) => ch.IsOpen ? true : throw new Exception("[RabbitLight] Channel is unexpectedly closed");
        }

        private void StartMonitor()
        {
            Helpers.Monitor.Run(() => ScaleListeners(),
                TimeSpan.FromSeconds(10), _config.ConnConfig.MonitoringInterval, _cts.Token,
                ex => Task.Run(() => _logger?.LogError(ex, "[RabbitLight] Error while scalling")));
        }

        private async Task ScaleListeners()
        {
            _logger?.LogDebug($"\r\n[RabbitLight] Start scalling");

            int expected = _config.ConnConfig.MinChannels;

            if (_config.ConnConfig.ScallingThreshold.HasValue)
            {
                // try catch in case the initial queues were killed
                try
                {
                    var tasks = _consumers.Select(x => RabbitHttpClient.GetMessageCount(_config.ConnConfig, x.Queue.Name));
                    var requests = await Task.WhenAll(tasks);
                    var messageCount = requests.Sum();

                    expected += (int)Math.Ceiling((double)messageCount / _config.ConnConfig.ScallingThreshold.Value);
                }
                catch { }
            }

            _logger?.LogDebug($"[RabbitLight] Expected channels: {expected}");

            expected = expected > _config.ConnConfig.MaxChannels ? _config.ConnConfig.MaxChannels : expected;
            var diff = expected - _connPool.TotalChannels;

            _logger?.LogDebug($"[RabbitLight] Total channels: {_connPool.TotalChannels}");
            _logger?.LogDebug($"[RabbitLight] Diff channels: {diff}");

            if (diff != 0)
                _logger?.LogDebug($"[RabbitLight] Scalling ({_connPool.TotalChannels} -> {expected})");

            if (diff > 0)
                await RegisterListeners(diff);
            else if (diff < 0)
                await _connPool.DeleteChannels(-diff);

            _logger?.LogDebug($"[RabbitLight] End scalling\r\n");
        }

        private void ValidateExchanges(IEnumerable<Type> consumerTypes)
        {
            var exchanges = consumerTypes.Select(x => GetExchangeMetadata(x).Item1).SelectMany(x => x);
            var duplicateExchanges = exchanges.GroupBy(x => x.Name).Where(x => x.Count() > 1);

            var invalidExchanges = new List<string>();
            foreach (var dups in duplicateExchanges)
            {
                var types = dups.Select(x => x.ExchangeType);
                var uniqueTypes = types.Distinct();
                if (uniqueTypes.Count() > 1)
                    invalidExchanges.Add($"{dups.Key}: {string.Join(", ", uniqueTypes)}");
            }

            if (invalidExchanges.Any())
                throw new Exception("Multiple exchange declarations should be of a same exchange type: "
                    + $"{string.Join("; ", invalidExchanges)}");
        }

        private void ValidateConsumers(IEnumerable<Type> consumerTypes)
        {
            var consumerRows = new List<(string, string)>();

            var exchanges = consumerTypes.Select(x => GetExchangeMetadata(x));
            foreach (var exchange1 in exchanges)
            {
                foreach (var exchange2 in exchange1.Item1)
                {
                    foreach (var methodInfo in exchange1.Item2)
                    {
                        var queues = methodInfo.GetCustomAttributes<QueueAttribute>(false);
                        foreach (var queue in queues)
                        {
                            consumerRows.Add((exchange2.Name, queue.Name));
                        }
                    }
                }
            }

            var duplicateConsumers = consumerRows.GroupBy(x => x).Where(x => x.Count() > 1)
                .Select(x => x.First()).Select(x => $"{x.Item1}/{x.Item2}");
            if (duplicateConsumers.Any())
                throw new Exception("Cannot have duplicate consumers (exchange/queue pair): "
                    + $"{string.Join("; ", duplicateConsumers)}");
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
            channel.QueueDeclare(queue: queue.Name, durable: true, exclusive: false, autoDelete: false, arguments: null);

            if (exchange.Name != string.Empty)
            {
                channel.ExchangeDeclare(exchange: exchange.Name, type: exchange.ExchangeType, durable: true, autoDelete: false, arguments: null);

                foreach (var routingKey in queue.RoutingKeys)
                    channel.QueueBind(queue: queue.Name, exchange: exchange.Name, routingKey: routingKey, arguments: null);
            }
        }

        #endregion
    }
}