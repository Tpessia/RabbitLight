using Newtonsoft.Json;
using RabbitLight.ConnectionPool;
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

        public async Task<ulong> Publish(string exchange, string routingKey, byte[] body, bool mandatory = false, Action<IBasicProperties> setBasicProperties = null)
        {
            var channel = await _connPool.GetOrCreateChannel();

            var basicProperties = channel.CreateBasicProperties();
            basicProperties.DeliveryMode = 2;
            basicProperties.ContentType = GetContentType(MessageType.Byte);
            setBasicProperties?.Invoke(basicProperties);

            var seqNumber = channel.NextPublishSeqNo;
            channel.BasicPublish(exchange, routingKey, mandatory, basicProperties, body);
            return seqNumber;
        }

        public Task<ulong> PublishString(string exchange, string routingKey, string body, bool mandatory = false, Action<IBasicProperties> setBasicProperties = null) =>
            Publish(exchange, routingKey, ParseMessage(body, MessageType.String), mandatory,
                props => { props.ContentType = GetContentType(MessageType.String); setBasicProperties?.Invoke(props); });

        public Task<ulong> PublishJson<T>(string exchange, string routingKey, T body, bool mandatory = false, Action<IBasicProperties> setBasicProperties = null) =>
            Publish(exchange, routingKey, ParseMessage(body, MessageType.Json), mandatory,
                props => { props.ContentType = GetContentType(MessageType.Json); setBasicProperties?.Invoke(props); });

        public Task<ulong> PublishXml<T>(string exchange, string routingKey, T body, bool mandatory = false, Action<IBasicProperties> setBasicProperties = null) =>
            Publish(exchange, routingKey, ParseMessage(body, MessageType.Xml), mandatory,
                props => { props.ContentType = GetContentType(MessageType.Xml); setBasicProperties?.Invoke(props); });

        public async Task PublishBatch(IEnumerable<PublishBatch> content)
        {
            var channel = await _connPool.GetOrCreateChannel();
            var batch = channel.CreateBasicPublishBatch();

            foreach (var item in content)
            {
                var basicProperties = channel.CreateBasicProperties();

                basicProperties.DeliveryMode = 2;
                basicProperties.ContentType = GetContentType(item.MessageType);

                item.SetBasicProperties?.Invoke(basicProperties);

                var body = ParseMessage(item.Body, item.MessageType);

                RabbitClientNormalizer.PublisherBatchAdd(batch, item, basicProperties, body);
            }

            batch.Publish();
        }

        public void Dispose() => _connPool.Dispose();

        private string XmlSerialize<T>(T obj)
        {
            var serializer = new XmlSerializer(typeof(T));
            using (var sw = new Utf8StringWriter())
            {
                using (var writer = XmlWriter.Create(sw))
                {
                    serializer.Serialize(writer, obj);
                    return sw.ToString();
                }
            }
        }

        private class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
        }

        private string GetContentType(MessageType type)
        {
            var map = new Dictionary<MessageType, string>
            {
                { MessageType.Byte, "application/octet-stream" },
                { MessageType.String, "text/plain" },
                { MessageType.Json, "application/json" },
                { MessageType.Xml, "application/xml" }
            };
            return map[type];
        }

        private byte[] ParseMessage(object message, MessageType type)
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
                    throw new ArgumentException($"Invalid message type \"{type.ToString()}\"");
            }
        }
    }
}
