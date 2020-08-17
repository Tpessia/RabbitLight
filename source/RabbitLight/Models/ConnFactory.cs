using RabbitMQ.Client;

namespace RabbitLight.Models
{
    public class ConnFactory : ConnectionFactory
    {
        public int PortApi { get; set; }
    }
}
