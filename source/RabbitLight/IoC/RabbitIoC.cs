using RabbitLight.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace RabbitLight.IoC
{
    public static class RabbitIoC
    {
        // The simplest way to store a long-living object
        private static RabbitClient _client { get; set; }

        public static IServiceCollection AddRabbitLight(this IServiceCollection services,
            IConfiguration config, IEnumerable<Assembly> assemblies = null, ChannelConfig channelConfig = null,
            Func<Type, IServiceProvider, Task> onStart = null, Func<Type, IServiceProvider, Task> onEnd = null)
        {
            ConnFactory connFactory = new ConnFactory();
            config.Bind(connFactory);

            // Register Publisher (is disposed with the DI container)
            // https://stackoverflow.com/questions/40844151/when-are-net-core-dependency-injected-instances-disposed
            var conn = RabbitConnectionManager.CreateConnection(connFactory).Result;
            services.AddSingleton<IConnection>(conn);

            // Register Consumer
            services.AddSingleton<RabbitClient>(sp => new RabbitClient(sp, connFactory, assemblies, channelConfig, onStart, onEnd));

            return services;
        }

        public static IApplicationBuilder UseRabbitLight(this IApplicationBuilder app)
        {
            _client = app.ApplicationServices.GetRequiredService<RabbitClient>();

            var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStarted.Register(OnStarted);
            lifetime.ApplicationStopping.Register(OnStopping);

            return app;
        }

        public static void OnStarted() => _client.Register();
        public static void OnStopping() => _client.Dispose();
    }
}
