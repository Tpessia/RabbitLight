using Newtonsoft.Json;
using RabbitLight.Helpers;
using RabbitMQ.Client;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace RabbitLight.Publisher
{
    internal class Publisher : IPublisher
    {
        private readonly IConnectionPool _connManager;

        public Publisher(IConnectionPool connManager)
        {
            if (connManager == null)
                throw new ArgumentException("Invalid null value", nameof(connManager));

            _connManager = connManager;
        }

        public async Task Publish(string exchange, string routingKey, byte[] body, bool mandatory = false, IBasicProperties basicProperties = null)
        {
            // TODO: publishing on same channel vs different channels
            var channel = await _connManager.GetOrCreateChannel();
            channel.BasicPublish(exchange, routingKey, mandatory, basicProperties, body);
        }

        public Task PublishString(string exchange, string routingKey, string body, bool mandatory = false, IBasicProperties basicProperties = null) =>
            Publish(exchange, routingKey, Encoding.UTF8.GetBytes(body), mandatory, basicProperties);

        public Task PublishJson<T>(string exchange, string routingKey, T body, bool mandatory = false, IBasicProperties basicProperties = null) =>
            PublishString(exchange, routingKey, JsonConvert.SerializeObject(body), mandatory, basicProperties);

        public Task PublishXml<T>(string exchange, string routingKey, T body, bool mandatory = false, IBasicProperties basicProperties = null) =>
            PublishString(exchange, routingKey, XmlSerialize(body), mandatory, basicProperties);

        // TODO: publish batch
        //public async Task PublishBatch(string exchange, string routingKey, bool mandatory, IBasicProperties basicProperties, byte[] body)
        //{
        //    var channel = await _connManager.GetOrCreateChannel();
        //    channel.BasicPublish(exchange, routingKey, mandatory, basicProperties, body);
        //}

        public void Dispose() => _connManager.Dispose();

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
    }
}
