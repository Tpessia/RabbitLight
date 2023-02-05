using RabbitLight.Api;
using RabbitLight.Publisher;
using System;

namespace RabbitLight.Context
{
    public interface IRabbitLightContext : IDisposable
    {
        IPublisher Publisher { get; }
        IRabbitApi Api { get; }
    }
}