using System;
using System.ComponentModel;
using System.Text;

namespace RabbitLight.Models
{
    public class ChannelConfig
    {
        /// <summary>
        /// Minimum number of parallel channels
        /// <br>https://www.rabbitmq.com/consumers.html#concurrency</br>
        /// </summary>
        public ushort MinChannels { get; set; }
        /// <summary>
        /// Maximum number of parallel channels
        /// <br>https://www.rabbitmq.com/consumers.html#concurrency</br>
        /// </summary>
        public ushort MaxChannels { get; set; }
        /// <summary>
        /// Number of messages required to scale a new channel (e.g. 1000 messages)
        /// </summary>
        public ushort? ScallingThreshold { get; set; }
        /// <summary>
        /// Number of messages that will be cached by each channel at once.
        /// <br>As RabbitMQ directs the channel's worker to the consumers with Round Robin,</br>
        /// <br>it should be a number smaller enough to prevent blocking other queues for a long time,</br>
        /// <br>and big enough to prevent multipe fetchs to the server (e.g. 10).</br>
        /// </summary>
        public ushort PrefetchCount { get; set; }
        /// <summary>
        /// Number of channels per connection. Should be equal to or smaller than the one configured on the server
        /// </summary>
        public ushort ChannelsPerConnection { get; set; }
        /// <summary>
        /// Delay when Nacking a message for requeue
        /// </summary>
        public TimeSpan RequeueInterval { get; set; }

        public ChannelConfig(ushort? minChannels = null, ushort? maxChannels = null, ushort? scallingThreshold = null,
            ushort? prefetchCount = null, ushort? channelsPerConnection = null, TimeSpan? requeueInterval = null)
        {
            if (minChannels < 1)
                throw new ArgumentException("Should be bigger than 0", nameof(minChannels));

            if (maxChannels < 1)
                throw new ArgumentException("Should be bigger than 0", nameof(maxChannels));
            
            if (minChannels > maxChannels)
                throw new ArgumentException($"{nameof(maxChannels)} should be bigger than {nameof(minChannels)}", nameof(maxChannels));
            
            if (scallingThreshold < 1)
                throw new ArgumentException("Should be bigger than 0", nameof(scallingThreshold));
            
            if (prefetchCount < 0)
                throw new ArgumentException("Should be equal to or bigger than 0", nameof(prefetchCount));
            
            if (channelsPerConnection < 1)
                throw new ArgumentException("Should be bigger than 0", nameof(channelsPerConnection));

            MinChannels = minChannels ?? 10;
            MaxChannels = maxChannels ?? 50;
            ScallingThreshold = scallingThreshold;
            PrefetchCount = prefetchCount ?? 10;
            ChannelsPerConnection = channelsPerConnection ?? 20;
            RequeueInterval = requeueInterval ?? TimeSpan.FromSeconds(5);
        }

        public override string ToString()
        {
            var strBuilder = new StringBuilder();
            foreach (PropertyDescriptor prop in TypeDescriptor.GetProperties(this))
                strBuilder.AppendLine($"{prop.Name}: {prop.GetValue(this)}");
            return strBuilder.ToString().TrimEnd();
        }
    }
}
