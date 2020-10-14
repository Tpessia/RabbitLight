using System;
using System.Threading.Tasks;

namespace RabbitLight.Consumer.Manager
{
    internal interface IConsumerManager : IDisposable
    {
        Task Register();
    }
}
