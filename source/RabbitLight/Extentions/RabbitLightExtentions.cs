using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitLight.Config;
using RabbitLight.Context;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RabbitLight.Extentions
{
    public static class RabbitLightExtentions
    {
        // The simplest way to store a long-living object
        private static List<RabbitLightContext> _contextList = new List<RabbitLightContext>();

        // TODO: Work around to get all registered contexts
        private static List<Type> _contextTypes = new List<Type>();
        private static List<Type> _registeredContexts => _contextTypes.Where(x => typeof(RabbitLightContext).IsAssignableFrom(x)
            && !x.IsAbstract && !x.IsInterface).Distinct().ToList();

        #region Services

        public static IServiceCollection AddRabbitLightContext<T>(this IServiceCollection services, Action<ContextConfig> configBuilder) where T : RabbitLightContext
        {
            var isRegistered = services.Any(x => x.ServiceType == typeof(T));
            if (isRegistered) return services;

            var config = new ContextConfig();
            configBuilder(config);
            services.AddSingleton<T>(sp => (T)Activator.CreateInstance(typeof(T), sp, config));
            _contextTypes.Add(typeof(T));

            return services;
        }

        #endregion

        #region Application

        public static IApplicationBuilder UseRabbitLight(this IApplicationBuilder app)
        {
            foreach (var type in _registeredContexts)
                _contextList.Add((RabbitLightContext)app.ApplicationServices.GetService(type));

            var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStarted.Register(OnStarted);
            lifetime.ApplicationStopping.Register(OnStopping);

            return app;
        }

        private static void OnStarted() => _contextList.ForEach(x => x.Register());
        private static void OnStopping() => _contextList.ForEach(x => x.Dispose());

        #endregion
    }
}
