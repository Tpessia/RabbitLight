using Newtonsoft.Json;
using RabbitLight.Exceptions;
using RabbitLight.Helpers;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace RabbitLight.Consumer
{
    public class MessageContext<T>
    {
        public BasicDeliverEventArgs EventArgs { get; set; }

        public MessageContext(BasicDeliverEventArgs eventArgs)
        {
            EventArgs = eventArgs;
        }

        public bool IsBytes() => EventArgs.IsBytes();
        public bool IsString() => EventArgs.IsString();
        public bool IsJson() => EventArgs.IsJson();
        public bool IsXml() => EventArgs.IsXml();

        public byte[] MessageBytes() => EventArgs.MessageBytes();
        public string MessageString() => EventArgs.MessageString();
        public T MessageJson() => EventArgs.MessageJson<T>();
        public T MessageXml() => EventArgs.MessageXml<T>();

        /// <summary>
        /// Automatically chooses a parser based on the Content Type Header
        /// </summary>
        public T Message() => EventArgs.Message<T>();

        public Dictionary<string, string> Headers() => EventArgs.Headers();
    }

    public static class MessageContextExtensions
    {
        public static bool IsBytes(this BasicDeliverEventArgs ea) =>
            ea.BasicProperties.ContentType == "application/octet-stream";

        public static bool IsString(this BasicDeliverEventArgs ea) =>
            ea.BasicProperties.ContentType == "text/plain";

        public static bool IsJson(this BasicDeliverEventArgs ea) =>
            ea.BasicProperties.ContentType == "application/json";

        public static bool IsXml(this BasicDeliverEventArgs ea) =>
            ea.BasicProperties.ContentType == "application/xml";

        public static byte[] MessageBytes(this BasicDeliverEventArgs ea)
        {
            return RabbitClientNormalizer.EventArgsGetMessageBytes(ea);
        }

        public static string MessageString(this BasicDeliverEventArgs ea)
        {
            return Encoding.UTF8.GetString(ea.MessageBytes());
        }

        public static T MessageJson<T>(this BasicDeliverEventArgs ea)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(ea.MessageString());
            }
            catch (Exception ex)
            {
                throw new SerializationException("Error while deserializing consumer message to JSON", ex);
            }
        }

        public static T MessageXml<T>(this BasicDeliverEventArgs ea)
        {
            try
            {
                using (TextReader reader = new StringReader(ea.MessageString()))
                {
                    var serializer = new XmlSerializer(typeof(T));
                    return (T)serializer.Deserialize(reader);
                }
            }
            catch (Exception ex)
            {
                throw new SerializationException("Error while deserializing consumer message to XML", ex);
            }
        }

        /// <summary>
        /// Automatically chooses a parser based on the Content Type Header
        /// </summary>
        public static T Message<T>(this BasicDeliverEventArgs ea)
        {
            if (ea.IsBytes() && typeof(byte[]).IsAssignableFrom(typeof(T)))
                return (dynamic)ea.MessageBytes();
            else if (ea.IsString() && typeof(string).IsAssignableFrom(typeof(T)))
                return (dynamic)ea.MessageString();
            else if (ea.IsJson())
                return ea.MessageJson<T>();
            else if (ea.IsXml())
                return ea.MessageXml<T>();
            else
                throw new InvalidOperationException($"[RabbitLight] Unable to automatically parse the message, try using a specific parser (MessageContext.{nameof(MessageBytes)}, MessageContext.{nameof(MessageString)}, MessageContext.{nameof(MessageJson)}, MessageContext.{nameof(MessageXml)})");
        }

        public static Dictionary<string, string> Headers(this BasicDeliverEventArgs ea)
        {
            var headers = new Dictionary<string, string>();

            if (ea.BasicProperties.Headers == null) return headers;

            foreach (var header in ea.BasicProperties.Headers)
                headers.Add(header.Key, Encoding.UTF8.GetString((byte[])header.Value));

            return headers;
        }
    }
}
