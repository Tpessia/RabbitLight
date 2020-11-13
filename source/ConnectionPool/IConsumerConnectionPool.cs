using RabbitMQ.Client;
using System;
using System.Threading.Tasks;

namespace RabbitLight.ConnectionPool
{
    internal interface IConsumerConnectionPool : IDisposable
    {
        int TotalChannels { get; }
        Task<IModel> CreateUnmanagedChannel();
        Task<IModel> CreateNewChannel();
        Task DeleteChannels(int count);
        bool NotifyConsumerStart(IModel channel);
        void NotifyConsumerEnd(IModel channel);
    }
}
