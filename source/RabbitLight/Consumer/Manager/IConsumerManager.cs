using System;

namespace RabbitLight.Consumer.Manager
{
    internal interface IConsumerManager : IDisposable
    {
        void Register();
    }
}
