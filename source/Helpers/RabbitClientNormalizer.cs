using RabbitLight.Publisher;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Linq;
using System.Reflection;

namespace RabbitLight.Helpers
{
    internal static class RabbitClientNormalizer
    {
        public static byte[] EventArgsGetMessageBytes(BasicDeliverEventArgs ea)
        {
            var bodyProp = ea.GetType().GetProperty("Body");

            if (bodyProp == null)
                throw new InvalidOperationException($"[RabbitLight] Property \"Body\" not found on {nameof(BasicDeliverEventArgs)}, ensure a valid RabbitMQ.Client Nuget version is installed");

            var bodyValue = bodyProp.GetValue(ea);
            var toArrayMethod = GetMethod(bodyProp.PropertyType, "ToArray");
            var byteType = typeof(BasicDeliverEventArgs).GetProperty("Body").PropertyType;

            byte[] msg;

            if (typeof(byte[]).IsAssignableFrom(byteType)) // Older clients
                msg = bodyValue as byte[];
            else if (typeof(ReadOnlyMemory<byte>).IsAssignableFrom(byteType)) // Newer clients
                msg = toArrayMethod.Invoke(bodyValue, null) as byte[];
            else
                throw new InvalidOperationException($"[RabbitLight] Invalid {nameof(BasicDeliverEventArgs)}.Body type, expected byte[] or ReadOnlyMemory<byte>, ensure a valid RabbitMQ.Client Nuget version is installed");

            return msg;
        }

        public static void PublisherBatchAdd(IBasicPublishBatch publishBatch, PublishBatch batchData, IBasicProperties basicProperties, byte[] body)
        {
            var byteAddMethod = GetMethod(publishBatch.GetType(), "Add",
                typeof(string), typeof(string), typeof(bool), typeof(IBasicProperties), typeof(byte[]));
            var memoryAddMethod = GetMethod(publishBatch.GetType(), "Add",
                typeof(string), typeof(string), typeof(bool), typeof(IBasicProperties), typeof(ReadOnlyMemory<byte>));

            var byteType = typeof(IModel).GetMethods().FirstOrDefault(x => x.Name == "BasicPublish")?.GetParameters()?.ElementAtOrDefault(4)?.ParameterType;
            if (byteType == null)
                throw new InvalidOperationException($"[RabbitLight] Method \"BasicPublish\" not found on {nameof(IModel)}, ensure a valid RabbitMQ.Client Nuget version is installed");

            if (typeof(byte[]).IsAssignableFrom(byteType)) // Older clients
                byteAddMethod.Invoke(publishBatch, new object[] { batchData.Exchange, batchData.RoutingKey, batchData.Mandatory, basicProperties, body });
            else if (typeof(ReadOnlyMemory<byte>).IsAssignableFrom(byteType)) // Newer clients
                memoryAddMethod.Invoke(publishBatch, new object[] { batchData.Exchange, batchData.RoutingKey, batchData.Mandatory, basicProperties, new ReadOnlyMemory<byte>(body) });
            else
                throw new InvalidOperationException($"[RabbitLight] Invalid {nameof(BasicDeliverEventArgs)}.Body type, expected byte[] or ReadOnlyMemory<byte>, ensure a valid RabbitMQ.Client Nuget version is installed");
        }

        private static MethodInfo GetMethod(Type type, string method, params Type[] parameters)
        {
            return type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, parameters, null);
        }
    }
}
