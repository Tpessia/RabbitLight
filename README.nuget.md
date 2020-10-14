# RabbitLight

## What

RabbitLight is a RabbitMQ Client for .NET developed with simplicity in mind.

Messages are routed to their respective consumers using Data Annotations, similar to the `[Route(MY-ROUTE)]` attribute used on AspNetCore projects.

To create a consumer, you just have to:

**1.** Create a class that inherits from `ConsumerBase`

**2.** Use `[Exchange(MY-EXCHANGE)]` to bind a exchange to that class

**3.** Add `[Queue(MY-QUEUE)]` to bind a queue to a method from that class

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

## How

**1.** Create a Context

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

**2.** On your `Startup` class, add the context configuration:

```csharp
// ...
using RabbitLight.Extentions;
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
            logger?.LogInformation($"\r\nSTARTING {type.Name}: {ea.DeliveryTag}\r\n");
        });

        // Optional callback called after a consumer is successfully invoked
        config.OnEnd = (sp, type, ea) => Task.Run(() =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(type);
            logger?.LogInformation($"\r\nENDING {type.Name}: {ea.DeliveryTag}\r\n");
        });

        // Optional callback called after the ACK message is sent
        config.OnAck = (sp, type, ea) => Task.Run(() =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(type);
            logger?.LogInformation($"\r\nACKED {type.Name}: {ea.DeliveryTag}\r\n");
        });

        // Optional global error handler, whose return identifies the requeue strategy
        config.OnError = (sp, ex, type, ea) => Task.Run(() =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(type);
            logger?.LogError($"Handled error in {type.Name}: {ea.DeliveryTag}");
            var requeue = !(ex is SerializationException);
            return requeue;
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

**3.** Create a consumer:

```csharp
// ...
using RabbitLight.Consumer;
using RabbitLight.Exceptions;
// ...

[Exchange("exchange1")]
[Exchange("exchange2", ExchangeType.Fanout)] // can be of any ExchangeType accepted by RabbitMQ
public class ExampleConsumer : ConsumerBase
{
    private readonly ILogger<ExampleConsumer> _logger;

    public ExampleConsumer(ILogger<ExampleConsumer> logger) // Any injected services
    {
        _logger = logger;
    }

    [Queue("queue1")] // routing key defaults to "*"
    [Queue("queue2", "key2", "key20")]
    public async Task Example(MessageContext<ExampleMessage> context)
    {
        // Get the message
        // var msg = context.MessageAsBytes;
        // var msg = context.MessageAsString;
        // var msg = context.MessageFromXml();
        // var msg = context.MessageFromJson();

        // Your code here...
    }

    [Queue("queue3", "key3")]
    public void ExampleDiscard(MessageContext<ExampleMessage> context)
    {
        // You may discard a message with DiscardMessageException
        // Any other exception will result in requeue
        var msg = context.MessageFromJson();
        if (msg == null)
            throw new DiscardMessageException("Invalid message");
    }
}

public class Example
{
    public string Text { get; set; }
}
```

> Tip: instead of using hard-coded strings, you can also use defined constants:\
> public const string MyExchange = "my-exchange";\
> [Exchange(Exchanges.MyExchange)]

#### Note

Remember that if another consumer class is listening to the same exchange and/or queues the messages will be routed acording to the ExchangeType selected.

**4.** Create a publisher:

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
        
        _publisher.Publish("exchange1", "key1", new byte[] { });
        _publisher.PublishString("exchange1", "key1", "Hello, World!");
        _publisher.PublishJson("exchange1", "key1", body);
        _publisher.PublishXml("exchange1", "key1", body);
        _publisher.PublishBatch(new List<PublishBatch> {
            new PublishBatch("exchange1", "key1", MessageType.String, "Hello, World!"),
            new PublishBatch("exchange2", "key2", MessageType.Json, body),
        });

        return "Message published!";
    }
}
```

**5.** In `appsettings.json` add the `RabbitLight` property:

```json
{
  "RabbitLight": {
    "UserName": "guest", // Username to use when authenticating to the server.
    "Password": "guest", // Password to use when authenticating to the server.
    "VirtualHost": "/", // Virtual host to access during this connection.
    "HostName": "127.0.0.1", // The host to connect to.
    "Port": 5672, // The port to connect on.
    "PortApi": 15672, // Port where RabbitMQ management UI plugin is available.
    "MinChannels": 10, // Minimum number of parallel channels.
    "MaxChannels": 50, // Maximum number of parallel channels.
    "ScallingThreshold": 500, // Number of messages required to scale a new channel (e.g. 500 messages) or null to disable.
    "PrefetchCount": 10, // Number of messages that will be cached by each channel at once.
    "ChannelsPerConnection": 20, // Number of channels per connection (RabbitMQ's IConnection).
    "RequeueDelay": "00:00:05", // Delay for when Nacking a message for requeue or null to none.
    "MonitoringInterval": "00:01:00" // Interval regarding channel monitoring tasks (health check and scalling)
  }
}
```