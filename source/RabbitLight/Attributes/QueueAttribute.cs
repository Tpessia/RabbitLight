using System;

namespace RabbitLight.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class QueueAttribute : Attribute
    {
        public string Name { get; set; }
        public string RoutingKey { get; set; }

        public QueueAttribute(string name, string routingKey = "*")
        {
            Name = name;
            RoutingKey = routingKey;
        }
    }
}
