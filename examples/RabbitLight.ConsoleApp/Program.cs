using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitLight.Config;
using RabbitLight.ConsoleApp.Consumers.Context;
using RabbitLight.ConsoleApp.Consumers.Routes;
using System;
using System.IO;
using System.Reflection;

namespace RabbitLight.ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            // Build appsettings.json Configurations
            var env = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "Development";
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
                .AddJsonFile("appsettings.json", false, false)
                .AddJsonFile($"appsettings.{env}.json", false, false)
                .Build();

            // Build Service Provider
            var serviceProvider = new ServiceCollection()
                .AddLogging(c => c.AddConsole())
                .BuildServiceProvider();

            // Create Context
            var context = new ConsoleAppContext(serviceProvider, new ContextConfig
            {
                ConnConfig = ConnectionConfig.FromConfig(configuration.GetSection("RabbitLight")),
                Consumers = Assembly.GetEntryAssembly().GetTypes()
            });

            // Register Context Consumers
            context.Register().Wait();

            // Publish a message
            context.Publisher.PublishString(Exchanges.TestExchange, "*", "Hello, World").Wait();

            // Prevent App From Closing
            Console.ReadLine();

            // Dispose Dependencies
            serviceProvider.Dispose();
        }
    }
}
