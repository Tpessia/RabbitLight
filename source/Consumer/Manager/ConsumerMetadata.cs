using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace RabbitLight.Consumer.Manager
{
    internal class ConsumerMetadata : IConsumerMetadata
    {
        public Type Type { get; set; }
        public Type ParamType { get; set; }
        public MethodInfo MethodInfo { get; set; }
        public ExchangeAttribute Exchange { get; set; }
        public QueueAttribute Queue { get; set; }
        public ILogger Logger { get; set; }

        public List<IModel> Channels { get; set; } = new List<IModel>();

        public async Task InvokeConsumer(ConsumerMetadata consumer, IServiceProvider serviceProvider, params object[] contextParams)
        {
            // Create Consumer Instance
            var instance = ActivatorUtilities.CreateInstance(serviceProvider, consumer.Type);
            var context = Activator.CreateInstance(consumer.ParamType, contextParams);

            var consumerParams = new object[] { context };

            // Run Consumer Method
            var isAwaitable = consumer.MethodInfo.ReturnType.GetMethod(nameof(Task.GetAwaiter)) != null;

            try
            {
                if (isAwaitable) await (dynamic)consumer.MethodInfo.Invoke(instance, consumerParams);
                else consumer.MethodInfo.Invoke(instance, consumerParams);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }
    }

    public interface IConsumerMetadata
    {
        Type Type { get; }
        Type ParamType { get; }
        MethodInfo MethodInfo { get; }
        ExchangeAttribute Exchange { get; }
        QueueAttribute Queue { get; }
        ILogger Logger { get; }
    }
}
