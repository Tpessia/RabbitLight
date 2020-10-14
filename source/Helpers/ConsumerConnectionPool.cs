using Microsoft.Extensions.Logging;
using RabbitLight.Config;
using RabbitLight.Extensions;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight.Helpers
{
    internal class ConsumerConnectionPool : IConsumerConnectionPool
    {
        private readonly ConnectionConfig _connConfig;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        // Channel groups
        private readonly Dictionary<IConnection, List<IModel>> _connPool = new Dictionary<IConnection, List<IModel>>(); // connection pool to ensure ratio of channels per connection
        private readonly Dictionary<IModel, int> _channelUsage = new Dictionary<IModel, int>(); // consumer usage counter, used for death row logic
        private readonly List<IModel> _deathRow = new List<IModel>(); // channels that were in use during removal attempt

        // Locks (to prevent unsynced management)
        private readonly SemaphoreSlim _connLock = new SemaphoreSlim(1); // ensures thread-safe lists
        private readonly SemaphoreSlim _deletionLock = new SemaphoreSlim(1); // ensures a consumer won't use a death row channel

        public int TotalChannels => !_connPool.Any() ? 0
            : _connPool.Values.Select(x => x.Count).Aggregate((a, b) => a + b) - _deathRow.Count;

        public ConsumerConnectionPool(ConnectionConfig connConfig, ILogger<ConsumerConnectionPool> logger = null)
        {
            if (connConfig == null)
                throw new ArgumentException("Invalid null value", nameof(connConfig));

            _connConfig = connConfig;
            _logger = logger;

            StartMonitor();
        }

        #region Public

        public async Task<IModel> CreateUnmanagedChannel()
        {
            var connFactory = _connConfig.CreateConnectionFactory();
            var conn = await connFactory.CreateConnectionAsync();
            return await conn.CreateModelAsync();
        }

        public async Task<IModel> CreateNewChannel()
        {
            _connLock.WaitOrThrow(TimeSpan.FromSeconds(30), _cts.Token);

            try
            {
                var channel = await CreateChannel();

                // Prefetch Size -> https://www.rabbitmq.com/amqp-0-9-1-reference.html#:~:text=long%20prefetch-size
                // Prefetch Count (global: false) -> applied separately to each new consumer on the channel
                channel.BasicQos(0, _connConfig.PrefetchCount, false);

                return channel;
            }
            finally
            {
                _connLock.Release();
            }
        }

        public async Task DeleteChannels(int count)
        {
            _connLock.WaitOrThrow(TimeSpan.FromSeconds(30), _cts.Token);

            try
            {
                if (!_connPool.Any()) return;

                for (int i = 0; i < count; i++)
                {
                    var smallestPool = _connPool.OrderBy(x => x.Value.Count()).FirstOrDefault();
                    if (!IsNull(smallestPool))
                    {
                        if (smallestPool.Value.Count() > 0)
                        {
                            var channel = smallestPool.Value[0];
                            await RemoveChannel(channel, smallestPool.Key);
                        }
                    }
                }
            }
            finally
            {
                _connLock.Release();
            }
        }

        public bool NotifyConsumerStart(IModel channel)
        {
            _deletionLock.WaitOrThrow(TimeSpan.FromSeconds(30), _cts.Token);

            try
            {
                if (channel == null || !_channelUsage.ContainsKey(channel) || _deathRow.Contains(channel))
                    return false;

                _channelUsage[channel]++;
                return true;
            }
            finally
            {
                _deletionLock.Release();
            }
        }

        public void NotifyConsumerEnd(IModel channel)
        {
            _deletionLock.WaitOrThrow(TimeSpan.FromSeconds(30), _cts.Token);

            try
            {
                _channelUsage[channel]--;
            }
            finally
            {
                _deletionLock.Release();
            }
        }

        public void Dispose()
        {
            // TODO: wait for _channelUsage == 0?

            _cts.Cancel();

            foreach (var poolItem in _connPool)
            {
                foreach (var ch in poolItem.Value)
                {
                    _deathRow.Add(ch);
                    ch.Close();
                    ch.Dispose();
                }

                var conn = poolItem.Key;
                conn.Close();
                conn.Dispose();
            }
        }

        #endregion

        #region Private

        private bool IsNull<T>(T obj) => obj.Equals(default(T));

        // Monitor

        private void StartMonitor()
        {
            Monitor.Run(async () =>
            {
                _logger?.LogDebug($"\r\n[RabbitLight] *** Start consumer pool monitor ***");

                _logger?.LogDebug($"[RabbitLight] Number of connections: {_connPool.Count}");
                _logger?.LogDebug($"[RabbitLight] Number of channels: {TotalChannels} ({_connPool.SelectMany(x => x.Value).Count()})");

                await DisposeDeathRow();
                await DisposeClosedChannels();

                _logger?.LogDebug($"[RabbitLight] *** Stop consumer pool monitor ***\r\n");
            },
            _connConfig.MonitoringInterval, _connConfig.MonitoringInterval, _cts.Token,
            ex => Task.Run(() => _logger?.LogError(ex, "[RabbitLight] Error while disposing connections/channels")));
        }

        private async Task DisposeClosedChannels()
        {
            _connLock.WaitOrThrow(TimeSpan.FromSeconds(30), _cts.Token);

            try
            {
                var poolClone = new Dictionary<IConnection, List<IModel>>(_connPool);
                foreach (var poolItem in poolClone)
                {
                    foreach (var ch in poolItem.Value.ToArray())
                    {
                        if (!ch.IsOpen)
                            await RemoveChannel(ch, poolItem.Key);
                    }

                    var conn = poolItem.Key;
                    var isEmpty = !poolItem.Value.Any();
                    if (!conn.IsOpen || isEmpty)
                        await RemoveConnection(conn);
                }
            }
            finally
            {
                _connLock.Release();
            }
        }

        private async Task DisposeDeathRow()
        {
            _connLock.WaitOrThrow(TimeSpan.FromSeconds(30), _cts.Token);

            try
            {
                if (!_deathRow.Any()) return;

                _logger?.LogDebug($"[RabbitLight] Disposing death row channels ({_deathRow.Count})");

                foreach (var channel in _deathRow.ToArray())
                    await RemoveChannel(channel);
            }
            finally
            {
                _connLock.Release();
            }
        }

        // Managers

        private async Task<IConnection> GetOrCreateConnection()
        {
            IConnection conn;
            var poolItem = _connPool.FirstOrDefault(x => x.Value.Count() < _connConfig.ChannelsPerConnection);

            // Prevent returning a closed connection
            if (!IsNull(poolItem) && !poolItem.Key.IsOpen)
            {
                await RemoveConnection(poolItem.Key);
                poolItem = default;
            }

            if (IsNull(poolItem))
            {
                var connFactory = _connConfig.CreateConnectionFactory();
                conn = await connFactory.CreateConnectionAsync();
                _connPool[conn] = new List<IModel>();
            }
            else
            {
                conn = poolItem.Key;
            }

            return conn;
        }

        private async Task RemoveConnection(IConnection conn)
        {
            if (conn == null || !_connPool.ContainsKey(conn)) return;

            var connUsage = _connPool[conn].Aggregate(0, (acc, x) => acc + _channelUsage[x]);
            var canRemove = connUsage == 0;
            if (!canRemove) return;

            _logger?.LogDebug($"[RabbitLight] Removing connection");

            foreach (var channel in _connPool[conn].ToArray())
                await RemoveChannel(channel, conn);

            if (conn.IsOpen) await conn.CloseAsync();
            _connPool.Remove(conn);
            conn.Dispose();
        }

        private async Task<IModel> CreateChannel(int retry = 0)
        {
            try
            {
                _logger?.LogDebug($"[RabbitLight] Creating channel");

                var conn = await GetOrCreateConnection();
                var channel = await conn.CreateModelAsync();

                _connPool[conn].Add(channel);
                _channelUsage[channel] = 0;

                return channel;
            }
            catch (TimeoutException ex)
            {
                if (retry < 3) return await CreateChannel(retry + 1);
                throw ex;
            }
        }

        private async Task RemoveChannel(IModel channel, IConnection conn = null)
        {
            if (channel == null) return;

            conn = conn ?? _connPool.FirstOrDefault(x => x.Value.Contains(channel)).Key;
            if (conn == null)
            {
                _deathRow.Remove(channel);
                return;
            }

            _deletionLock.WaitOrThrow(TimeSpan.FromSeconds(30), _cts.Token);

            try
            {
                var canRemove = _channelUsage[channel] == 0;
                if (!canRemove)
                {
                    _deathRow.Add(channel);
                    return;
                }

                _logger?.LogDebug($"[RabbitLight] Removing channel ({channel.ChannelNumber})");

                if (channel.IsOpen) await channel.CloseAsync();
                _connPool[conn].Remove(channel);
                _channelUsage.Remove(channel);
                _deathRow.Remove(channel);
                channel.Dispose();
            }
            finally
            {
                _deletionLock.Release();
            }
        }

        #endregion
    }
}
