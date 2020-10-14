using Newtonsoft.Json.Linq;
using RabbitLight.Config;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace RabbitLight.Helpers
{
    internal static class RabbitHttpClient
    {
        public static async Task<int?> GetMessageCount(ConnectionConfig connConfig, string queue)
        {
            var vhost = connConfig.VirtualHost == "/" ? "%2F" : connConfig.VirtualHost;
            using (var client = CreateHttpClient(connConfig))
            {
                var request = await client.GetAsync($"queues/{vhost}/{queue}");
                request.EnsureSuccessStatusCode();
                var result = JObject.Parse(await request.Content.ReadAsStringAsync());
                return result["messages"]?.ToObject<int?>();
            }
        }

        public static async Task CreateVHostAndConfigs(ConnectionConfig connConfig)
        {
            if (string.IsNullOrWhiteSpace(connConfig.VirtualHost) || connConfig.VirtualHost == "/")
                return;

            using (var client = CreateHttpClient(connConfig))
            {
                // Create Virtual Host
                var vhostReq = await client.PutAsync($"vhosts/{connConfig.VirtualHost}",
                    new StringContent("", Encoding.UTF8, "application/json"));
                vhostReq.EnsureSuccessStatusCode();

                // Set Permissions
                var permissionsReq = await client.PutAsync($"permissions/{connConfig.VirtualHost}/{connConfig.UserName}",
                    new StringContent("{\"configure\":\".*\",\"write\":\".*\",\"read\":\".*\"}", Encoding.UTF8, "application/json"));
                permissionsReq.EnsureSuccessStatusCode();

                // HA Configuration (Node Mirrowing)
                var haReq = await client.PutAsync($"policies/{connConfig.VirtualHost}/ha-config",
                    new StringContent("{\"pattern\":\".*\",\"definition\":{\"ha-mode\":\"exactly\",\"ha-params\":2,\"ha-sync-mode\":\"automatic\"}}", Encoding.UTF8, "application/json"));
                haReq.EnsureSuccessStatusCode();
            } 

        }

        private static HttpClient CreateHttpClient(ConnectionConfig connConfig)
        {
            var credentials = new NetworkCredential(connConfig.UserName, connConfig.Password);
            // TODO: Dispose handler
            var handler = new HttpClientHandler { Credentials = credentials };
            var client = new HttpClient(handler);
            client.BaseAddress = new Uri($"http://{connConfig.HostName}:{connConfig.PortApi}/api/");
            return client;
        }
    }
}
