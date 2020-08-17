using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RabbitLight.Config;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight.Helpers
{
    internal class ConnectionPool : IConnectionPool
    {
        private readonly ConnectionConfig _connConfig;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Dictionary<IConnection, List<IModel>> _connPool = new Dictionary<IConnection, List<IModel>>();

        // Locks (to prevent unsynced management)
        private readonly SemaphoreSlim _connLock = new SemaphoreSlim(1);
        private readonly SemaphoreSlim _channelLock = new SemaphoreSlim(1);

        public int TotalChannels => !_connPool.Any() ? 0
            : _connPool.Values.Select(x => x.Count()).Aggregate((a, b) => a + b);

        public ConnectionPool(ConnectionConfig connConfig, ILogger logger = null)
        {
            if (connConfig == null)
                throw new ArgumentException("Invalid null value", nameof(connConfig));

            _connConfig = connConfig;
            _connConfig.DispatchConsumersAsync = true;
            _connConfig.RequestedChannelMax = _connConfig.ChannelsPerConnection;

            Monitoring.Run(() => Task.Run(() => DisposeClosedChannels()),
                _connConfig.MonitoringInterval, _cts.Token,
                ex => Task.Run(() => logger?.LogError(ex, "[RabbitLight] Error while disposing connections/channels")));
        }

        #region Public

        public async Task<IConnection> GetOrCreateConnection()
        {
            await _connLock.WaitAsync();

            try
            {
                IConnection conn;
                var poolItem = _connPool.FirstOrDefault(x => x.Value.Count() < _connConfig.ChannelsPerConnection);

                // Prevent returning a closed connection
                if (!IsNull(poolItem) && !poolItem.Key.IsOpen)
                {
                    RemoveConnection(poolItem.Key);
                    poolItem = default;
                }

                if (IsNull(poolItem))
                {
                    conn = await CreateUnmanagedConnection();
                    _connPool[conn] = new List<IModel>();
                }
                else
                {
                    conn = poolItem.Key;
                }

                return conn;
            }
            finally
            {
                _connLock.Release();
            }
        }

        public async Task<IModel> CreateUnmanagedChannel()
        {
            var conn = await CreateUnmanagedConnection();
            var channel = conn.CreateModel();
            return channel;
        }

        public async Task<IModel> GetOrCreateChannel()
        {
            await _channelLock.WaitAsync();

            try
            {
                var conn = await GetOrCreateConnection();
                var cachedChannel = _connPool[conn].FirstOrDefault();

                // Prevent returning a closed channel
                if (cachedChannel != null && !cachedChannel.IsOpen)
                {
                    RemoveChannel(cachedChannel, conn);
                    cachedChannel = default;
                }

                var channel = cachedChannel != null ? cachedChannel : await CreateChannel();
                return channel;
            }
            finally
            {
                _channelLock.Release();
            }
        }

        public async Task<IModel> CreateConsumerChannel()
        {
            await _channelLock.WaitAsync();

            try
            {
                var channel = await CreateChannel();
                channel.BasicQos(0, _connConfig.PrefetchCount, false);
                return channel;
            }
            finally
            {
                _channelLock.Release();
            }
        }

        public void DeleteChannels(int count)
        {
            _connLock.Wait();
            _channelLock.Wait();

            var emptyPools = new List<IConnection>();

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
                            RemoveChannel(channel, smallestPool.Key);
                        }
                        else
                        {
                            emptyPools.Add(smallestPool.Key);
                        }
                    }
                }
            }
            finally
            {
                _connLock.Release();
                _channelLock.Release();
            }

            for (int i = 0; i < emptyPools.Count(); i++)
            {
                RemoveConnection(emptyPools[0]);
                DeleteChannels(1);
            }
        }

        public void DeleteChannel(IModel channel)
        {
            _connLock.Wait();
            _channelLock.Wait();

            try
            {
                var found = false;
                foreach (var poolItem in _connPool)
                {
                    foreach (var ch in poolItem.Value)
                    {
                        if (channel == ch)
                        {
                            RemoveChannel(channel, poolItem.Key);
                            found = true;
                        }

                        if (found) break;
                    }

                    if (found) break;
                }
            }
            finally
            {
                _connLock.Release();
                _channelLock.Release();
            }
        }

        public async Task<int?> GetMessageCount(string queue)
        {
            var vhost = _connConfig.VirtualHost == "/" ? "%2F" : _connConfig.VirtualHost;
            using var client = CreateHttpClient();
            var request = await client.GetAsync($"queues/{vhost}/{queue}");
            request.EnsureSuccessStatusCode();
            var result = JObject.Parse(await request.Content.ReadAsStringAsync());
            return result["messages"]?.ToObject<int?>();
        }

        public void DisposeClosedChannels()
        {
            _connLock.Wait();
            _channelLock.Wait();

            try
            {
                var deletes = new List<Action>();

                foreach (var poolItem in _connPool)
                {
                    foreach (var ch in poolItem.Value)
                    {
                        if (!ch.IsOpen)
                            deletes.Add(() => RemoveChannel(ch, poolItem.Key));
                    }

                    var conn = poolItem.Key;
                    if (!conn.IsOpen)
                        deletes.Add(() => RemoveConnection(conn));
                }

                for (int i = 0; i < deletes.Count(); i++)
                    deletes[i].Invoke();

            }
            finally
            {
                _connLock.Release();
                _channelLock.Release();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();

            foreach (var poolItem in _connPool)
            {
                foreach (var ch in poolItem.Value)
                {
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

        private async Task<IConnection> CreateUnmanagedConnection()
        {
            await CreateVHostAndConfigs();
            return _connConfig.CreateConnection();
        }

        private async Task CreateVHostAndConfigs()
        {
            if (string.IsNullOrWhiteSpace(_connConfig.VirtualHost) || _connConfig.VirtualHost == "/")
                return;

            using var client = CreateHttpClient();

            // Create Virtual Host
            var vhostReq = await client.PutAsync($"vhosts/{_connConfig.VirtualHost}",
                new StringContent("", Encoding.UTF8, "application/json"));
            vhostReq.EnsureSuccessStatusCode();

            // Set Permissions
            var permissionsReq = await client.PutAsync($"permissions/{_connConfig.VirtualHost}/{_connConfig.UserName}",
                new StringContent("{\"configure\":\".*\",\"write\":\".*\",\"read\":\".*\"}", Encoding.UTF8, "application/json"));
            permissionsReq.EnsureSuccessStatusCode();

            // HA Configuration (Node Mirrowing)
            var haReq = await client.PutAsync($"policies/{_connConfig.VirtualHost}/ha-config",
                new StringContent("{\"pattern\":\".*\",\"definition\":{\"ha-mode\":\"exactly\",\"ha-params\":2,\"ha-sync-mode\":\"automatic\"}}", Encoding.UTF8, "application/json"));
            haReq.EnsureSuccessStatusCode();
        }

        private HttpClient CreateHttpClient()
        {
            var credentials = new NetworkCredential(_connConfig.UserName, _connConfig.Password);
            // TODO: Dispose handler
            var handler = new HttpClientHandler { Credentials = credentials };
            var client = new HttpClient(handler);
            client.BaseAddress = new Uri($"http://{_connConfig.HostName}:{_connConfig.PortApi}/api/");
            return client;
        }

        private bool IsNull<T>(T obj) => obj.Equals(default(T));

        private async Task<IModel> CreateChannel()
        {
            var conn = await GetOrCreateConnection();
            var channel = conn.CreateModel();
            _connPool[conn].Add(channel);
            return channel;
        }

        private void RemoveChannel(IModel channel, IConnection conn)
        {
            if (channel.IsOpen) channel.Close();
            _connPool[conn].Remove(channel);
            channel.Dispose();
        }

        private void RemoveConnection(IConnection conn)
        {
            if (conn.IsOpen) conn.Close();
            _connPool.Remove(conn);
            conn.Dispose();
        }

        #endregion
    }
}
