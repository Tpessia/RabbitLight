using RabbitMQ.Client;
using System.Threading.Tasks;

namespace RabbitLight.Extensions
{
    internal static class RabbitClientExtensions
    {
        public static Task<IConnection> CreateConnectionAsync(this ConnectionFactory connFactory)
        {
            return Task.Run(() => connFactory.CreateConnection());
        }

        public static Task<IModel> CreateModelAsync(this IConnection conn)
        {
            return Task.Run(() => conn.CreateModel());
        }

        public static Task CloseAsync(this IConnection conn)
        {
            return Task.Run(() => conn.Close());
        }

        public static Task CloseAsync(this IModel channel)
        {
            return Task.Run(() => channel.Close());
        }
    }
}
