using RabbitLight.Consumer;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace RabbitLight.Config
{
    public class ContextConfig
    {
        /// <summary>
        /// RabbitMQ + RabbitLight configuration
        /// </summary>
        public ConnectionConfig ConnConfig { get; set; }
        /// <summary>
        /// List of consumers that inherit from ConsumerBase
        /// </summary>
        public IEnumerable<Type> Consumers { get; set; }
        /// <summary>
        /// Callback called before a consumer is invoked
        /// </summary>
        public Func<IServiceProvider, Type, BasicDeliverEventArgs, Task> OnStart { get; set; }
        /// <summary>
        /// Callback called after a consumer is invoked
        /// </summary>
        public Func<IServiceProvider, Type, BasicDeliverEventArgs, Task> OnEnd { get; set; }
        /// <summary>
        /// Callback called after the ACK message is sent
        /// </summary>
        public Func<IServiceProvider, Type, BasicDeliverEventArgs, Task> OnAck { get; set; }
        /// <summary>
        /// Callback called after a consumer throws an unhandled exception
        /// </summary>
        public Func<IServiceProvider, Exception, Type, BasicDeliverEventArgs, Task<bool>> OnError { get; set; }

        public void Validate()
        {
            if (ConnConfig == null)
                throw new ArgumentException("Invalid null value", nameof(ConnConfig));

            if (Consumers == null)
                throw new ArgumentException("Invalid null value", nameof(Consumers));

            Consumers ??= Assembly.GetEntryAssembly().GetTypes();
            Consumers = Consumers.Where(x => typeof(ConsumerBase).IsAssignableFrom(x)
                && !x.IsInterface && !x.IsAbstract);
        }
    }
}
