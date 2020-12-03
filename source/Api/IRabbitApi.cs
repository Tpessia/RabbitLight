using RabbitMQ.Client;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RabbitLight.Api
{
    public interface IRabbitApi
    {
        Task CreateExchange(string exchange, string type = ExchangeType.Topic, bool durable = true, bool autoDelete = false, IDictionary<string, object> arguments = null);
        Task CreateQueue(string queue, bool durable = true, bool exclusive = false, bool autoDelete = false, IDictionary<string, object> arguments = null);
        Task CreateBind(string queue, string exchange, string routingKey = "#", IDictionary<string, object> arguments = null);
    }
}
