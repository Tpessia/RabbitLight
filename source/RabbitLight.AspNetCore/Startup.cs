using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitLight.AspNetCore.Consumers.Context;
using RabbitLight.Config;
using RabbitLight.Extentions;
using System.Reflection;
using System.Threading.Tasks;

namespace RabbitLight.AspNetCore
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            services.AddRabbitLightContext<TestContext>(config =>
            {
                config.ConnConfig = ConnectionConfig.FromConfig(Configuration.GetSection("RabbitLight"));
                config.Consumers = Assembly.GetEntryAssembly().GetTypes();
                config.OnStart = (sp, type, ea) => Task.Run(() => sp.GetService<ILoggerFactory>()?.CreateLogger(type).LogInformation($"\r\nSTARTING {type.Name}: {ea.DeliveryTag}\r\n"));
                config.OnEnd = (sp, type, ea) => Task.Run(() => sp.GetService<ILoggerFactory>()?.CreateLogger(type).LogInformation($"\r\nENDING {type.Name}: {ea.DeliveryTag}\r\n"));
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.UseRabbitLight();
        }
    }
}