using RabbitMQ.Client;
using System;
using System.Threading.Tasks;

namespace RabbitLight.Helpers
{
    internal interface IPublisherConnectionPool : IDisposable
    {
        Task<IModel> GetOrCreateChannel();
    }
}
