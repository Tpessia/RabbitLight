using Newtonsoft.Json;
using RabbitLight.Exceptions;
using RabbitMQ.Client.Events;
using System;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace RabbitLight.Consumer
{
    public class MessageContext<T>
    {
        public BasicDeliverEventArgs EventArgs { get; set; }

        public byte[] MessageAsBytes()
        {
            // Workaround for RabbitMQ typing inconsistence
            dynamic body = EventArgs.Body;
            if (body is ReadOnlyMemory<byte>)
                return body.ToArray();
            else if (body is byte[])
                return body;
            throw new InvalidCastException("EventArgs.Body is of invalid type");
        }

        public string MessageAsString() => Encoding.UTF8.GetString(MessageAsBytes());

        public MessageContext(BasicDeliverEventArgs eventArgs)
        {
            EventArgs = eventArgs;
        }

        public T MessageFromJson()
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(MessageAsString());
            }
            catch (Exception ex)
            {
                throw new SerializationException("Error while deserializing consumer message to JSON", ex);
            }
        }

        public T MessageFromXml()
        {
            try
            {
                using TextReader reader = new StringReader(MessageAsString());
                var serializer = new XmlSerializer(typeof(T));
                return (T)serializer.Deserialize(reader);
            }
            catch (Exception ex)
            {
                throw new SerializationException("Error while deserializing consumer message to XML", ex);
            }
        }
    }
}
