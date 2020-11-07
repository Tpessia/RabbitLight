using Microsoft.Extensions.Logging;
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
    internal class PublisherConnectionPool : IPublisherConnectionPool
    {
        private readonly ContextConfig _config;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly IServiceProvider _sp;

        // Channel groups
        private readonly Dictionary<IConnection, List<IModel>> _connPool = new Dictionary<IConnection, List<IModel>>(); // connection pool to ensure ratio of channels per connection
        private readonly Dictionary<int, IModel> _threadPool = new Dictionary<int, IModel>(); // ensures that each thread has it's own channel

        // Locks
        private readonly SemaphoreSlim _connLock = new SemaphoreSlim(1); // ensures thread-safe lists

        public PublisherConnectionPool(ContextConfig config, IServiceProvider sp, ILogger<PublisherConnectionPool> logger = null)
        {
            if (config == null)
                throw new ArgumentException("Invalid null value", nameof(config));

            _config = config;
            _sp = sp;
            _logger = logger;

            StartMonitor();
        }

        #region Public

        public async Task<IModel> GetOrCreateChannel()
        {
            await _connLock.WaitOrThrowAsync(TimeSpan.FromSeconds(30), _cts.Token);

            var threadId = Thread.CurrentThread.ManagedThreadId;

            try
            {
                var hasActiveChannel = _threadPool.ContainsKey(threadId) && _threadPool[threadId].IsOpen;
                if (hasActiveChannel)
                    return _threadPool[threadId];
                else
                    return await CreateChannel();
            }
            finally
            {
                _connLock.Release();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();

            foreach (var poolItem in _connPool)
            {
                foreach (var ch in poolItem.Value)
                {
                    try
                    {
                        ch.Close();
                        ch.Dispose();
                    }
                    catch
                    {
                        continue;
                    }
                }

                try
                {
                    var conn = poolItem.Key;
                    conn.Close();
                    conn.Dispose();
                }
                catch
                {
                    continue;
                }
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
                _logger?.LogDebug($"\r\n*** [RabbitLight] Start publisher pool monitor ***");

                _logger?.LogDebug($"[RabbitLight] Number of connections: {_connPool.Count}");
                _logger?.LogDebug($"[RabbitLight] Number of channels: {_threadPool.Count}");

                await DisposeClosedChannels();

                _logger?.LogDebug($"*** [RabbitLight] Stop publisher pool monitor ***\r\n");
            },
            _config.ConnConfig.MonitoringInterval, _config.ConnConfig.MonitoringInterval, _cts.Token,
            ex => Task.Run(() => _logger?.LogError(ex, "[RabbitLight] Error while disposing connections/channels")));
        }

        private async Task DisposeClosedChannels()
        {
            await _connLock.WaitOrThrowAsync(TimeSpan.FromSeconds(30), _cts.Token);

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

                channel.ConfirmSelect();
                channel.BasicNacks += (sender, ea) =>
                {
                    _config.OnPublisherNack(_sp, ea);
                };

                _connPool[conn].Add(channel);

                // The thread id should only be stored after the last await and before the return,
                // to ensure that the thread that gets the channel is the correct owner
                var threadId = Thread.CurrentThread.ManagedThreadId;
                _threadPool[threadId] = channel;

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

            _logger?.LogDebug($"[RabbitLight] Removing channel ({channel.ChannelNumber})");

            if (channel.IsOpen) await channel.CloseAsync();
            _threadPool.RemoveAll(x => x.Value == channel);
            if (conn != null) _connPool[conn].Remove(channel);
            channel.Dispose();
        }

        #endregion
    }
}
