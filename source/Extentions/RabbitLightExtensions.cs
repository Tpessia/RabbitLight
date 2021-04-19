using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitLight.Config;
using RabbitLight.Context;
using RabbitLight.Host;
using System;
using System.Linq;

namespace RabbitLight.Extensions
{
    public static class RabbitLightExtensions
    {
        #region Services

        public static IServiceCollection AddRabbitLightContext<T>(this IServiceCollection services, Action<ContextConfig> configBuilder) where T : RabbitLightContext
        {
            var contextType = typeof(T);

            var validContext = typeof(RabbitLightContext).IsAssignableFrom(contextType) && !contextType.IsAbstract && !contextType.IsInterface;
            if (!validContext) return services;

            var isRegistered = services.Any(x => x.ServiceType == typeof(T));
            if (isRegistered) return services;

            var config = new ContextConfig();
            configBuilder(config);
            config.Alias = config.Alias ?? contextType.Name;

            services.AddSingleton<T>(sp => (T)Activator.CreateInstance(typeof(T), sp, config));

            if (config.UseHostedService)
                services.AddSingleton<IHostedService>(sp =>
                    new RabbitLightHost((RabbitLightContext)sp.GetService(contextType)));

            return services;
        }

        #endregion

        #region Application

        public static IApplicationBuilder UseRabbitLight<T>(this IApplicationBuilder app) where T : RabbitLightContext
        {
            var context = (RabbitLightContext)app.ApplicationServices.GetService<T>();

            var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStarted.Register(() => context.Register().Wait());
            lifetime.ApplicationStopping.Register(() => context.Dispose());

            return app;
        }

        #endregion
    }
}
