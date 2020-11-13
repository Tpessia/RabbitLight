using RabbitLight.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RabbitLight.Consumer
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class QueueAttribute : Attribute
    {
        public string Name { get; set; }
        public IEnumerable<string> RoutingKeys { get; set; }
        public bool Durable { get; set; }
        public bool Exclusive { get; set; }
        public bool AutoDelete { get; set; }
        public IDictionary<string, object> Arguments { get; set; }
        public IDictionary<string, object> BindingArguments { get; set; }

        /// <summary>
        /// Rabbit Queue Declaration
        /// </summary>
        /// <param name="name"></param>
        /// <param name="routingKeys"></param>
        public QueueAttribute(string name, params string[] routingKeys)
        {
            Name = name;
            RoutingKeys = routingKeys.Any() ? routingKeys : new[] { "*" };

            Durable = true;
            Exclusive = false;
            AutoDelete = false;
            Arguments = null;
        }

        /// <summary>
        /// Rabbit Queue Declaration
        /// </summary>
        /// <param name="name"></param>
        /// <param name="durable"></param>
        /// <param name="exclusive"></param>
        /// <param name="autoDelete"></param>
        /// <param name="arguments">e.g. "x-dead-letter-exchange: some.exchange.name; x-max-priority: 10; x-example-boolean: true; x-example-list: [1,2]"</param>
        /// <param name="bindingArguments">e.g. "x-match: all, x-example-number: 10, x-example-boolean: true, x-example-list: [1,2]"</param>
        /// <param name="routingKeys"></param>
        public QueueAttribute(string name, bool durable = true, bool exclusive = false, bool autoDelete = false, string arguments = null, string bindingArguments = null, params string[] routingKeys)
            : this(name, routingKeys)
        {
            Durable = durable;
            Exclusive = exclusive;
            AutoDelete = autoDelete;
            Arguments = ArgumentHelper.ParseArguments(arguments);
            BindingArguments = ArgumentHelper.ParseArguments(bindingArguments);
        }
    }
}
