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

        public QueueAttribute(string name, params string[] routingKey)
        {
            Name = name;
            RoutingKeys = routingKey.Any() ? routingKey : new[] { "*" };
        }
    }
}
