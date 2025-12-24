# Sitas.Edge

A modern, extensible .NET service bus library for multi-protocol messaging. Built with **Builder**, **Strategy**, and **Attribute-Based Discovery** patterns for a clean, intuitive developer experience.

## Table of Contents

- [Features](#features)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
  - [Handler Patterns](#handler-patterns)
  - [Dependency Injection](#dependency-injection)
  - [Auto-Injection](#auto-injection)
- [API Reference](#api-reference)
  - [Handler Method Signature](#handler-method-signature)
  - [IMessageContext](#imessagecontext)
  - [TagValue&lt;T&gt;](#tagvaluet-edge-plc-driver)
  - [Edge PLC Driver API Methods](#edge-plc-driver-api-methods)
  - [MQTT API Methods](#mqtt-api-methods)
  - [Event Handler Method Signature](#event-handler-method-signature)
  - [TagReadResults](#tagreadresults)
  - [CancellationToken](#cancellationtoken)
- [Protocol Guides](#protocol-guides)
  - [Sitas.Edge.Mqtt - MQTT Protocol](#conduitmqtt---mqtt-protocol)
  - [Sitas.Edge.EdgePlcDriver - Allen-Bradley PLC](#conduitedgeplcdriver---allen-bradley-plc)
- [Advanced Features](#advanced-features)
  - [EventMediator](#eventmediator)
  - [Disabling Handlers](#disabling-handlers)
- [Best Practices](#best-practices)
- [Requirements](#requirements)

## Features

- 🔌 **Multi-Protocol Support** - MQTT, Edge PLC Driver (Allen-Bradley PLCs), and extensible to AMQP, Kafka, etc.
- 🏗️ **Fluent Builder API** - Intuitive configuration with IntelliSense support
- 🎯 **Attribute-Based Handlers** - Declare subscriptions with simple attributes
- 💉 **Dependency Injection** - Works with any DI container (Microsoft DI, Autofac, SimpleInjector, Ninject, Lamar, DryIoc)
- 🔄 **Auto-Reconnect** - Resilient connections with exponential backoff
- 📦 **Cross-Platform** - Works with Console, WPF, Windows Forms, ASP.NET Core
- 🎭 **Strongly-Typed Messages** - Type-safe message handling with automatic serialization
- ⚡ **Flexible Activation** - FuncActivator auto-creates handlers, no manual DI registration needed

## Available Packages

| Package | Description |
|---------|-------------|
| `Sitas.Edge.Core` | Core abstractions and interfaces |
| `Sitas.Edge.Mqtt` | MQTT protocol support (includes MQTTnet) |
| `Sitas.Edge.EdgePlcDriver` | Edge PLC Driver for Allen-Bradley PLC communication (includes ASComm IoT) |
| `Sitas.Edge.DependencyInjection` | DI extensions for ASP.NET Core and Generic Host |

> **Note:** When building for distribution, you can use ILRepack to merge dependencies (MQTTnet, ASCommStd) into the main DLLs for single-file deployment.

---

## Quick Start

### MQTT Example

```csharp
using Sitas.Edge.Core;
using Sitas.Edge.Mqtt;
using Sitas.Edge.Mqtt.Attributes;

[MqttSubscribe("mqtt", "sensors/temperature")]
public class TemperatureHandler : IMessageSubscriptionHandler<TemperatureReading>
{
    public Task HandleAsync(TemperatureReading message, IMessageContext context, CancellationToken ct)
    {
        Console.WriteLine($"Temperature: {message.Value}°C");
        return Task.CompletedTask;
    }
}

var sitasEdge = SitasEdgeBuilder.Create()
    .AddMqttConnection(mqtt => mqtt
        .WithConnectionName("mqtt")
        .WithBroker("broker.hivemq.com", 1883)
        .WithHandlersFromEntryAssembly())
    .Build();

await sitasEdge.ConnectAllAsync();
```

### Edge PLC Driver Example

```csharp
using Sitas.Edge.Core;
using Sitas.Edge.EdgePlcDriver;
using Sitas.Edge.EdgePlcDriver.Attributes;

[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", pollingIntervalMs: 100)]
public class TemperatureHandler : IMessageSubscriptionHandler<TagValue<float>>
{
    public Task HandleAsync(TagValue<float> message, IMessageContext context, CancellationToken ct)
    {
        Console.WriteLine($"Temperature: {message.Value}°C");
        return Task.CompletedTask;
    }
}

var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0)
        .WithDefaultPollingInterval(100)
        .WithHandlersFromEntryAssembly())
    .Build();

await sitasEdge.ConnectAllAsync();
```

---

## Core Concepts

### Handler Patterns

Sitas.Edge supports two complementary patterns for subscribing to messages:

#### 🎯 Pattern 1: Declarative (Attribute-Based)
**Best for:** Production code, organized structure, testable handlers

```csharp
// MQTT Example
[MqttSubscribe("mqtt", "sensors/temperature")]
public class TemperatureHandler : IMessageSubscriptionHandler<TemperatureReading>
{
    private readonly ILogger<TemperatureHandler> _logger;
    
    public TemperatureHandler(ILogger<TemperatureHandler> logger)
    {
        _logger = logger;  // Full DI support
    }

    public Task HandleAsync(TemperatureReading message, IMessageContext context, CancellationToken ct)
    {
        _logger.LogInformation("Temperature: {Value}°C", message.Value);
        return Task.CompletedTask;
    }
}

// Edge PLC Driver Example
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", pollingIntervalMs: 100)]
public class TemperatureHandler : IMessageSubscriptionHandler<TagValue<float>>
{
    public Task HandleAsync(TagValue<float> message, IMessageContext context, CancellationToken ct)
    {
        Console.WriteLine($"Temperature: {message.Value}°C");
        return Task.CompletedTask;
    }
}
```

**Advantages:**
- ✅ Clean separation of concerns
- ✅ Full dependency injection support
- ✅ Easy to test (mock dependencies)
- ✅ Automatic discovery via `WithHandlersFromEntryAssembly()`
- ✅ Reusable across projects

#### ⚡ Pattern 2: Inline (Lambda-Based)
**Best for:** Quick prototyping, debugging, temporary subscriptions, dynamic scenarios

```csharp
// MQTT - Typed subscription
// Signature: Task<IAsyncDisposable> SubscribeAsync<TMessage>(string topic, Func<TMessage, IMessageContext, CancellationToken, Task> handler, QualityOfService qos, CancellationToken ct)
var sub = await mqttConnection.SubscribeAsync<TemperatureReading>(
    "sensors/temperature",                                    // topic: string
    async (message, context, ct) =>                            // handler: Func<TMessage, IMessageContext, CancellationToken, Task>
    {
        // message: TemperatureReading (your custom type)
        // context: IMessageContext (Topic, CorrelationId, Publisher, etc.)
        // ct: CancellationToken
        Console.WriteLine($"Temp: {message.Value}°C from {context.Topic}");
    },
    qos: QualityOfService.AtLeastOnce);                        // qos: QualityOfService enum

// MQTT - Topic-aware (for wildcards)
// Signature: Task<IAsyncDisposable> SubscribeAsync(string topic, Func<string, ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task> handler, QualityOfService qos, CancellationToken ct)
var wildcardSub = await mqttConnection.SubscribeAsync(
    "factory/+/status",                                       // topic: string (with wildcards)
    async (actualTopic, payload, context, ct) =>              // handler: Func<string, ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task>
    {
        // actualTopic: string (the matched topic, e.g., "factory/building1/status")
        // payload: ReadOnlyMemory<byte> (raw message bytes)
        // context: IMessageContext
        // ct: CancellationToken
        var buildingName = actualTopic.Split('/')[1];
        var data = JsonSerializer.Deserialize<FactoryStatus>(payload.Span);
        Console.WriteLine($"Building {buildingName}: {data.IsOnline}");
    });

// Edge PLC Driver
// Signature: Task<IAsyncDisposable> SubscribeAsync<T>(string tagName, Func<TagValue<T>, IEdgePlcDriverMessageContext, CancellationToken, Task> handler, int pollingIntervalMs, CancellationToken ct)
var plcSub = await plcConnection.SubscribeAsync<int>(
    "Counter_Production",                                     // tagName: string
    async (tagValue, context, ct) =>                          // handler: Func<TagValue<T>, IEdgePlcDriverMessageContext, CancellationToken, Task>
    {
        // tagValue: TagValue<int> (Value, PreviousValue, Quality, TagName, etc.)
        // context: IEdgePlcDriverMessageContext (extends IMessageContext, adds WriteTagAsync, ReadTagAsync)
        // ct: CancellationToken
        Console.WriteLine($"Count: {tagValue.Value} (Quality: {tagValue.Quality})");
        if (tagValue.Value >= 1000)
        {
            // Use context to write back to PLC
            await context.WriteTagAsync("Counter_Production", 0, ct);
        }
    },
    pollingIntervalMs: 500);                                 // pollingIntervalMs: int (milliseconds)

// Unsubscribe when done
await sub.DisposeAsync();                                     // Returns: Task
```

**Advantages:**
- ✅ No separate handler class needed
- ✅ Quick to write and test
- ✅ Perfect for debugging (add temporary logging)
- ✅ Can subscribe/unsubscribe dynamically at runtime
- ✅ Direct access to connection for read/write operations

#### When to Use Each Pattern

| Scenario | Pattern | Why |
|----------|---------|-----|
| **Production handlers** | Declarative | Maintainable, testable, organized |
| **Complex business logic** | Declarative | DI support, separation of concerns |
| **Unit testing required** | Declarative | Easy to mock dependencies |
| **Quick debugging** | Inline | Fast to add/remove, no files needed |
| **Temporary monitoring** | Inline | Can dispose when done |
| **Dynamic subscriptions** | Inline | Subscribe/unsubscribe at runtime |
| **Wildcard topic inspection** | Inline (Topic-Aware) | Extract dynamic segments |

---

### Dependency Injection

Sitas.Edge provides flexible dependency injection support with **two activation strategies** and works with **any DI container**.

#### Understanding Activators

**1️⃣ FuncActivator (Default & Recommended)**
- Creates handlers **even if not registered** in DI container
- Resolves constructor dependencies from DI when available
- Falls back to `new()` if dependencies are missing
- Auto-injects Conduit types (`IMqttConnection`, `IEdgePlcDriver`, `ISitasEdge`, `ILogger<T>`)

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<IMyService, MyService>();
var serviceProvider = services.BuildServiceProvider();

var sitasEdge = SitasEdgeBuilder.Create()
    .WithActivator(type => 
        ActivatorUtilities.GetServiceOrCreateInstance(serviceProvider, type))
    .AddMqttConnection(mqtt => mqtt
        .WithConnectionName("mqtt")
        .WithBroker("broker.hivemq.com", 1883)
        .WithHandlersFromEntryAssembly())
    .Build();
```

**2️⃣ ServiceProviderActivator (Strict)**
- Resolves handlers directly from DI container
- Enforces strict DI registration
- Must register **every handler class** in DI container

```csharp
var services = new ServiceCollection();
services.AddSingleton<IMyService, MyService>();
services.AddTransient<TemperatureHandler>();  // Must register all handlers
var serviceProvider = services.BuildServiceProvider();

var sitasEdge = SitasEdgeBuilder.Create()
    .WithServiceProvider(serviceProvider)
    .AddMqttConnection(mqtt => mqtt
        .WithConnectionName("mqtt")
        .WithBroker("broker.hivemq.com", 1883)
        .WithHandlersFromEntryAssembly())
    .Build();
```

**💡 Recommendation:** Use **FuncActivator** (`.WithActivator()`) for flexibility. Use **ServiceProviderActivator** (`.WithServiceProvider()`) only when you need strict DI validation.

#### Multi-Container Support

Use the `DIContainerBuilder` pattern to support multiple DI containers:

```csharp
// Switch between containers by changing one line
var diContainer = DIContainerBuilder.Create()
    .UseNativeDI()      // or .UseAutofac() or .UseSimpleInjector()
    .Build();

var sitasEdge = SitasEdgeBuilder.Create()
    .WithActivator(diContainer.GetActivator())
    .AddMqttConnection(mqtt => mqtt
        .WithConnectionName("mqtt")
        .WithBroker("broker.hivemq.com", 1883)
        .WithHandlersFromEntryAssembly())
    .Build();
```

**Supported Containers:**
- Microsoft.Extensions.DependencyInjection (NativeDI)
- Autofac
- SimpleInjector
- Any container that provides `IServiceProvider` or `Func<Type, object>`

---

### Auto-Injection

Sitas.Edge automatically injects common dependencies into your handlers **without requiring manual DI registration**.

#### Automatically Injected Types

| Type | Description |
|------|-------------|
| `ISitasEdge` | The main Conduit instance |
| `IMqttConnection` | MQTT connection (if configured) |
| `IEdgePlcDriver` | PLC connection (if configured) |
| `IMessagePublisher` | Generic publisher from any configured connection |
| `IMqttPublisher` | MQTT-specific publisher |
| `IEdgePlcDriverPublisher` | PLC-specific publisher |
| `ILogger<T>` | Logger (uses `NullLogger` if not registered) |

#### Example: Handler with Auto-Injected Dependencies

```csharp
[MqttSubscribe("mqtt", "sensors/temperature")]
public class TemperatureHandler : IMessageSubscriptionHandler<TemperatureReading>
{
    private readonly ILogger<TemperatureHandler> _logger;  // Auto-injected
    private readonly IMqttConnection _mqtt;                 // Auto-injected
    private readonly IMyService _myService;                 // From your DI container
    
    public TemperatureHandler(
        ILogger<TemperatureHandler> logger,
        IMqttConnection mqtt,
        IMyService myService)
    {
        _logger = logger;      // ✅ NullLogger if not registered
        _mqtt = mqtt;          // ✅ Auto-injected by Conduit
        _myService = myService; // ✅ From your DI container
    }
    
    public async Task HandleAsync(
        TemperatureReading message,
        IMessageContext context,
        CancellationToken ct)
    {
        _logger.LogInformation("Temperature: {Value}°C", message.Value);
        
        // Publish to another topic using the injected connection
        await _mqtt.Publisher.PublishAsync("alerts/high-temp", 
            new { temp = message.Value }, 
            cancellationToken: ct);
    }
}
```

**Minimal Configuration:** Handlers with only auto-injected dependencies work without any DI setup:

```csharp
// No DI container needed!
var sitasEdge = SitasEdgeBuilder.Create()
    .AddMqttConnection(mqtt => mqtt
        .WithConnectionName("mqtt")
        .WithBroker("broker.hivemq.com", 1883)
        .WithHandlersFromEntryAssembly())
    .Build();  // ✅ No .WithActivator() needed
```

---

## API Reference

### Handler Method Signature

All message handlers implement `IMessageSubscriptionHandler<TMessage>` with the following method:

```csharp
Task HandleAsync(
    TMessage message,                    // The deserialized message payload
    IMessageContext context,              // Message context with metadata and publisher
    CancellationToken cancellationToken = default)  // Token to cancel the operation
```

**Parameters:**
- `message` (`TMessage`): The deserialized message payload. Type depends on handler:
  - **MQTT**: Your custom message class (e.g., `TemperatureReading`, `SensorData`)
  - **Edge PLC Driver**: `TagValue<T>` where `T` is the tag data type (e.g., `TagValue<float>`, `TagValue<int>`)
- `context` (`IMessageContext`): Provides metadata about the message (see [IMessageContext](#imessagecontext) below)
- `cancellationToken` (`CancellationToken`): Optional token to cancel the operation

**Returns:** `Task` - Represents the asynchronous operation

### IMessageContext

Provides context information about a received message:

```csharp
public interface IMessageContext
{
    string Topic { get; }                                    // Topic/channel from which message was received
    string? CorrelationId { get; }                          // Correlation ID for message tracking
    DateTimeOffset ReceivedAt { get; }                       // Timestamp when message was received
    ReadOnlyMemory<byte> RawPayload { get; }                 // Raw payload bytes
    IMessagePublisher Publisher { get; }                     // Publisher for sending response messages
    IReadOnlyDictionary<string, string> Metadata { get; }   // Additional metadata (key-value pairs)
}
```

**Properties:**
- `Topic` (`string`): The topic or channel from which the message was received
  - **MQTT**: The MQTT topic (e.g., `"sensors/temperature"`)
  - **Edge PLC Driver**: The tag name (e.g., `"Sensor_Temperature"`)
- `CorrelationId` (`string?`): Optional correlation identifier for request-response patterns
- `ReceivedAt` (`DateTimeOffset`): Timestamp when the message was received
- `RawPayload` (`ReadOnlyMemory<byte>`): Raw message bytes before deserialization
- `Publisher` (`IMessagePublisher`): Interface for publishing messages back to the connection
- `Metadata` (`IReadOnlyDictionary<string, string>`): Additional metadata as key-value pairs

**Protocol-Specific Contexts:**

**IEdgePlcDriverMessageContext** (extends `IMessageContext`):
```csharp
public interface IEdgePlcDriverMessageContext : IMessageContext
{
    string TagName { get; }                                 // Tag name that triggered this message
    new IEdgePlcDriverPublisher Publisher { get; }         // PLC-specific publisher
    Task WriteTagAsync<T>(string tagName, T value, CancellationToken ct = default);
    Task<TagValue<T>> ReadTagAsync<T>(string tagName, CancellationToken ct = default);
}
```

**IMqttMessageContext** (extends `IMessageContext`):
```csharp
public interface IMqttMessageContext : IMessageContext
{
    QualityOfService QoS { get; }                            // MQTT quality of service level
    bool Retain { get; }                                     // Whether message was retained
    // Additional MQTT 5.0 properties available
}
```

### TagValue<T> (Edge PLC Driver)

Represents a PLC tag value with metadata:

```csharp
public sealed class TagValue<T>
{
    public string TagName { get; set; }                      // Name of the tag
    public T Value { get; set; }                             // Current value of the tag
    public T? PreviousValue { get; set; }                    // Previous value (null on first read)
    public DateTimeOffset Timestamp { get; set; }            // When value was read from PLC
    public TagQuality Quality { get; set; }                  // Quality indicator (Good, Uncertain, Bad, CommError, NotFound)
    public bool IsInitialRead { get; }                       // True if this is the first read (no previous value)
    public bool HasChanged { get; }                          // True if value changed from previous read
}
```

**TagQuality Enum:**
```csharp
public enum TagQuality
{
    Good = 0,        // Value is good and reliable
    Uncertain = 1,   // Value quality is uncertain
    Bad = 2,         // Value is bad or unavailable
    CommError = 3,   // Communication error occurred
    NotFound = 4     // Tag was not found in the PLC
}
```

### Edge PLC Driver API Methods

#### ReadTagAsync<T>

Reads a single tag value from the PLC.

```csharp
Task<TagValue<T>> ReadTagAsync<T>(
    string tagName,                                          // Tag name to read (supports nested paths)
    CancellationToken cancellationToken = default)           // Optional cancellation token
```

**Inputs:**
- `tagName` (`string`): Tag name or nested path (e.g., `"Sensor_Temperature"` or `"ngpSampleCurrent.pallets[0].cavities[1].siteNumber"`)
- `cancellationToken` (`CancellationToken`): Optional token to cancel the operation

**Output:**
- Returns `Task<TagValue<T>>` where `T` is the expected data type (e.g., `float`, `int`, `MachineStatus`)

**Example:**
```csharp
var result = await connection.ReadTagAsync<float>("Sensor_Temperature");
if (result.Quality == TagQuality.Good)
{
    Console.WriteLine($"Temperature: {result.Value}°C");
    Console.WriteLine($"Previous: {result.PreviousValue}°C");
    Console.WriteLine($"Changed: {result.HasChanged}");
}
```

#### ReadTagsAsync<T>

Reads multiple tags of the same type in a single operation (type-safe).

```csharp
Task<IReadOnlyDictionary<string, TagValue<T>>> ReadTagsAsync<T>(
    IEnumerable<string> tagNames,                            // Collection of tag names to read
    CancellationToken cancellationToken = default)           // Optional cancellation token
```

**Inputs:**
- `tagNames` (`IEnumerable<string>`): Collection of tag names (all must be the same type)
- `cancellationToken` (`CancellationToken`): Optional token to cancel the operation

**Output:**
- Returns `Task<IReadOnlyDictionary<string, TagValue<T>>>` - Dictionary mapping tag names to their TagValue with metadata (Quality, Timestamp, PreviousValue, etc.)

**Example:**
```csharp
var results = await connection.ReadTagsAsync<int>(new[]
{
    "Tag1",
    "Tag2",
    "Tag3"
});
// Returns Dictionary<string, TagValue<int>> - includes metadata
foreach (var (tagName, tagValue) in results)
{
    Console.WriteLine($"{tagName}: {tagValue.Value} (Quality: {tagValue.Quality}, Timestamp: {tagValue.Timestamp})");
    if (tagValue.PreviousValue.HasValue)
    {
        Console.WriteLine($"  Previous: {tagValue.PreviousValue.Value}");
    }
}
```

#### ReadTagsAsync (Mixed Types)

Reads multiple tags of different types in a single operation.

```csharp
Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(
    IEnumerable<string> tagNames,                            // Collection of tag names (can be different types)
    CancellationToken cancellationToken = default)           // Optional cancellation token
```

**Inputs:**
- `tagNames` (`IEnumerable<string>`): Collection of tag names (can be different types)
- `cancellationToken` (`CancellationToken`): Optional token to cancel the operation

**Output:**
- Returns `Task<IReadOnlyDictionary<string, object?>>` - Dictionary mapping tag names to their values (requires casting)

**Example:**
```csharp
var results = await connection.ReadTagsAsync(new[] { "Tag1", "Tag2", "Tag3" });
foreach (var (tagName, value) in results)
{
    if (value is float floatValue)
        Console.WriteLine($"{tagName}: {floatValue}°C");
    else if (value is int intValue)
        Console.WriteLine($"{tagName}: {intValue}");
}
```

#### WriteTagAsync<T>

Writes a value to a PLC tag.

```csharp
Task WriteTagAsync<T>(
    string tagName,                                          // Tag name to write to (supports nested paths)
    T value,                                                 // Value to write (type T)
    CancellationToken cancellationToken = default)            // Optional cancellation token
```

**Inputs:**
- `tagName` (`string`): Tag name or nested path (e.g., `"Setpoint_Temperature"` or `"ngpSampleCurrent.pallets[0].cavities[1].siteNumber"`)
- `value` (`T`): Value to write. Type `T` must match the PLC tag type
  - For STRING types: Can pass `string` directly (automatically converts to `LogixString`)
- `cancellationToken` (`CancellationToken`): Optional token to cancel the operation

**Output:**
- Returns `Task` - Completes when write operation finishes

**Example:**
```csharp
// Write primitive type
await connection.WriteTagAsync("Setpoint_Temperature", 75.5f);

// Write to nested path
await connection.WriteTagAsync("ngpSampleCurrent.pallets[0].cavities[1].siteNumber", 5);

// Write STRING (automatic conversion)
await connection.WriteTagAsync("LotNumber", "LOT-12345");

// Write UDT
var status = new MachineStatus { running = true, productCount = 100 };
await connection.WriteTagAsync("Machine1_Status", status);
```

#### WriteTagsAsync

Writes multiple tag values in a single operation.

```csharp
Task WriteTagsAsync(
    IReadOnlyDictionary<string, object> tagValues,           // Dictionary of tag names to values
    CancellationToken cancellationToken = default)            // Optional cancellation token
```

**Inputs:**
- `tagValues` (`IReadOnlyDictionary<string, object>`): Dictionary mapping tag names to their values
- `cancellationToken` (`CancellationToken`): Optional token to cancel the operation

**Output:**
- Returns `Task` - Completes when all write operations finish

**Example:**
```csharp
var values = new Dictionary<string, object>
{
    { "Tag1", 100 },
    { "Tag2", 200.5f },
    { "Tag3", true }
};
await connection.WriteTagsAsync(values);
```

#### SubscribeAsync<T> (Inline Subscription)

Dynamically subscribes to tag changes with a lambda handler.

```csharp
Task<IAsyncDisposable> SubscribeAsync<T>(
    string tagName,                                          // Tag name to subscribe to
    Func<TagValue<T>, IEdgePlcDriverMessageContext, CancellationToken, Task> handler,  // Handler function
    int pollingIntervalMs = 100,                             // Polling interval in milliseconds
    CancellationToken cancellationToken = default)           // Optional cancellation token
```

**Inputs:**
- `tagName` (`string`): Tag name to subscribe to
- `handler` (`Func<TagValue<T>, IEdgePlcDriverMessageContext, CancellationToken, Task>`): Handler function that receives:
  - `TagValue<T>`: The tag value with metadata
  - `IEdgePlcDriverMessageContext`: Context with tag name, publisher, and read/write methods
  - `CancellationToken`: Token to cancel the operation
- `pollingIntervalMs` (`int`): Polling interval in milliseconds (default: 100)
- `cancellationToken` (`CancellationToken`): Optional token to cancel the subscription setup

**Output:**
- Returns `Task<IAsyncDisposable>` - Disposable subscription that can be used to unsubscribe

**Example:**
```csharp
var subscription = await connection.SubscribeAsync<int>(
    "Counter_Production",
    async (tagValue, context, ct) =>
    {
        Console.WriteLine($"Count: {tagValue.Value} (Quality: {tagValue.Quality})");
        
        if (tagValue.Value >= 1000)
        {
            // Write back to PLC using context
            await context.WriteTagAsync("Counter_Production", 0, ct);
        }
    },
    pollingIntervalMs: 500);

// Unsubscribe when done
await subscription.DisposeAsync();
```

### MQTT API Methods

#### SubscribeAsync<TMessage> (Typed)

Subscribes to an MQTT topic with automatic JSON deserialization.

```csharp
Task<IAsyncDisposable> SubscribeAsync<TMessage>(
    string topic,                                           // Topic pattern (supports wildcards: +, #)
    Func<TMessage, IMessageContext, CancellationToken, Task> handler,  // Handler function
    QualityOfService qos = QualityOfService.AtLeastOnce,    // MQTT QoS level
    CancellationToken cancellationToken = default)         // Optional cancellation token
    where TMessage : class
```

**Inputs:**
- `topic` (`string`): Topic pattern (e.g., `"sensors/temperature"` or `"factory/+/status"`)
- `handler` (`Func<TMessage, IMessageContext, CancellationToken, Task>`): Handler function that receives:
  - `TMessage`: The deserialized message object
  - `IMessageContext`: Context with topic, correlation ID, publisher, etc.
  - `CancellationToken`: Token to cancel the operation
- `qos` (`QualityOfService`): MQTT quality of service level (default: `AtLeastOnce`)
- `cancellationToken` (`CancellationToken`): Optional token to cancel the subscription setup

**Output:**
- Returns `Task<IAsyncDisposable>` - Disposable subscription

**Example:**
```csharp
var sub = await mqttConnection.SubscribeAsync<TemperatureReading>(
    "sensors/temperature",
    async (message, context, ct) =>
    {
        Console.WriteLine($"Temp: {message.Value}°C from topic: {context.Topic}");
        // Publish response using context
        await context.Publisher.PublishAsync("alerts/high-temp", 
            new { temp = message.Value }, ct);
    },
    qos: QualityOfService.ExactlyOnce);
```

#### SubscribeAsync (Raw Bytes)

Subscribes to an MQTT topic with raw byte handling.

```csharp
Task<IAsyncDisposable> SubscribeAsync(
    string topic,                                           // Topic pattern
    Func<ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task> handler,  // Handler function
    QualityOfService qos = QualityOfService.AtLeastOnce,    // MQTT QoS level
    CancellationToken cancellationToken = default)         // Optional cancellation token
```

**Inputs:**
- `topic` (`string`): Topic pattern
- `handler` (`Func<ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task>`): Handler function that receives:
  - `ReadOnlyMemory<byte>`: Raw message payload bytes
  - `IMessageContext`: Message context
  - `CancellationToken`: Token to cancel the operation
- `qos` (`QualityOfService`): MQTT QoS level
- `cancellationToken` (`CancellationToken`): Optional token to cancel the subscription setup

**Output:**
- Returns `Task<IAsyncDisposable>` - Disposable subscription

**Example:**
```csharp
var sub = await mqttConnection.SubscribeAsync(
    "devices/+/raw",
    async (payload, context, ct) =>
    {
        var bytes = payload.ToArray();
        var data = Encoding.UTF8.GetString(bytes);
        Console.WriteLine($"Raw data: {data}");
    });
```

#### SubscribeAsync (Topic-Aware)

Subscribes to an MQTT topic with topic parameter (useful for wildcards).

```csharp
Task<IAsyncDisposable> SubscribeAsync(
    string topic,                                           // Topic pattern (supports wildcards)
    Func<string, ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task> handler,  // Handler function
    QualityOfService qos = QualityOfService.AtLeastOnce,    // MQTT QoS level
    CancellationToken cancellationToken = default)         // Optional cancellation token
```

**Inputs:**
- `topic` (`string`): Topic pattern with wildcards (e.g., `"factory/+/status"`)
- `handler` (`Func<string, ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task>`): Handler function that receives:
  - `string`: The actual topic that matched (e.g., `"factory/building1/status"`)
  - `ReadOnlyMemory<byte>`: Raw message payload bytes
  - `IMessageContext`: Message context
  - `CancellationToken`: Token to cancel the operation
- `qos` (`QualityOfService`): MQTT QoS level
- `cancellationToken` (`CancellationToken`): Optional token to cancel the subscription setup

**Output:**
- Returns `Task<IAsyncDisposable>` - Disposable subscription

**Example:**
```csharp
var sub = await mqttConnection.SubscribeAsync(
    "factory/+/status",
    async (actualTopic, payload, context, ct) =>
    {
        // Extract building name from topic
        var parts = actualTopic.Split('/');
        var buildingName = parts[1];  // "building1", "building2", etc.
        
        var data = JsonSerializer.Deserialize<FactoryStatus>(payload.Span);
        Console.WriteLine($"Building {buildingName}: {data.IsOnline}");
    });
```

#### PublishAsync<T>

Publishes a message to an MQTT topic.

```csharp
Task PublishAsync<T>(
    string topic,                                           // Topic to publish to
    T message,                                               // Message object to publish
    CancellationToken cancellationToken = default)          // Optional cancellation token
    where T : class
```

**Inputs:**
- `topic` (`string`): MQTT topic to publish to
- `message` (`T`): Message object (automatically serialized to JSON)
- `cancellationToken` (`CancellationToken`): Optional token to cancel the operation

**Output:**
- Returns `Task` - Completes when message is published

**Example:**
```csharp
await mqttConnection.Publisher.PublishAsync("sensors/temperature",
    new TemperatureReading { Value = 25.5f, Timestamp = DateTimeOffset.UtcNow },
    cancellationToken: ct);
```

### Event Handler Method Signature

Event handlers implement `IEventHandler<TEvent>` with the following method:

```csharp
Task HandleAsync(
    TEvent eventData,                                       // Event data sent via EmitAsync
    TagReadResults tagValues,                               // Values read from PLC tags (if [ReadTag] attributes used)
    CancellationToken cancellationToken = default)         // Token to cancel the operation
```

**Parameters:**
- `eventData` (`TEvent`): The event data object passed to `EventMediator.EmitAsync()`
- `tagValues` (`TagReadResults`): Container with tag values read from PLC (see [TagReadResults](#tagreadresults) below)
- `cancellationToken` (`CancellationToken`): Optional token to cancel the operation

**Returns:** `Task` - Represents the asynchronous operation

### TagReadResults

Container for tag values read from PLC tags specified in `[ReadTag]` attributes on event handlers.

```csharp
public class TagReadResults
{
    // Properties
    public IEnumerable<string> TagNames { get; }            // All tag names in results
    public int Count { get; }                                // Number of tag values
    public bool AllGoodQuality { get; }                      // True if all tags have good quality
    public IEnumerable<string> BadQualityTags { get; }       // Tags with bad quality
    
    // Methods
    public T? Get<T>(string tagName)                         // Get tag value (returns default if not found)
    public TagReadValue<T>? GetTagValue<T>(string tagName)   // Get tag value with full metadata
    public T GetRequired<T>(string tagName)                 // Get tag value (throws if not found)
    public bool Contains(string tagName)                     // Check if tag exists
}
```

**Methods:**
- `Get<T>(string tagName)`: Returns `T?` - Gets tag value, returns `default(T)` if not found or cannot cast
- `GetTagValue<T>(string tagName)`: Returns `TagReadValue<T>?` - Gets tag value with full metadata (quality, timestamp)
- `GetRequired<T>(string tagName)`: Returns `T` - Gets tag value, throws `KeyNotFoundException` if not found
- `Contains(string tagName)`: Returns `bool` - Checks if tag exists in results

**TagReadValue<T> Properties:**
```csharp
public class TagReadValue<T>
{
    public string TagName { get; init; }                    // Name of the tag
    public T Value { get; init; }                            // The tag value
    public TagQuality Quality { get; init; }                 // Quality indicator
    public DateTimeOffset Timestamp { get; init; }           // When value was read
    public bool IsGood { get; }                              // True if quality is Good
}
```

**Example:**
```csharp
[Event("getMachineData")]
public class MachineDataHandler : IEventHandler<object>
{
    [ReadTag("plc1", "Sensor_Temperature")]
    public float Temperature { get; set; }
    
    public Task HandleAsync(object eventData, TagReadResults tags, CancellationToken ct)
    {
        // Access via property (auto-populated)
        Console.WriteLine($"Temp: {Temperature}°C");
        
        // Or access via TagReadResults
        var temp = tags.Get<float>("Sensor_Temperature");
        var tempWithMeta = tags.GetTagValue<float>("Sensor_Temperature");
        
        if (tempWithMeta?.IsGood == true)
        {
            Console.WriteLine($"Quality: {tempWithMeta.Quality}, Timestamp: {tempWithMeta.Timestamp}");
        }
        
        return Task.CompletedTask;
    }
}
```

### CancellationToken

Standard .NET `CancellationToken` used throughout Sitas.Edge for canceling operations.

**Usage:**
- All async methods accept an optional `CancellationToken cancellationToken = default` parameter
- Pass `CancellationToken.None` or omit the parameter to use default (non-cancellable)
- Pass a `CancellationTokenSource.Token` to enable cancellation
- When cancellation is requested, operations throw `OperationCanceledException`

**Example:**
```csharp
var cts = new CancellationTokenSource();

// Cancel after 5 seconds
cts.CancelAfter(TimeSpan.FromSeconds(5));

try
{
    var result = await connection.ReadTagAsync<float>("Sensor_Temperature", cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation was cancelled");
}
```

---

## Protocol Guides

### Sitas.Edge.Mqtt - MQTT Protocol

#### Wildcard Patterns

| Pattern | Matches | Use Case |
|---------|---------|----------|
| `sensors/temperature` | Exact match | Single specific topic |
| `sensors/+/temperature` | `sensors/room1/temperature`<br>`sensors/room2/temperature` | Single-level wildcard |
| `sensors/#` | `sensors/temperature`<br>`sensors/room1/temperature`<br>`sensors/room1/temp/current` | Multi-level wildcard |

#### Configuration Options

```csharp
var sitasEdge = SitasEdgeBuilder.Create()
    .AddMqttConnection(mqtt => mqtt
        .WithConnectionName("mqtt")
        .WithBroker("broker.hivemq.com", 1883)
        .WithCredentials("user", "password")
        .WithClientId($"app-{Environment.MachineName}")
        .WithTls(enabled: false)
        .WithAutoReconnect(enabled: true, maxDelaySeconds: 30)
        .WithKeepAlive(60)
        .WithHandlersFromEntryAssembly())
    .Build();
```

#### Configuration from appsettings.json

```csharp
using Sitas.Edge.Mqtt.Configuration;

var mqttOptions = configuration.GetSection("Mqtt").Get<MqttConnectionOptions>();

var sitasEdge = SitasEdgeBuilder.Create()
    .AddMqttConnection(mqtt => mqtt
        .WithOptions(mqttOptions)
        .WithHandlersFromEntryAssembly())
    .Build();
```

**appsettings.json:**
```json
{
  "Mqtt": {
    "ConnectionName": "mqtt",
    "Host": "broker.hivemq.com",
    "Port": 1883,
    "Username": "user",
    "Password": "password",
    "UseTls": false,
    "KeepAliveSeconds": 60,
    "AutoReconnect": true,
    "ProtocolVersion": "V500"
  }
}
```

---

### Sitas.Edge.EdgePlcDriver - Allen-Bradley PLC

Connect to Allen-Bradley PLCs using the ASComm IoT library from Automated Solutions.

#### Supported PLC Families

- ControlLogix
- CompactLogix
- GuardPLC
- SoftLogix
- Micro800 (Micro820, Micro830, Micro850, Micro870, Micro880)

#### Installation

```bash
dotnet add package Sitas.Edge.EdgePlcDriver

# NOTE: You also need a valid ASComm IoT license from Automated Solutions
# Visit: https://automatedsolutions.com/products/iot/ascommiot/
```

#### Quick Start - Reading and Writing Tags

```csharp
using Sitas.Edge.EdgePlcDriver;

// 1. Create Edge PLC Driver
var driver = EdgePlcDriverBuilder.Create()
    .WithConnectionName("plc1")
    .WithPlc("192.168.1.10", cpuSlot: 0)
    .WithDefaultPollingInterval(100)
    .Build();

// 2. Connect to PLC
await driver.ConnectAsync();

// 3. Read a tag
// Method: Task<TagValue<T>> ReadTagAsync<T>(string tagName, CancellationToken ct = default)
// Returns: TagValue<float> with properties: Value, PreviousValue, Quality, TagName, Timestamp, IsInitialRead, HasChanged
var temperature = await driver.ReadTagAsync<float>("Sensor_Temperature");
Console.WriteLine($"Temperature: {temperature.Value}°C (Quality: {temperature.Quality})");
Console.WriteLine($"Previous: {temperature.PreviousValue}°C");
Console.WriteLine($"Changed: {temperature.HasChanged}");

// 4. Write a tag
// Method: Task WriteTagAsync<T>(string tagName, T value, CancellationToken ct = default)
// Input: tagName (string), value (T - must match PLC tag type)
await driver.WriteTagAsync("Setpoint_Temperature", 75.5f);

// 5. Read multiple tags (type-safe)
// Method: Task<IReadOnlyDictionary<string, TagValue<T>>> ReadTagsAsync<T>(IEnumerable<string> tagNames, CancellationToken ct = default)
// Returns: Dictionary<string, TagValue<int>> - includes metadata (Quality, Timestamp, etc.)
var siteNumbers = await driver.ReadTagsAsync<int>(new[]
{
    "ngpSampleCurrent.pallets[0].cavities[0].siteNumber",
    "ngpSampleCurrent.pallets[0].cavities[1].siteNumber"
});
foreach (var (tagName, tagValue) in siteNumbers)
{
    Console.WriteLine($"{tagName}: {tagValue.Value} (Quality: {tagValue.Quality})");
}

// 6. Write to nested paths
await driver.WriteTagAsync("ngpSampleCurrent.pallets[0].cavities[1].siteNumber", 5);

// 7. Write STRING values (automatic LogixString conversion)
// Note: string is automatically converted to LogixString internally
await driver.WriteTagAsync("ngpSampleCurrent.pallets[0].cavities[1].lotNumber", "LOT-12345");
```

**Key Features:**
- ✅ **Type-safe batch reads**: `ReadTagsAsync<T>()` for same-type tags
- ✅ **Mixed batch reads**: `ReadTagsAsync()` returns `Dictionary<string, object?>`
- ✅ **Nested tag paths**: Direct access to array elements and UDT fields
- ✅ **Full UDT writes**: Modify structure in memory, write entire structure efficiently
- ✅ **80-90% faster**: Batch reads use ASComm groups for optimized performance

#### Handler Examples by Data Type

| Type | C# Handler | Example |
|------|------------|---------|
| `float/REAL` | `TagValue<float>` | Temperature sensor |
| `int/DINT` | `TagValue<int>` | Site numbers, counters |
| `bool/BOOL` | `TagValue<bool>` | Machine running status |
| `UDT` | `TagValue<STRUCT_samples>` | Complex nested UDT |
| `Array` | `TagValue<float[]>` | Zone temperatures |
| `STRING` | `TagValue<LOGIX_STRING>` | Lot numbers, IDs |

#### Working with UDTs (User-Defined Types)

```csharp
using System.Runtime.InteropServices;
using Sitas.Edge.EdgePlcDriver.DataTypes;

// Define UDT matching PLC structure
[StructLayout(LayoutKind.Sequential)]
public class MachineStatus
{
    public Boolean running;           // BOOL
    public Boolean faulted;           // BOOL
    public Int32 productCount;        // DINT
    public Single cycleTime;          // REAL
    public LogixString operatorName = new();  // STRING
}

// Read a UDT - returns strongly typed!
var tag = await connection.ReadTagAsync<MachineStatus>("Machine1_Status");
Console.WriteLine($"Running: {tag.Value.running}, Count: {tag.Value.productCount}");

// Write a UDT
var newStatus = new MachineStatus
{
    running = true,
    faulted = false,
    productCount = 0,
    cycleTime = 2.5f
};
newStatus.operatorName.SetString("John Doe");
await connection.WriteTagAsync("Machine1_Status", newStatus);
```

**UDT Guidelines:**
- ✅ Use `[StructLayout(LayoutKind.Sequential)]`
- ✅ Use public **fields** (not properties)
- ✅ Match field order exactly (same as RSLogix/Studio 5000)
- ✅ Initialize arrays and nested types
- ❌ Don't use auto-properties

#### Data Type Mapping

| PLC Type | C# Type | Bytes |
|----------|---------|-------|
| BOOL | Boolean | 4 (in UDT) |
| DINT | Int32 | 4 |
| REAL | Single/float | 4 |
| LREAL | Double | 8 |
| STRING | LogixString | 88 |

#### Multiple Tag Subscriptions

You can use **multiple `[EdgePlcDriverSubscribe]` attributes** on a single handler to subscribe to multiple tags, as long as they share the same data type:

```csharp
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature_Zone1", pollingIntervalMs: 100)]
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature_Zone2", pollingIntervalMs: 100)]
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature_Zone3", pollingIntervalMs: 100)]
public class MultiZoneTemperatureHandler : IMessageSubscriptionHandler<TagValue<float>>
{
    public Task HandleAsync(TagValue<float> message, IMessageContext context, CancellationToken ct)
    {
        // Use TagName to identify which sensor triggered
        Console.WriteLine($"{message.TagName}: {message.Value}°C");
        return Task.CompletedTask;
    }
}
```

**Note:** All tags must have the same C# type. For different types, use separate handlers or subscribe to the parent UDT.

#### Subscription Modes: Polling vs Unsolicited

**📊 Polling Mode (Default)**
- Regular intervals (e.g., every 100ms)
- Lower PLC CPU overhead
- Suitable for most scenarios

**⚡ Unsolicited Mode (Fast Polling)**
- Very fast polling (10ms) for near real-time response
- Lower latency (10ms vs 100-1000ms typical polling)
- Higher PLC CPU overhead
- Use selectively for critical tags only

```csharp
// Polling mode (default)
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", pollingIntervalMs: 100)]
public class TemperatureHandler : IMessageSubscriptionHandler<TagValue<float>>
{
    // ...
}

// Unsolicited mode - fast polling (10ms) for critical tags
[EdgePlcDriverSubscribe("plc1", "Emergency_Stop", mode: TagSubscriptionMode.Unsolicited)]
public class EmergencyStopHandler : IMessageSubscriptionHandler<TagValue<bool>>
{
    // ...
}
```

#### Configuration Options

```csharp
var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0, backplane: 1)
        .WithDefaultPollingInterval(100)
        .WithConnectionTimeout(10)
        .WithAutoReconnect(enabled: true, maxDelaySeconds: 30)
        .WithHandlersFromEntryAssembly())
    .Build();
```

#### Configuration from appsettings.json

```csharp
using Sitas.Edge.EdgePlcDriver.Configuration;

var plcOptions = configuration.GetSection("Plc1").Get<EdgePlcDriverOptions>();

var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc => plc
        .WithOptions(plcOptions)
        .WithHandlersFromEntryAssembly())
    .Build();
```

**appsettings.json:**
```json
{
  "Plc1": {
    "ConnectionName": "plc1",
    "IpAddress": "192.168.1.10",
    "CpuSlot": 0,
    "Backplane": 1,
    "DefaultPollingIntervalMs": 100,
    "ConnectionTimeoutSeconds": 10,
    "AutoReconnect": true,
    "MaxReconnectDelaySeconds": 30
  }
}
```

#### Nested Tag Paths

Sitas.Edge.EdgePlcDriver supports direct access to nested UDT fields and array elements:

```csharp
// Read nested field
var siteNumber = await connection.ReadTagAsync<int>(
    "ngpSampleCurrent.pallets[0].cavities[1].siteNumber");

// Read nested STRING field
var lotNumber = await connection.ReadTagAsync<LOGIX_STRING>(
    "ngpSampleCurrent.pallets[0].cavities[0].lotNumber");

// Write to nested field
await connection.WriteTagAsync(
    "ngpSampleCurrent.pallets[0].cavities[1].siteNumber", 5);

// Subscribe to nested field changes
[EdgePlcDriverSubscribe("plc1", "ngpSampleCurrent.pallets[0].cavities[0].siteNumber", pollingIntervalMs: 1000)]
public class SiteNumberHandler : IMessageSubscriptionHandler<TagValue<int>>
{
    // ...
}
```

**Path Format:**
- `tagName` - Root tag
- `tagName.fieldName` - UDT field
- `tagName.arrayName[0]` - Array element
- `tagName.arrayName[0].fieldName` - Field in array element

---

## Advanced Features

### EventMediator

Sitas.Edge includes a built-in event system for decoupling business logic and orchestrating workflows across handlers.

#### Basic Event Usage

```csharp
using Sitas.Edge.Core.Events;
using Sitas.Edge.Core.Events.Attributes;

// 1. Define your event data
public record TemperatureChangedEvent(float Temperature);

// 2. Create an event handler
[Event("tempChanged")]
public class TemperatureChangedHandler : IEventHandler<TemperatureChangedEvent>
{
    private readonly ILogger<TemperatureChangedHandler> _logger;
    private readonly ISitasEdge _sitasEdge;
    
    public TemperatureChangedHandler(ILogger<TemperatureChangedHandler> logger, ISitasEdge conduit)
    {
        _logger = logger;
        _sitasEdge = sitasEdge;  // Auto-injected
    }
    
    // Method signature: Task HandleAsync(TEvent eventData, TagReadResults tagValues, CancellationToken ct = default)
    // - eventData: TemperatureChangedEvent (the event data passed to EmitAsync)
    // - tags: TagReadResults (container with PLC tag values if [ReadTag] attributes are used)
    // - ct: CancellationToken (optional)
    public async Task HandleAsync(
        TemperatureChangedEvent eventData,                   // TEvent: Your event data type
        TagReadResults tags,                                 // TagReadResults: Container for PLC tag values
        CancellationToken ct = default)                      // CancellationToken: Optional cancellation token
    {
        _logger.LogInformation("🌡️ Temperature changed: {Temp:F2}°C", eventData.Temperature);
        
        // Access tag values from TagReadResults (if [ReadTag] attributes are used)
        var plcTemp = tags.Get<float>("Sensor_Temperature");  // Returns float? (null if not found)
        if (plcTemp.HasValue)
        {
            _logger.LogInformation("PLC Temperature: {Temp}°C", plcTemp.Value);
        }
        
        // Get MQTT connection via ISitasEdge
        var mqtt = _sitasEdge.GetConnection<IMqttConnection>();
        await mqtt.Publisher.PublishAsync(
            "sensors/temperature", 
            new { temperature = eventData.Temperature }, 
            cancellationToken: ct);
    }
}

// 3. Emit the event from anywhere
// Method: Task EmitAsync<TEvent>(string eventName, TEvent eventData, CancellationToken ct = default)
// Input: eventName (string), eventData (TEvent), ct (CancellationToken, optional)
// Output: Task
await EventMediator.Global.EmitAsync("tempChanged", new TemperatureChangedEvent(25.5f));
```

#### Event Handler Priority

Control the execution order of handlers with the `Priority` parameter:

```csharp
[Event("orderProcessed", Priority = 100)]  // Runs first
public class ValidateOrderHandler : IEventHandler<OrderEvent> { }

[Event("orderProcessed", Priority = 50)]   // Runs second
public class ProcessPaymentHandler : IEventHandler<OrderEvent> { }

[Event("orderProcessed", Priority = 10)]   // Runs last
public class SendEmailHandler : IEventHandler<OrderEvent> { }
```

#### Auto-Reading PLC Tags

Event handlers can automatically read PLC tags before execution:

```csharp
[Event("getMachineData")]
public class MachineDataHandler : IEventHandler<object>
{
    [ReadTag("plc1", "Sensor_Temperature")]
    public float Temperature { get; set; }
    
    [ReadTag("plc1", "Motor_Speed")]
    public int Speed { get; set; }
    
    public Task HandleAsync(object eventData, TagReadResults tags, CancellationToken ct)
    {
        // Properties are auto-populated before this runs
        Console.WriteLine($"Temp: {Temperature}°C, Speed: {Speed} RPM");
        return Task.CompletedTask;
    }
}
```

---

### Loading Handlers from Multiple Assemblies

Sitas.Edge can discover and load handlers from the current assembly (entry assembly) or from different assemblies, including external DLLs loaded at runtime.

#### Loading from Entry Assembly

The simplest approach is to load handlers from the entry assembly (your main application):

```csharp
var sitasEdge = SitasEdgeBuilder.Create()
    .AddMqttConnection(mqtt => mqtt
        .WithConnectionName("mqtt")
        .WithBroker("broker.hivemq.com", 1883)
        .WithHandlersFromEntryAssembly())  // Discovers handlers in the main assembly
    .Build();
```

#### Loading from Multiple Assemblies

You can load handlers from multiple assemblies, including the entry assembly and external assemblies:

```csharp
using System.Reflection;

// Load handlers from entry assembly and an external assembly
var sitasEdge = SitasEdgeBuilder.Create()
    .AddMqttConnection(mqtt => mqtt
        .WithConnectionName("mqtt")
        .WithBroker("broker.hivemq.com", 1883)
        .WithHandlersFromAssemblies(
            Assembly.GetEntryAssembly()!,           // Handlers from main application
            typeof(ExternalHandlers.SomeHandler).Assembly  // Handlers from external assembly
        ))
    .Build();
```

#### Loading from External DLL at Runtime

You can load handlers from an external DLL compiled separately, avoiding circular dependencies:

```csharp
using System.Reflection;

// Load external handlers assembly from DLL path
var externalHandlersPath = Path.Combine(AppContext.BaseDirectory, "ExternalHandlers.dll");
var externalHandlersAssembly = Assembly.LoadFrom(externalHandlersPath);

var sitasEdge = SitasEdgeBuilder.Create()
    .AddMqttConnection(mqtt => mqtt
        .WithConnectionName("mqtt")
        .WithBroker("broker.hivemq.com", 1883)
        .WithHandlersFromAssemblies(
            Assembly.GetEntryAssembly()!,  // Handlers from current assembly
            externalHandlersAssembly       // Handlers from external DLL
        ))
    .Build();
```

**Complete Example: Loading External Handlers**

```csharp
using System.Reflection;

// Load external handlers assembly (if available)
var externalHandlersPath = Path.Combine(AppContext.BaseDirectory, "ExternalHandlers.dll");
Assembly? externalHandlersAssembly = null;

if (File.Exists(externalHandlersPath))
{
    externalHandlersAssembly = Assembly.LoadFrom(externalHandlersPath);
    Console.WriteLine($"✅ Loaded ExternalHandlers assembly from: {externalHandlersPath}");
}
else
{
    Console.WriteLine($"⚠️  ExternalHandlers.dll not found at: {externalHandlersPath}");
}

var sitasEdge = SitasEdgeBuilder.Create()
    .AddMqttConnection(mqtt =>
    {
        mqtt.WithConnectionName("mqtt")
            .WithBroker("broker.hivemq.com", 1883)
            .WithHandlersFromEntryAssembly();  // Always load from entry assembly
        
        // Add external handlers if available
        if (externalHandlersAssembly != null)
        {
            mqtt.WithHandlersFromAssemblies(externalHandlersAssembly);
        }
    })
    .Build();
```

**Benefits:**
- ✅ **Modular Architecture**: Separate handlers into different projects/assemblies
- ✅ **Avoid Circular Dependencies**: Load external handlers without project references
- ✅ **Plugin System**: Load handlers from plugins or extensions at runtime
- ✅ **Flexible Deployment**: Distribute handlers as separate DLLs

**Method Signatures:**
- `WithHandlersFromEntryAssembly()` → `IMqttClientBuilder` / `IEdgePlcDriverBuilder`
  - **Input**: None
  - **Output**: Builder instance for chaining
  - **Behavior**: Discovers handlers in `Assembly.GetEntryAssembly()`

- `WithHandlersFromAssemblies(params Assembly[] assemblies)` → `IMqttClientBuilder` / `IEdgePlcDriverBuilder`
  - **Input**: `assemblies` - Array of `Assembly` objects to scan
  - **Output**: Builder instance for chaining
  - **Behavior**: Discovers handlers in all specified assemblies

---

### Disabling Handlers

You can temporarily disable handlers without removing them from your codebase:

```csharp
using Sitas.Edge.Core.Attributes;
using Sitas.Edge.Mqtt.Attributes;

[DisableHandler]  // 🚫 This handler will NOT be discovered or registered
[MqttSubscribe("mqtt", "test/topic")]
public class DebugHandler : IMessageSubscriptionHandler<MyMessage>
{
    public Task HandleAsync(MyMessage message, IMessageContext context, CancellationToken ct)
    {
        // This handler is completely ignored during discovery
        return Task.CompletedTask;
    }
}
```

**Use Cases:**
- Temporarily disable handlers during development
- Conditionally exclude handlers without deleting code
- Keep experimental handlers in the codebase without activating them

---

## Best Practices

### 1. Choose the Right Activation Strategy

| Scenario | Use | Why |
|----------|-----|-----|
| **Flexible app** | `.WithActivator(func)` | Auto-creates handlers, resolves DI when available |
| **Strict DI validation** | `.WithServiceProvider(sp)` | Enforces all handlers registered in DI |
| **Console app** | `.WithActivator(func)` | Less boilerplate, easier setup |
| **ASP.NET Core** | Either | Both work well |

### 2. Service Design Patterns

**Pattern A: Attribute-Based Handlers (Recommended for production)**
```csharp
[MqttSubscribe("mqtt", "sensors/temperature")]
public class TemperatureHandler : IMessageSubscriptionHandler<TempReading>
{
    private readonly ILogger<TemperatureHandler> _logger;
    
    public TemperatureHandler(ILogger<TemperatureHandler> logger)
    {
        _logger = logger;  // Auto-injected
    }
    
    public Task HandleAsync(TempReading msg, IMessageContext ctx, CancellationToken ct)
    {
        _logger.LogInformation("Temp: {Value}°C", msg.Value);
        return Task.CompletedTask;
    }
}
```

**Pattern B: Inline Subscriptions (For debugging/prototyping)**
```csharp
var debugSub = await connection.SubscribeAsync<TempReading>(
    "sensors/temperature",
    async (msg, ctx, ct) => Console.WriteLine($"Temp: {msg.Value}°C"));

await debugSub.DisposeAsync();  // Remove when done
```

### 3. Avoid Common Pitfalls

❌ **Don't:** Cast `IMessageContext` to check protocol
```csharp
var mqttContext = context as IMqttMessageContext;  // Tight coupling
```

✅ **Do:** Use dependency injection for connections
```csharp
public TemperatureHandler(IMqttConnection mqtt) { }  // Clean injection
```

❌ **Don't:** Register services that only need connections in strict DI containers
```csharp
container.Register<PlcMonitorService>();  // Will fail if IEdgePlcDriver not registered
```

✅ **Do:** Create them manually with `new`
```csharp
var service = new PlcMonitorService(sitasEdge.GetConnection<IEdgePlcDriver>());
```

### 4. Complete Configuration Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sitas.Edge.Core;
using Sitas.Edge.EdgePlcDriver;
using Sitas.Edge.Mqtt;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. Configure Dependency Injection
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IMyService, MyService>();
        var serviceProvider = services.BuildServiceProvider();

        // 2. Configure Sitas.Edge
        var sitasEdge = SitasEdgeBuilder.Create()
            .WithActivator(type => 
                ActivatorUtilities.GetServiceOrCreateInstance(serviceProvider, type))
            .AddMqttConnection(mqtt => mqtt
                .WithConnectionName("mqtt")
                .WithBroker("broker.hivemq.com", 1883)
                .WithHandlersFromEntryAssembly())
            .AddEdgePlcDriverConnection(plc => plc
                .WithConnectionName("plc1")
                .WithPlc("192.168.1.10", cpuSlot: 0)
                .WithDefaultPollingInterval(100)
                .WithHandlersFromEntryAssembly())
            .Build();

        // 3. Connect
        await sitasEdge.ConnectAllAsync();

        // 4. Keep running
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
        await Task.Delay(Timeout.Infinite, cts.Token);

        // 5. Cleanup
        await sitasEdge.DisconnectAllAsync();
        await sitasEdge.DisposeAsync();
    }
}
```

### 5. Integration with ASP.NET Core

```csharp
// Program.cs
using Sitas.Edge.EdgePlcDriver.Configuration;
using Sitas.Edge.Mqtt.Configuration;

builder.Services.AddSitasEdge(builder.Configuration, conduit => conduit
    .AddEdgePlcDriverConnection(plc => plc
        .WithOptions(builder.Configuration.GetSection("Plc1").Get<EdgePlcDriverOptions>()!)
        .WithHandlersFromEntryAssembly())
    .AddMqttConnection(mqtt => mqtt
        .WithOptions(builder.Configuration.GetSection("Mqtt").Get<MqttConnectionOptions>()!)
        .WithHandlersFromEntryAssembly())
);
```

### 6. Building for Distribution

**Option 1: Copy all DLLs** (Simple)
```bash
cp bin/Release/net8.0/*.dll ../MyApp/libs/
```

**Option 2: ILRepack** (Single DLL)
```xml
<ItemGroup>
  <PackageReference Include="ILRepack.Lib.MSBuild.Task" Version="2.0.18.1" />
</ItemGroup>

<Target Name="ILRepack" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
  <ILRepack 
    InputAssemblies="$(TargetPath);AutomatedSolutions.ASCommStd.dll"
    OutputFile="$(TargetPath)"
    Internalize="true" />
</Target>
```

---

## Requirements

- .NET 8.0 or later
- For ASComm: Valid ASComm IoT license from [Automated Solutions](https://automatedsolutions.com/products/iot/ascommiot/)
- For MQTT: MQTTnet 4.x (included via NuGet)

## License

MIT License - See LICENSE file for details.

## Acknowledgments

- ASComm IoT by [Automated Solutions](https://automatedsolutions.com/)
- MQTTnet for MQTT protocol support
