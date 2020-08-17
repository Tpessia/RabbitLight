using Newtonsoft.Json;
using RabbitMQ.Client.Events;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace RabbitLight.Models
{
    public class BusContext<T>
    {
        public BasicDeliverEventArgs EventArgs { get; set; }
        public string Message => Encoding.UTF8.GetString(EventArgs.Body);

        public BusContext(BasicDeliverEventArgs eventArgs)
        {
            EventArgs = eventArgs;
        }

        public T MessageFromJson() => JsonConvert.DeserializeObject<T>(Message);

        public T MessageFromXml()
        {
            using TextReader reader = new StringReader(Message);
            var serializer = new XmlSerializer(typeof(T));
            return (T)serializer.Deserialize(reader);
        }
    }
}
