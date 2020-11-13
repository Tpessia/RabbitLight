using RabbitLight.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RabbitLight.Consumer
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ExchangeAttribute : Attribute
    {
        public string Name { get; set; }
        public string ExchangeType { get; set; }
        public bool Durable { get; set; }
        public bool AutoDelete { get; set; }
        public IDictionary<string, object> Arguments { get; set; }

        /// <summary>
        /// Rabbit Exchange Declaration
        /// </summary>
        /// <param name="name"></param>
        /// <param name="type"></param>
        /// <param name="durable"></param>
        /// <param name="autoDelete"></param>
        /// <param name="arguments">e.g. "x-dead-letter-exchange: some.exchange.name; x-max-priority: 10; x-example-boolean: true; x-example-list: [1,2]"</param>
        public ExchangeAttribute(string name, string type = RabbitMQ.Client.ExchangeType.Topic, bool durable = true, bool autoDelete = false, string arguments = null)
        {
            var exchangeTypes = RabbitMQ.Client.ExchangeType.All();
            if (!exchangeTypes.Any(x => x == type))
                throw new Exception($"Exchange \"{name}\" type should be one of: {string.Join(", ", exchangeTypes)}");

            Name = name;
            ExchangeType = type;

            Durable = durable;
            AutoDelete = autoDelete;
            Arguments = ArgumentHelper.ParseArguments(arguments);
        }
    }
}
