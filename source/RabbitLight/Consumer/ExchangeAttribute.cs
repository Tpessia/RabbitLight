using RabbitMQ.Client;
using System;
using System.Linq;

namespace RabbitLight.Consumer
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ExchangeAttribute : Attribute
    {
        public string Name { get; set; }
        public string Type { get; set; }

        public ExchangeAttribute(string name, string type = ExchangeType.Topic)
        {
            var exchangeTypes = ExchangeType.All();
            if (!exchangeTypes.Any(x => x == type))
                throw new Exception($"Exchange \"{name}\" type should be one of: {string.Join(", ", exchangeTypes)}");

            Name = name;
            Type = type;
        }
    }
}
