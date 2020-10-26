using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RabbitLight.Publisher
{
    public interface IPublisher : IDisposable
    {
        Task<ulong> Publish(string exchange, string routingKey, byte[] body, bool mandatory = false, IBasicProperties basicProperties = null);
        Task<ulong> PublishString(string exchange, string routingKey, string body, bool mandatory = false, IBasicProperties basicProperties = null);
        Task<ulong> PublishJson<T>(string exchange, string routingKey, T body, bool mandatory = false, IBasicProperties basicProperties = null);
        Task<ulong> PublishXml<T>(string exchange, string routingKey, T body, bool mandatory = false, IBasicProperties basicProperties = null);
        Task PublishBatch(IEnumerable<PublishBatch> content);
    }
}
