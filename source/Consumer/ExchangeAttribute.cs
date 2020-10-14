using System;
using System.Linq;

namespace RabbitLight.Consumer
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ExchangeAttribute : Attribute
    {
        public string Name { get; set; }
        public string ExchangeType { get; set; }

        public ExchangeAttribute(string name, string type = RabbitMQ.Client.ExchangeType.Topic)
        {
            var exchangeTypes = RabbitMQ.Client.ExchangeType.All();
            if (!exchangeTypes.Any(x => x == type))
                throw new Exception($"Exchange \"{name}\" type should be one of: {string.Join(", ", exchangeTypes)}");

            Name = name;
            ExchangeType = type;
        }
    }
}
