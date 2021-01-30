using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitLight.Context;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight.Host
{
    internal class RabbitLightHost : IHostedService, IDisposable
    {
        private readonly RabbitLightContext _context;

        public RabbitLightHost(RabbitLightContext context)
        {
            _context = context;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _context.Register();
                }
                catch (Exception ex)
                {
                    _context.Logger.LogError(ex, $"[{_context.Config.Alias}] Error while registering");
                }
            });
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
