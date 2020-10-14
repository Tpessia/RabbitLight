using Newtonsoft.Json;
using RabbitLight.Helpers;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace RabbitLight.Publisher
{
    internal class Publisher : IPublisher
    {
        private readonly IPublisherConnectionPool _connPool;

        public Publisher(IPublisherConnectionPool connPool)
        {
            if (connPool == null)
                throw new ArgumentException("Invalid null value", nameof(connPool));

            _connPool = connPool;
        }

        public async Task Publish(string exchange, string routingKey, byte[] body, bool mandatory = false, IBasicProperties basicProperties = null)
        {
            var channel = await _connPool.GetOrCreateChannel();
            channel.BasicPublish(exchange, routingKey, mandatory, basicProperties, body);
        }

        public Task PublishString(string exchange, string routingKey, string body, bool mandatory = false, IBasicProperties basicProperties = null) =>
            Publish(exchange, routingKey, Encoding.UTF8.GetBytes(body), mandatory, basicProperties);

        public Task PublishJson<T>(string exchange, string routingKey, T body, bool mandatory = false, IBasicProperties basicProperties = null) =>
            PublishString(exchange, routingKey, JsonConvert.SerializeObject(body), mandatory, basicProperties);

        public Task PublishXml<T>(string exchange, string routingKey, T body, bool mandatory = false, IBasicProperties basicProperties = null) =>
            PublishString(exchange, routingKey, XmlSerialize(body), mandatory, basicProperties);

        public async Task PublishBatch(IEnumerable<PublishBatch> content)
        {
            var channel = await _connPool.GetOrCreateChannel();
            var batch = channel.CreateBasicPublishBatch();

            foreach (var item in content)
            {
                var body = ParseMessage(item.Body, item.MessageType);
                batch.Add(item.Exchange, item.RoutingKey, item.Mandatory, item.BasicProperties, body);
            }

            batch.Publish();
        }

        public void Dispose() => _connPool.Dispose();

        private string XmlSerialize<T>(T obj)
        {
            var serializer = new XmlSerializer(typeof(T));
            using (var sw = new StringWriter())
            {
                using (var writer = XmlWriter.Create(sw))
                {
                    serializer.Serialize(writer, obj);
                    return sw.ToString();
                }
            }
        }

        private ReadOnlyMemory<byte> ParseMessage(object message, MessageType type)
        {
            switch (type)
            {
                case MessageType.Byte:
                    if (message is byte[]) return (byte[])message;
                    else throw new ArgumentException("Message should be of type byte[]");
                case MessageType.String:
                    if (message is string) return Encoding.UTF8.GetBytes((string)message);
                    else throw new ArgumentException("Message should be of type string");
                case MessageType.Json:
                    return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
                case MessageType.Xml:
                    return Encoding.UTF8.GetBytes(XmlSerialize(message));
                default:
                    throw new ArgumentException($"Invalid message type {type.ToString()}");
            }
        }
    }
}
