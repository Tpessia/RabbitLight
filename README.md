# RabbitLight

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
            sp.GetService<ILoggerFactory>()?.CreateLogger(type).LogInformation($"\r\nSTARTING {type.Name}: {ea.DeliveryTag}\r\n"));

        // Optional callback called after a consumer is successfully invoked
        config.OnEnd = (sp, type, ea) => Task.Run(() =>
            sp.GetService<ILoggerFactory>()?.CreateLogger(type).LogInformation($"\r\nENDING {type.Name}: {ea.DeliveryTag}\r\n"));
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

[Exchange("example-exchange1")]
[Exchange("example-exchange2", ExchangeType.Fanout)] // can be of any ExchangeType accepted by RabbitMQ
public class ExampleConsumer : ConsumerBase
{
    private readonly ILogger<ExampleConsumer> _logger;

    public ExampleConsumer(ILogger<ExampleConsumer> logger) // Any injected services
    {
        _logger = logger;
    }

    [Queue("example-queue1")] // routing key defaults to "*"
    [Queue("example-queue2", "example2", "example20")]
    public async Task Example(MessageContext<Example> context)
    {
        // Routes:
        //     EXCHANGE           ROUTING KEY          QUEUE
        // example-exchange1 ->         *          -> example-queue1
        // example-exchange2 ->         *          -> example-queue1
        // example-exchange1 ->      example2      -> example-queue2
        // example-exchange2 ->      example2      -> example-queue2
        // example-exchange1 ->      example20     -> example-queue2
        // example-exchange2 ->      example20     -> example-queue2

        // Get the message
        // var msg = context.MessageAsBytes;
        // var msg = context.MessageAsString;
        // var msg = context.MessageFromXml();
        // var msg = context.MessageFromJson();

        // Your code here...
    }

    [Queue("example-queue3", "example3")]
    public void ExampleDiscard(MessageContext<Example> context)
    {
        // Routes:
        //     EXCHANGE           ROUTING KEY          QUEUE
        // example-exchange1 ->      example3      -> example-queue3
        // example-exchange2 ->      example3      -> example-queue3

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

Obs.: Remember that if another consumer class is listening to the same exchange and/or queues the messages will be routed acording to the ExchangeType selected.

e.g.:\
Consumer1 with exchange ABC, queue 123 and routing key ABC123\
Consumer2 with exchange ABC, queue 123 and routing key 123ABC

When a message with the ABC123 routing key is received, there is no guarantee that it will be routed to Consumer1 rather than Consumer2.

Think of the routing key declaration only as a binding rule that will be created in the RabbitMQ server, it has no priority relationship with the consumer that declared it.


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

        // Publish message
        // await _publisher.Publish("example-exchange1", "example1", new byte[] { });
        // await _publisher.PublishString("example-exchange1", "example1", "Example");
        // await _publisher.PublishXml("example-exchange1", "example1", body);
        // await _publisher.PublishJson("example-exchange1", "example1", body);

        return "Message published!";
    }
}
```

**5.** In `appsettings.json` add the `RabbitLight` property:

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
    "RequeueDelay": "00:00:05",
    "MonitoringInterval": "00:01:00"
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
| **ScallingThreshold** | Number of messages required to scale a new channel (e.g. 500 messages) or null to disable. |
| **PrefetchCount** | Number of messages that will be cached by each channel at once. |
| **ChannelsPerConnection** | Number of channels per connection (RabbitMQ's IConnection). |
| **RequeueDelay** | Delay for when Nacking a message for requeue or null to 0. |
| **MonitoringInterval** | Interval regarding channel monitoring tasks (health check and scalling) |
