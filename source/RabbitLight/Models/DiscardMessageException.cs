using System;
using System.Runtime.Serialization;

namespace RabbitLight.Models
{
    public class DiscardMessageException : Exception
    {
        public DiscardMessageException() { }

        public DiscardMessageException(string message)
            : base(message) { }

        public DiscardMessageException(string message, Exception innerException)
            : base(message, innerException) { }

        protected DiscardMessageException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }
}
