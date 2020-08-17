using RabbitMQ.Client;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace RabbitLight.Helpers
{
    internal interface IConnectionPool : IDisposable
    {
        int TotalChannels { get; }
        Task<int?> GetMessageCount(string queue);
        Task<IConnection> GetOrCreateConnection();
        Task<IModel> CreateUnmanagedChannel();
        Task<IModel> GetOrCreateChannel();
        Task<IModel> CreateConsumerChannel();
        void DeleteChannels(int count);
        void DeleteChannel(IModel channel);
        void DisposeClosedChannels();
    }
}
