using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;

namespace RabbitLight.Helpers
{
    internal static class RabbitClientNormalizer
    {
        public static Type GetEventArgsByteType()
        {
            return typeof(BasicDeliverEventArgs).GetProperty("Body").PropertyType;
        }

        public static Type GetPublisherBatchByteType()
        {
            return typeof(IModel).GetMethod("BasicPublish").GetParameters()[4].ParameterType;
        }

        public static void NormalizeEventArgs(Action caseByte, Action caseReadOnlyMemory)
        {
#pragma warning disable CS0184
            var byteType = GetEventArgsByteType();
            if (typeof(byte[]).IsAssignableFrom(byteType))
                caseByte();
            else if (typeof(ReadOnlyMemory<byte>).IsAssignableFrom(byteType))
                caseReadOnlyMemory();
            else
                throw new InvalidOperationException($"[RabbitLight] Invalid {nameof(BasicDeliverEventArgs)}.Body type, expected byte[] or ReadOnlyMemory<byte>");
#pragma warning restore CS0184
        }

        public static void NormalizePublisherBatch(Action caseByte, Action caseReadOnlyMemory)
        {
#pragma warning disable CS0184
            var byteType = GetPublisherBatchByteType();
            if (typeof(byte[]).IsAssignableFrom(byteType))
                caseByte();
            else if (typeof(ReadOnlyMemory<byte>).IsAssignableFrom(byteType))
                caseReadOnlyMemory();
            else
                throw new InvalidOperationException($"[RabbitLight] Invalid {nameof(IBasicPublishBatch)}.Add param type, expected byte[] or ReadOnlyMemory<byte>");
#pragma warning restore CS0184
        }
    }
}
