using RabbitMQ.Client;
using System;
using System.Threading.Tasks;

namespace RabbitLight.Helpers
{
    internal interface IConnectionPool : IDisposable
    {
        int TotalChannels { get; }
        Task<int?> GetMessageCount(string queue);
        Task<IConnection> GetOrCreateConnection();
        Task<IModel> CreateSingleChannel();
        Task<IModel> GetOrCreateChannel();
        Task<IModel> CreateConsumerChannel();
        void DeleteChannels(int count);
        void DeleteChannel(IModel channel);
        void DisposeClosedChannels();
    }
}
