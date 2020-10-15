# RabbitLight

RabbitLight is a RabbitMQ Client for .NET developed with simplicity in mind.

Messages are routed to their respective consumers using Data Annotations, similar to the `[Route(MY-ROUTE)]` attribute used on AspNetCore projects.

It also comes with Auto Scaling and Self Healing on the client side to ensure that your application is always connected to the broker, while optimizing the usage of the machine's resources with parallel processing.

To create a **consumer**, you just have to:

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
await _publisher.PublishString("exchange1", "key1", "Hello, World!");
```