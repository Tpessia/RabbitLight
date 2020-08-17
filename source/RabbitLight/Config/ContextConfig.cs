using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
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

        public void Validate()
        {
            if (ConnConfig == null)
                throw new ArgumentException("Invalid null value", nameof(ConnConfig));

            if (Consumers == null)
                throw new ArgumentException("Invalid null value", nameof(Consumers));
        }
    }
}
