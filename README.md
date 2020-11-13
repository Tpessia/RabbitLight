[![NuGet Status](https://img.shields.io/nuget/v/RabbitLight)](https://www.nuget.org/packages/RabbitLight)
[![NuGet Status](https://img.shields.io/github/languages/code-size/Tpessia/RabbitLight?label=size)](https://www.nuget.org/packages/RabbitLight)
[![NuGet Status](https://img.shields.io/nuget/dt/RabbitLight)](https://www.nuget.org/packages/RabbitLight)

**Sample Projects: [RabbitLight.Samples](https://github.com/Tpessia/RabbitLight.Samples)**
<br>
<br>

<img align="left" width="45" height="45" src="https://raw.githubusercontent.com/Tpessia/RabbitLight/master/source/rabbit-shape.png" alt="Icon">

# RabbitLight

## What

RabbitLight is a RabbitMQ Client for .NET developed with simplicity in mind.

Messages are routed to their respective consumers using Data Annotations, similar to the `[Route("my-route")]` attribute used on AspNetCore projects.

It also comes with Auto Scaling and Self Healing on the client side to ensure that your application is always connected to the broker, while optimizing the usage of the machine's resources with parallel processing.

To create a **consumer**, you just have to:

**1.** Create a class that inherits from `ConsumerBase`

**2.** Use `[Exchange("my-exchange")]` to bind a exchange to that class

**3.** Add `[Queue("my-queue")]` to bind a queue to a method from that class

```csharp
[Exchange("my-exchange")]
public class MyConsumer : ConsumerBase
{
    [Queue("my-queue")]
    public async Task MyQueue(MessageContext<MyMessage> context)
    {
        // ...
    }
}
```

And to **publish** a message:

**1.** Inject the context, and get the publisher:

```csharp
public class ExampleController : ControllerBase
{
    private readonly IPublisher _publisher;

    public ExampleController(ExampleContext busContext)
    {
        _publisher = busContext.Publisher;
    }

    // ...
}
```

**2.** Publish a message:

```csharp
await _publisher.PublishString("my-exchange", "routing-key", "Hello, World!");
```

## How

### **1. Create a Context**

Think of a context as a unique instance of a client, that listens and/or publishes to a specific RabbitMQ server.

You may have multiple contexts in your application, but a context should only be added once per application (if it's added multiple times, only the first registration is considered).

```csharp
public class ExampleContext : RabbitLightContext
{
    public ExampleContext(IServiceProvider sp, ContextConfig config) : base(sp, config)
    {
    }
}
```

### **2. On your `Startup` class, add the context configuration:**

```csharp
// ...
using RabbitLight.Extensions;
// ...

public void ConfigureServices(IServiceCollection services)
{
    // ...

    services.AddRabbitLightContext<ExampleContext>(config =>
    {
        // Connection Config (from appsettings.json, explained later on this README.md)
        config.ConnConfig = ConnectionConfig.FromConfig(Configuration.GetSection("RabbitLight"));

        // Consumer Types to include
        config.Consumers = Assembly.GetEntryAssembly().GetTypes();

        // Optional callback called before a consumer is invoked
        config.OnStart = (sp, type, ea) => Task.Run(() =>
        {
           var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(type);
           logger?.LogInformation($"Starting {type.Name}: {ea.DeliveryTag}");
        });

        // Optional callback called after a consumer is successfully invoked
        config.OnEnd = (sp, type, ea) => Task.Run(() =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(type);
            logger?.LogInformation($"Ending {type.Name}: {ea.DeliveryTag}");
        });

        // Optional callback called after the ACK message is sent
        config.OnAck = (sp, type, ea) => Task.Run(() =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(type);
            logger?.LogInformation($"Acked {type.Name}: {ea.DeliveryTag}");
        });

        // Optional global error handler, whose return identifies the requeue strategy
        config.OnError = (sp, ex, type, ea) => Task.Run(() =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(type);
            logger?.LogError($"Handled error in {type.Name}: {ea.DeliveryTag}");
            var requeue = !(ex is SerializationException);
            return requeue;
        });

        // Optional callback called after a publisher receives a NACK from the server
        config.OnPublisherNack = (sp, sender, ea) => Task.Run(() =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger<IPublisher>();
            logger?.LogError($"Publisher error: {ea.DeliveryTag}");
        });
    });

    // ...
}

public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    // ...

    app.UseRabbitLight();

    // ...
}
```

### **3. Create a consumer:**

```csharp
// ...
using RabbitLight.Consumer;
using RabbitLight.Exceptions;
// ...

[Exchange("exchange", ExchangeType.Topic)] // ExchangeTypes accepted by RabbitMQ (Direct, Fanout, Headers, Topic)
public class ExampleConsumer : ConsumerBase
{
    private readonly ILogger<ExampleConsumer> _logger;

    public ExampleConsumer(ILogger<ExampleConsumer> logger) // Any injected services
    {
        _logger = logger;
    }

    [Queue("queue1")] // routing key defaults to "*"
    public async Task Example(MessageContext<ExampleMessage> context)
    {
        // Routes:
        // EXCHANGE      ROUTING KEY      QUEUE
        // exchange  ->       *       ->  queue1

        // Get the message
        // var msg = context.MessageBytes();
        // var msg = context.MessageString();
        // var msg = context.MessageXml();
        // var msg = context.MessageJson();

        // Automatically chooses a parser based on the Content Type Header
        var msg = context.Message();

        // Your code here...
    }

    [Queue("queue2", "key2")]
    [Queue("queue3", "key3", "key30")]
    public void ExampleDiscard(MessageContext<ExampleMessage> context)
    {
        // Routes:
        // EXCHANGE      ROUTING KEY      QUEUE
        // exchange  ->     key2      ->  queue2
        // exchange  ->     key3      ->  queue3
        // exchange  ->     key30     ->  queue3

        // You may discard a message with DiscardMessageException
        // Any other exception will result in requeue
        var msg = context.Message();
        if (msg == null)
            throw new DiscardMessageException("Invalid message");
    }
}

public class ExampleMessage
{
    public string Text { get; set; }
}
```

#### Tip
Instead of using hard-coded strings, you can also use defined constants:
``` csharp
public const string MyExchange = "my-exchange";
[Exchange(Exchanges.MyExchange)]
```

#### Note

Remember that if another consumer class is listening to the same exchange and/or queues the messages will be routed acording to the ExchangeType selected.

Example:

```csharp
[Exchange("exchangeA")]
public class ConsumerA : ConsumerBase
{
    [Queue("queue1", "routingA")]
    public async Task Example(MessageContext<ExampleMessage> context)
    {
        // ...
    }
}

[Exchange("exchangeB")]
public class ConsumerB : ConsumerBase
{
    [Queue("queue1", "routingB")]
    public async Task Example(MessageContext<ExampleMessage> context)
    {
        // ...
    }
}
```

When a message with the **routingA** routing key is received, there is no guarantee that it will be routed to **ConsumerA** rather than **ConsumerB**, as they share the same destination queue. If there is need to check the routing key from a message, use `context.EventArgs.RoutingKey`.

### **4. Create a publisher:**

```csharp
// ...
using RabbitLight.Publisher;
// ...

[ApiController]
[Route("[controller]")]
public class ExampleController : ControllerBase
{
    private readonly IPublisher _publisher;

    public ExampleController(ExampleContext busContext)
    {
        _publisher = busContext.Publisher;
    }

    [HttpPost]
    public async Task<string> SendMessage()
    {
        var body = new Example { Text = "Hello, World!" };

        // Publish byte[]
        await _publisher.Publish("exchange", "key1", new byte[] { });

        // Publish string
        await _publisher.PublishString("exchange", "key1", "Hello, World!");

        // Publish Json
        await _publisher.PublishJson("exchange", "key1", body);

        // Publish Xml
        await _publisher.PublishXml("exchange", "key1", body);

        // Publish Batch (byte[], string, Json and/or Xml)
        await _publisher.PublishBatch(new List<PublishBatch> {
            new PublishBatch("exchange", "key1", MessageType.String, "Hello, World!"),
            new PublishBatch("exchange", "key2", MessageType.Json, body),
        });

        return "Message published!";
    }
}
```  

### **5. In `appsettings.json` add the `RabbitLight` property:**

```json
{
  "RabbitLight": {
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/",
    "HostName": "127.0.0.1",
    "Port": 5672,
    "PortApi": 15672,
    "MinChannels": 10,
    "MaxChannels": 50,
    "ScallingThreshold": 500,
    "PrefetchCount": 10,
    "ChannelsPerConnection": 20,
    "RequeueDelay": "00:00:30",
    "MonitoringInterval": "00:01:00",
    "SkipVHostConfig": false,
    "SkipDeclarations": false
  }
}
```

| Field | Description |
| ----- | ----------- |
| **RabbitMQ Config:** | ---------------------- |
| **UserName** | Username to use when authenticating to the server. |
| **Password** | Password to use when authenticating to the server. |
| **VirtualHost** | Virtual host to access during this connection. |
| **HostName** | The host to connect to. |
| **Port** | The port to connect on. |
| **RabbitLight Config:** | ---------------------- |
| **PortApi** | Port where RabbitMQ management UI plugin is available. |
| **MinChannels** | Minimum number of parallel channels. |
| **MaxChannels** | Maximum number of parallel channels. |
| **ScallingThreshold** | Number of messages required to scale a new channel (e.g. 500 messages) or null to disable scalling. |
| **PrefetchCount** | Number of messages that will be cached by each channel at once. |
| **ChannelsPerConnection** | Number of channels per connection (RabbitMQ's IConnection). |
| **RequeueDelay** | Delay for when Nacking a message for requeue or null to instantaneous. |
| **MonitoringInterval** | Interval regarding channel monitoring tasks (health check, auto scalling and self healing) |
| **SkipVHostConfig** | Skip VHost creation and other configs |
| **SkipDeclarations** | Skip queue and exchange declaration and bind |

### **Bonus: Console App**

Using RabbittLight with a simple Console App

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitLight.Config;
using RabbitLight.ConsoleApp.Consumers.Context;
using System;
using System.IO;
using System.Reflection;

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
        var context = new TestContext(serviceProvider, new ContextConfig
        {
            ConnConfig = ConnectionConfig.FromConfig(configuration.GetSection("RabbitLight")),
            Consumers = Assembly.GetEntryAssembly().GetTypes()
        });

        // Register ContextConsumers
        context.Register().Wait();

        // Publish a message
        context.Publisher.PublishString("exchange", "*", "Hello, World").Wait();

        // Prevent App From Closing
        Console.ReadLine();

        // Dispose Dependencies
        serviceProvider.Dispose();
    }
}
```