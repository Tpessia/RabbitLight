using RabbitMQ.Client;
using System;
using System.Threading.Tasks;

namespace RabbitLight.ConnectionPool
{
    internal interface IPublisherConnectionPool : IDisposable
    {
        Task<IModel> GetOrCreateChannel();
    }
}
