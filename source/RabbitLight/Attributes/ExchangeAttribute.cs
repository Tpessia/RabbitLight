using System;

namespace RabbitLight.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ExchangeAttribute : Attribute
    {
        public string Name { get; set; }

        public ExchangeAttribute(string name)
        {
            Name = name;
        }
    }
}
