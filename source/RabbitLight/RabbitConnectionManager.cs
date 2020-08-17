using RabbitLight.Models;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight
{
    public class RabbitConnectionManager : IDisposable
    {
        private readonly ConnFactory _connFactory;
        private readonly ChannelConfig _channelConfig;
        private readonly SemaphoreSlim _connLock = new SemaphoreSlim(1);
        private readonly Dictionary<IConnection, List<IModel>> _connPool = new Dictionary<IConnection, List<IModel>>();

        public int TotalChannels => !_connPool.Any() ? 0
            : _connPool.Values.Select(x => x.Count()).Aggregate((a, b) => a + b);

        public RabbitConnectionManager(ConnFactory connFactory, ChannelConfig channelConfig)
        {
            _connFactory = connFactory;
            _channelConfig = channelConfig ?? new ChannelConfig();
        }

        #region Static

        public static async Task<IConnection> CreateConnection(ConnFactory connFactory)
        {
            await EnsureVirtualHostExists(connFactory);
            return connFactory.CreateConnection();
        }

        public static async Task EnsureVirtualHostExists(ConnFactory connFactory)
        {
            if (string.IsNullOrWhiteSpace(connFactory.VirtualHost) || connFactory.VirtualHost == "/")
                return;

            using var client = CreateHttpClient(connFactory);

            // Create Virtual Host
            var vhostReq = await client.PutAsync($"vhost/{connFactory.VirtualHost}",
                new StringContent("", Encoding.UTF8, "application/json"));
            vhostReq.EnsureSuccessStatusCode();

            // Set Permissions
            var permissionsReq = await client.PutAsync($"permissions/{connFactory.VirtualHost}/{connFactory.UserName}",
                new StringContent("{\"configure\":\".*\",\"write\":\".*\",\"read\":\".*\"}", Encoding.UTF8, "application/json"));
            permissionsReq.EnsureSuccessStatusCode();

            // HA Configuration (Node Mirrowing)
            var haReq = await client.PutAsync($"policies/{connFactory.VirtualHost}/ha-config",
                new StringContent("{\"pattern\":\".*\",\"definition\":{\"ha-mode\":\"exactly\",\"ha-params\":2,\"ha-sync-mode\":\"automatic\"}}", Encoding.UTF8, "application/json"));
            haReq.EnsureSuccessStatusCode();
        }

        public static HttpClient CreateHttpClient(ConnFactory connFactory)
        {
            var credentials = new NetworkCredential(connFactory.UserName, connFactory.Password);
            // TODO: Dispose handler
            var handler = new HttpClientHandler { Credentials = credentials };
            var client = new HttpClient(handler);
            client.BaseAddress = new Uri($"http://{connFactory.HostName}:{connFactory.PortApi}/api/");
            return client;
        }

        #endregion

        #region Public

        public async Task<IConnection> GetOrCreateConnection()
        {
            await _connLock.WaitAsync();

            try
            {
                IConnection conn;
                var poolItem = _connPool.FirstOrDefault(x => x.Value.Count() < _channelConfig.ChannelsPerConnection);

                if (IsNull(poolItem))
                {
                    conn = await CreateConnection(_connFactory);
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

        public async Task<IModel> CreateSingleChannel()
        {
            var conn = await CreateConnection(_connFactory);
            var channel = conn.CreateModel();
            return channel;
        }

        public async Task<IModel> CreateChannel()
        {
            var conn = await GetOrCreateConnection();
            var channel = conn.CreateModel();
            channel.BasicQos(0, _channelConfig.PrefetchCount, false);
            _connPool[conn].Add(channel);
            return channel;
        }

        public void DeleteChannels(int count)
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
                        RemoveChannel(channel, smallestPool.Value);
                    }
                    else
                    {
                        RemoveConnection(smallestPool.Key);
                        DeleteChannels(1);
                    }
                }
            }
        }

        public void DeleteChannel(IModel channel)
        {
            var found = false;
            foreach (var poolItem in _connPool)
            {
                foreach (var ch in poolItem.Value)
                {
                    if (channel == ch)
                    {
                        RemoveChannel(channel, poolItem.Value);
                        found = true;
                    }

                    if (found) break;
                }

                if (found) break;
            }
        }

        public void DisposeClosedChannels()
        {
            foreach (var poolItem in _connPool)
            {
                foreach (var ch in poolItem.Value)
                    if (!ch.IsOpen)
                        RemoveChannel(ch, poolItem.Value);

                var conn = poolItem.Key;
                if (!conn.IsOpen)
                    RemoveConnection(conn);
            }
        }

        public void Dispose()
        {
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

        private bool IsNull<T>(T obj) => obj.Equals(default(T));

        private void RemoveChannel(IModel channel, List<IModel> poolItem)
        {
            if (channel.IsOpen) channel.Close();
            poolItem.Remove(channel);
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
