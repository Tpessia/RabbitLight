using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RabbitLight.Publisher
{
    public interface IPublisher : IDisposable
    {
        Task Publish(string exchange, string routingKey, byte[] body, bool mandatory = false, IBasicProperties basicProperties = null);
        Task PublishString(string exchange, string routingKey, string body, bool mandatory = false, IBasicProperties basicProperties = null);
        Task PublishJson<T>(string exchange, string routingKey, T body, bool mandatory = false, IBasicProperties basicProperties = null);
        Task PublishXml<T>(string exchange, string routingKey, T body, bool mandatory = false, IBasicProperties basicProperties = null);
        Task PublishBatch(IEnumerable<PublishBatch> content);
    }
}
