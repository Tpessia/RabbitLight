using System;
using System.Runtime.Serialization;

namespace RabbitLight.Exceptions
{
    public class SerializationException : Exception
    {
        public SerializationException() { }

        public SerializationException(string message)
            : base(message) { }

        public SerializationException(string message, Exception innerException)
            : base(message, innerException) { }

        protected SerializationException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }
}
