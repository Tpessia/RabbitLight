﻿using Microsoft.Extensions.Logging;
using RabbitLight.Config;
using RabbitLight.Extensions;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight.Helpers
{
    internal class ConsumerConnectionPool : IConsumerConnectionPool
    {
        private readonly ContextConfig _config;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        // Channel groups
        private readonly Dictionary<IConnection, List<IModel>> _connPool = new Dictionary<IConnection, List<IModel>>(); // connection pool to ensure ratio of channels per connection
        private readonly Dictionary<IModel, int> _channelUsage = new Dictionary<IModel, int>(); // consumer usage counter, used for death row logic
        private readonly HashSet<IModel> _deathRow = new HashSet<IModel>(); // channels that were in use during removal attempt

        // Locks (to prevent unsynced management)
        private readonly SemaphoreSlim _connLock = new SemaphoreSlim(1); // ensures thread-safe lists
        private readonly SemaphoreSlim _deletionLock = new SemaphoreSlim(1); // ensures a consumer won't use a death row channel

        public int TotalChannels => !_connPool.Any() ? 0
            : _connPool.Values.Select(x => x.Count).Aggregate((a, b) => a + b) - _deathRow.Count;

        public ConsumerConnectionPool(ContextConfig config, ILogger<ConsumerConnectionPool> logger = null)
        {
            if (config == null)
                throw new ArgumentException("Invalid null value", nameof(config));

            _config = config;
            _logger = logger;

            StartMonitor();
        }

        #region Public

        public async Task<IModel> CreateUnmanagedChannel()
        {
            var connFactory = _config.ConnConfig.CreateConnectionFactory();
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
                channel.BasicQos(0, _config.ConnConfig.PrefetchCount, false);

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
            _deletionLock.Wait(_cts.Token);

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
            _config.ConnConfig.MonitoringInterval, _config.ConnConfig.MonitoringInterval, _cts.Token,
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

        private async Task<IConnection> GetOrCreateConnection(bool forceCreation = false)
        {
            IConnection conn;
            var poolItem = _connPool.LastOrDefault(x => x.Value.Count() < _config.ConnConfig.ChannelsPerConnection);

            // Prevent returning a closed connection
            if (!IsNull(poolItem) && !poolItem.Key.IsOpen)
            {
                await RemoveConnection(poolItem.Key);
                poolItem = default;
            }

            if (IsNull(poolItem) || forceCreation)
            {
                var connFactory = _config.ConnConfig.CreateConnectionFactory();
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

                IConnection conn;
                IModel channel;

                try
                {
                    conn = await GetOrCreateConnection();
                    channel = await conn.CreateModelAsync();
                }
                catch (ChannelAllocationException)
                {
                    // TODO: workaround, there should be a reason to why the channel count is unsynced
                    conn = await GetOrCreateConnection(forceCreation: true);
                    channel = await conn.CreateModelAsync();
                }

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
                if (channel.IsOpen) await channel.CloseAsync();
                _channelUsage.Remove(channel);
                _deathRow.Remove(channel);
                channel.Dispose();
                return;
            }

            await _deletionLock.WaitOrThrowAsync(TimeSpan.FromSeconds(30), _cts.Token);

            try
            {
                var canRemove = _channelUsage[channel] == 0 || !channel.IsOpen;
                if (!canRemove)
                {
                    _logger?.LogDebug($"[RabbitLight] Unable to remove channel ({conn.ToString()} -> {channel.ChannelNumber})");

                    if (!_deathRow.Contains(channel))
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
