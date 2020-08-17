using Newtonsoft.Json;
using RabbitMQ.Client.Events;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace RabbitLight.Consumer
{
    public class MessageContext<T>
    {
        public BasicDeliverEventArgs EventArgs { get; set; }
        public byte[] MessageAsBytes => EventArgs.Body;
        public string MessageAsString => Encoding.UTF8.GetString(MessageAsBytes);

        public MessageContext(BasicDeliverEventArgs eventArgs)
        {
            EventArgs = eventArgs;
        }

        public T MessageFromJson() => JsonConvert.DeserializeObject<T>(MessageAsString);

        public T MessageFromXml()
        {
            using TextReader reader = new StringReader(MessageAsString);
            var serializer = new XmlSerializer(typeof(T));
            return (T)serializer.Deserialize(reader);
        }
    }
}
