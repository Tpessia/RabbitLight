using RabbitMQ.Client;

namespace RabbitLight.Publisher
{
    public class PublishBatch
    {
        /// <summary>
        /// The destination exchange
        /// </summary>
        public string Exchange { get; set; }
        /// <summary>
        /// The routing key binding the exchange to the queues
        /// </summary>
        public string RoutingKey { get; set; }
        /// <summary>
        /// The type of the message
        /// </summary>
        public MessageType MessageType { get; set; }
        /// <summary>
        /// The message body
        /// </summary>
        public object Body { get; set; }
        /// <summary>
        /// Ensure the message must be routable (there should be a queue listening to the routing key)
        /// </summary>
        public bool Mandatory { get; set; }
        /// <summary>
        /// Common AMQP Basic content-class headers interface
        /// </summary>
        public IBasicProperties BasicProperties { get; set; }

        public PublishBatch(string exchange, string routingKey, MessageType messageType, object body, bool mandatory = false, IBasicProperties basicProperties = null)
        {
            MessageType = messageType;
            Exchange = exchange;
            RoutingKey = routingKey;
            Body = body;
            Mandatory = mandatory;
            BasicProperties = basicProperties;
        }
    }
}
