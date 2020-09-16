using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitLight.Exceptions;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace RabbitLight.Consumer.Manager
{
    public class ConsumerMetadata
    {
        public Type Type { get; set; }
        public Type ParamType { get; set; }
        public object Instance { get; set; }
        public MethodInfo MethodInfo { get; set; }
        public ExchangeAttribute Exchange { get; set; }
        public QueueAttribute Queue { get; set; }
        public ILogger Logger { get; set; }

        internal async Task Invoke(IServiceProvider serviceProvider, params object[] contextParams)
        {
            // Create Consumer Instance
            var consumer = ActivatorUtilities.CreateInstance(serviceProvider, Type);
            var context = Activator.CreateInstance(ParamType, contextParams);

            var consumerParams = new object[] { context };

            // Run Consumer Method
            var isAwaitable = MethodInfo.ReturnType.GetMethod(nameof(Task.GetAwaiter)) != null;

            if (isAwaitable) await (dynamic)MethodInfo.Invoke(consumer, consumerParams);
            else MethodInfo.Invoke(consumer, consumerParams);
        }
    }
}
