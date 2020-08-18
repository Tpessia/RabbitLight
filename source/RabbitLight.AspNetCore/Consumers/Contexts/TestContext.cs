using RabbitLight.Config;
using RabbitLight.Context;
using System;

namespace RabbitLight.AspNetCore.Consumers.Context
{
    public class TestContext : RabbitLightContext
    {
        public TestContext(IServiceProvider sp, ContextConfig config) : base(sp, config)
        {
        }
    }
}
