# Sitas.Edge

A modern, extensible .NET service bus library for multi-protocol messaging. Built with **Builder**, **Strategy**, and **Attribute-Based Discovery** patterns for a clean, intuitive developer experience.

## Table of Contents

- [Available Packages & Namespaces](#available-packages--namespaces)
- [Installation](#installation)
- [Creating Connections](#creating-connections)
- [Reading Tags](#reading-tags)
- [Writing Tags](#writing-tags)
- [Inline Subscriptions](#inline-subscriptions)
- [Handler Subscriptions](#handler-subscriptions)
- [Handler Discovery](#handler-discovery)
- [Dependency Injection](#dependency-injection)
- [Handler Examples by Data Type](#handler-examples-by-data-type)
- [EventMediator](#eventmediator)
- [Getting Connections](#getting-connections)
- [MQTT Integration](#mqtt-integration)
- [Advanced Topics](#advanced-topics)

---

## Available Packages & Namespaces

| Package | Description | Key Namespaces |
|---------|-------------|----------------|
| `Sitas.Edge.Core` | Core abstractions and interfaces | `Sitas.Edge.Core`<br>`Sitas.Edge.Core.Abstractions`<br>`Sitas.Edge.Core.Events`<br>`Sitas.Edge.Core.Events.Attributes`<br>`Sitas.Edge.Core.Attributes` |
| `Sitas.Edge.EdgePlcDriver` | Edge PLC Driver for Allen-Bradley PLC communication | `Sitas.Edge.EdgePlcDriver`<br>`Sitas.Edge.EdgePlcDriver.Attributes`<br>`Sitas.Edge.EdgePlcDriver.Messages`<br>`Sitas.Edge.EdgePlcDriver.Configuration`<br>`Sitas.Edge.EdgePlcDriver.DataTypes` |
| `Sitas.Edge.Mqtt` | MQTT protocol support | `Sitas.Edge.Mqtt`<br>`Sitas.Edge.Mqtt.Attributes`<br>`Sitas.Edge.Mqtt.Configuration` |
| `Sitas.Edge.DependencyInjection` | DI extensions for ASP.NET Core | `Sitas.Edge.DependencyInjection` |

**Key Classes and Their Namespaces:**
- `SitasEdgeBuilder` → `Sitas.Edge.Core`
- `IEdgePlcDriver` → `Sitas.Edge.EdgePlcDriver`
- `EdgePlcDriverBuilder` → `Sitas.Edge.EdgePlcDriver`
- `TagValue<T>` → `Sitas.Edge.EdgePlcDriver.Messages`
- `TagQuality` → `Sitas.Edge.EdgePlcDriver.Messages`
- `EdgePlcDriverSubscribeAttribute` → `Sitas.Edge.EdgePlcDriver.Attributes`
- `EdgePlcDriverReadAttribute` → `Sitas.Edge.EdgePlcDriver.Attributes`
- `EdgePlcDriverOptions` → `Sitas.Edge.EdgePlcDriver.Configuration`
- `IMessageContext` → `Sitas.Edge.Core.Abstractions`
- `IEdgePlcDriverMessageContext` → `Sitas.Edge.EdgePlcDriver` (extends `IMessageContext`)
- `ISitasEdge` → `Sitas.Edge.Core.Abstractions`
- `IMqttConnection` → `Sitas.Edge.Mqtt`
- `EventMediator` → `Sitas.Edge.Core.Events`
- `TagReadResults` → `Sitas.Edge.Core.Events`

---

## Installation

### Prerequisites

- .NET 8.0 or later
- For Edge PLC Driver: Valid ASComm IoT license from [Automated Solutions](https://automatedsolutions.com/products/iot/ascommiot/)

### Install Packages

```bash
# Core package (required)
dotnet add package Sitas.Edge.Core

# Edge PLC Driver (for Allen-Bradley PLCs)
dotnet add package Sitas.Edge.EdgePlcDriver

# MQTT support (optional)
dotnet add package Sitas.Edge.Mqtt

# Dependency Injection extensions (optional, for ASP.NET Core)
dotnet add package Sitas.Edge.DependencyInjection
```

---

## Creating Connections

Sitas.Edge supports multiple ways to create and configure connections. Choose the approach that best fits your needs.

### Method 1: Using SitasEdgeBuilder (Recommended)

Use `SitasEdgeBuilder` when you need multiple connections (PLC + MQTT) or want centralized management:

```csharp
using Sitas.Edge.Core;
using Sitas.Edge.EdgePlcDriver;

var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0)
        .WithDefaultPollingInterval(100)
        .WithHandlersFromEntryAssembly())
    .Build();

await sitasEdge.ConnectAllAsync();
```

### Method 2: Using EdgePlcDriverBuilder Individually

Create a standalone PLC connection without SitasEdgeBuilder:

```csharp
using Sitas.Edge.EdgePlcDriver;

var driver = EdgePlcDriverBuilder.Create()
    .WithConnectionName("plc1")
    .WithPlc("192.168.1.10", cpuSlot: 0)
    .WithDefaultPollingInterval(100)
    .Build();

await driver.ConnectAsync();
```

### Method 3: Using WithOptions from appsettings.json

Load configuration from `appsettings.json`:

```csharp
using Sitas.Edge.Core;
using Sitas.Edge.EdgePlcDriver;
using Sitas.Edge.EdgePlcDriver.Configuration;

// Load configuration
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var plcOptions = configuration.GetSection("Plc1").Get<EdgePlcDriverOptions>();

var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc => plc
        .WithOptions(plcOptions!)
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

### Method 4: Using WithOptions from Object

Create options programmatically:

```csharp
using Sitas.Edge.EdgePlcDriver.Configuration;

var options = new EdgePlcDriverOptions
{
    ConnectionName = "plc1",
    IpAddress = "192.168.1.10",
    CpuSlot = 0,
    Backplane = 1,
    DefaultPollingIntervalMs = 100,
    ConnectionTimeoutSeconds = 10,
    AutoReconnect = true,
    MaxReconnectDelaySeconds = 30
};

var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc => plc
        .WithOptions(options)
        .WithHandlersFromEntryAssembly())
    .Build();
```

---

## Reading Tags

Edge PLC Driver provides three methods for reading tags, each returning different data structures.

### ReadTagAsync<T> - Single Tag Read

Reads a single tag and returns `TagValue<T>` with full metadata.

**Signature:**
```csharp
Task<TagValue<T>> ReadTagAsync<T>(
    string tagName,
    CancellationToken cancellationToken = default)
```

**Returns:** `Task<TagValue<T>>` - Contains Value, PreviousValue, Quality, Timestamp, TagName, IsInitialRead, HasChanged

**Example:**
```csharp
using Sitas.Edge.EdgePlcDriver;
using Sitas.Edge.EdgePlcDriver.Messages;

var result = await connection.ReadTagAsync<float>("Sensor_Temperature");

if (result.Quality == TagQuality.Good)
{
    Console.WriteLine($"Temperature: {result.Value}°C");
    Console.WriteLine($"Previous: {result.PreviousValue}°C");
    Console.WriteLine($"Changed: {result.HasChanged}");
    Console.WriteLine($"Timestamp: {result.Timestamp}");
}
else
{
    Console.WriteLine($"Error: Quality = {result.Quality}");
}
```

### ReadTagsAsync<T> - Multiple Tags (Same Type)

Reads multiple tags of the same type in a single batch operation.

**Signature:**
```csharp
Task<IReadOnlyDictionary<string, TagValue<T>>> ReadTagsAsync<T>(
    IEnumerable<string> tagNames,
    CancellationToken cancellationToken = default)
```

**Returns:** `Task<IReadOnlyDictionary<string, TagValue<T>>>` - Dictionary mapping tag names to TagValue objects with metadata

**Example:**
```csharp
var results = await connection.ReadTagsAsync<int>(new[]
{
    "ngpSampleCurrent.pallets[0].cavities[0].siteNumber",
    "ngpSampleCurrent.pallets[0].cavities[1].siteNumber",
    "ngpSampleCurrent.pallets[0].cavities[2].siteNumber"
});

foreach (var (tagName, tagValue) in results)
{
    Console.WriteLine($"{tagName}: {tagValue.Value} (Quality: {tagValue.Quality})");
    if (tagValue.PreviousValue.HasValue)
    {
        Console.WriteLine($"  Previous: {tagValue.PreviousValue.Value}");
    }
}
```

### ReadTagsAsync - Multiple Tags (Mixed Types)

Reads multiple tags of different types. Returns raw values without type conversion.

**Signature:**
```csharp
Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(
    IEnumerable<string> tagNames,
    CancellationToken cancellationToken = default)
```

**Returns:** `Task<IReadOnlyDictionary<string, object?>>` - Dictionary with raw values (requires casting)

**Example:**
```csharp
var results = await connection.ReadTagsAsync(new[]
{
    "Sensor_Temperature",  // float
    "Counter_Production",  // int
    "Machine_Running"      // bool
});

foreach (var (tagName, value) in results)
{
    if (value is float floatValue)
        Console.WriteLine($"{tagName}: {floatValue}°C");
    else if (value is int intValue)
        Console.WriteLine($"{tagName}: {intValue}");
    else if (value is bool boolValue)
        Console.WriteLine($"{tagName}: {boolValue}");
}
```

### TagValue<T> Properties

The `TagValue<T>` class (`Sitas.Edge.EdgePlcDriver.Messages`) contains:

| Property | Type | Description |
|----------|------|-------------|
| `TagName` | `string` | Name of the tag |
| `Value` | `T` | Current value of the tag |
| `PreviousValue` | `T?` | Previous value (null on first read) |
| `Timestamp` | `DateTimeOffset` | When value was read from PLC |
| `Quality` | `TagQuality` | Quality indicator (Good, Uncertain, Bad, CommError, NotFound) |
| `IsInitialRead` | `bool` | True if this is the first read (no previous value) |
| `HasChanged` | `bool` | True if value changed from previous read |

### TagQuality Enum

The `TagQuality` enum (`Sitas.Edge.EdgePlcDriver.Messages`) indicates tag value quality:

| Value | Description |
|-------|-------------|
| `Good` | Value is good and reliable |
| `Uncertain` | Value quality is uncertain |
| `Bad` | Value is bad or unavailable |
| `CommError` | Communication error occurred |
| `NotFound` | Tag was not found in the PLC |

**Example:**
```csharp
var result = await connection.ReadTagAsync<int>("Counter");

switch (result.Quality)
{
    case TagQuality.Good:
        Console.WriteLine($"Value: {result.Value}");
        break;
    case TagQuality.NotFound:
        Console.WriteLine("Tag not found in PLC");
        break;
    case TagQuality.CommError:
        Console.WriteLine("Communication error");
        break;
    default:
        Console.WriteLine($"Quality: {result.Quality}");
        break;
}
```

---

## Writing Tags

### WriteTagAsync<T> - Single Tag Write

Writes a value to a single PLC tag.

**Signature:**
```csharp
Task WriteTagAsync<T>(
    string tagName,
    T value,
    CancellationToken cancellationToken = default)
```

**Inputs:**
- `tagName` (`string`): Tag name or nested path (e.g., `"Setpoint_Temperature"` or `"ngpSampleCurrent.pallets[0].cavities[1].siteNumber"`)
- `value` (`T`): Value to write (must match PLC tag type)
- `cancellationToken` (`CancellationToken`): Optional cancellation token

**Example:**
```csharp
// Write primitive type
await connection.WriteTagAsync("Setpoint_Temperature", 75.5f);

// Write to nested path
await connection.WriteTagAsync("ngpSampleCurrent.pallets[0].cavities[1].siteNumber", 5);

// Write STRING (automatically converts to LogixString)
await connection.WriteTagAsync("LotNumber", "LOT-12345");
```

### WriteTagsAsync - Multiple Tags Write

Writes multiple tag values in a single operation.

**Signature:**
```csharp
Task WriteTagsAsync(
    IReadOnlyDictionary<string, object> tagValues,
    CancellationToken cancellationToken = default)
```

**Inputs:**
- `tagValues` (`IReadOnlyDictionary<string, object>`): Dictionary mapping tag names to values
- `cancellationToken` (`CancellationToken`): Optional cancellation token

**Example:**
```csharp
var values = new Dictionary<string, object>
{
    { "Setpoint_Temperature", 75.5f },
    { "Counter_Production", 100 },
    { "Machine_Running", true }
};

await connection.WriteTagsAsync(values);
```

### Writing Structs (Not Recommended)

While you *can* write entire UDT structures, it's **not recommended** for production code. Prefer writing individual fields:

```csharp
// ❌ Not Recommended: Writing entire UDT
var status = new MachineStatus
{
    running = true,
    productCount = 100,
    cycleTime = 2.5f
};
await connection.WriteTagAsync("Machine1_Status", status);

// ✅ Recommended: Write individual fields
await connection.WriteTagAsync("Machine1_Status.running", true);
await connection.WriteTagAsync("Machine1_Status.productCount", 100);
await connection.WriteTagAsync("Machine1_Status.cycleTime", 2.5f);
```

**Why?** Writing individual fields is:
- More predictable
- Easier to debug
- Less prone to partial writes
- Better error handling

---

## Inline Subscriptions

Subscribe to tag changes dynamically using lambda handlers. Useful for debugging, temporary monitoring, or dynamic scenarios.

### SubscribeAsync<T> Signature

```csharp
Task<IAsyncDisposable> SubscribeAsync<T>(
    string tagName,
    Func<TagValue<T>, IEdgePlcDriverMessageContext, CancellationToken, Task> handler,
    int pollingIntervalMs = 100,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `tagName` (`string`): Tag name to subscribe to
- `handler` (`Func<TagValue<T>, IEdgePlcDriverMessageContext, CancellationToken, Task>`): Handler function that receives:
  - `TagValue<T>`: The tag value with metadata (Value, PreviousValue, Quality, Timestamp, etc.)
  - `IEdgePlcDriverMessageContext`: Context with TagName, Publisher, WriteTagAsync, ReadTagAsync methods
  - `CancellationToken`: Token to cancel the operation
- `pollingIntervalMs` (`int`): Polling interval in milliseconds (default: 100)
- `cancellationToken` (`CancellationToken`): Optional token to cancel the subscription setup

**Returns:** `Task<IAsyncDisposable>` - Disposable subscription for unsubscribing

**Example:**
```csharp
using Sitas.Edge.EdgePlcDriver;

var subscription = await connection.SubscribeAsync<int>(
    "Counter_Production",
    async (tagValue, context, ct) =>
    {
        Console.WriteLine($"Count: {tagValue.Value} (Quality: {tagValue.Quality})");
        Console.WriteLine($"Tag: {context.TagName}");
        
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

### IEdgePlcDriverMessageContext

The context parameter (`Sitas.Edge.EdgePlcDriver`) extends `IMessageContext` with PLC-specific methods:

**Properties:**
- `TagName` (`string`): Tag name that triggered this message
- `Publisher` (`IEdgePlcDriverPublisher`): PLC-specific publisher
- All `IMessageContext` properties: Topic, CorrelationId, ReceivedAt, RawPayload, Metadata

**Methods:**
- `WriteTagAsync<T>(string tagName, T value, CancellationToken ct = default)`: Write to a PLC tag
- `ReadTagAsync<T>(string tagName, CancellationToken ct = default)`: Read from a PLC tag

---

## Handler Subscriptions

Use attribute-based handlers for production code. Handlers are automatically discovered and registered.

### Basic Handler

```csharp
using Sitas.Edge.EdgePlcDriver.Attributes;
using Sitas.Edge.EdgePlcDriver.Messages;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Attributes;

[EdgePlcDriverSubscribe("plc1", "ngpSampleCurrent.pallets[0].cavities[0].lotNumber", 
    pollingIntervalMs: 1000, OnChangeOnly = false)]
public class LotNumberHandler : IMessageSubscriptionHandler<TagValue<LOGIX_STRING>>
{
    private readonly ILogger<LotNumberHandler> _logger;

    public LotNumberHandler(ILogger<LotNumberHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(
        TagValue<LOGIX_STRING> message,
        IMessageContext context,
        CancellationToken ct)
    {
        var lotNumber = message.Value?.Value ?? string.Empty;
        _logger.LogInformation("Lot Number: {LotNumber}", lotNumber);
        return Task.CompletedTask;
    }
}
```

### IMessageSubscriptionHandler<T>

All handlers must implement `IMessageSubscriptionHandler<T>` (`Sitas.Edge.Core.Abstractions`):

```csharp
public interface IMessageSubscriptionHandler<TMessage>
{
    Task HandleAsync(
        TMessage message,
        IMessageContext context,
        CancellationToken cancellationToken = default);
}
```

### HandleAsync Parameters

**For Edge PLC Driver handlers:**
- `message` (`TagValue<T>`): Tag value with metadata (Value, PreviousValue, Quality, Timestamp, TagName, IsInitialRead, HasChanged)
- `context` (`IMessageContext`): Message context with:
  - `Topic` (`string`): Tag name
  - `CorrelationId` (`string?`): Optional correlation ID
  - `ReceivedAt` (`DateTimeOffset`): Timestamp when received
  - `RawPayload` (`ReadOnlyMemory<byte>`): Raw payload bytes
  - `Publisher` (`IMessagePublisher`): Publisher for sending messages
  - `Metadata` (`IReadOnlyDictionary<string, string>`): Additional metadata
- `ct` (`CancellationToken`): Optional cancellation token

### Multiple Subscriptions

Subscribe to multiple tags on the same handler (all tags must be the same type):

```csharp
[EdgePlcDriverSubscribe("plc1", "ngpSampleCurrent.pallets[0].cavities[0].siteNumber", 
    pollingIntervalMs: 1000, OnChangeOnly = false)]
[EdgePlcDriverSubscribe("plc1", "ngpSampleCurrent.pallets[0].cavities[1].siteNumber", 
    pollingIntervalMs: 1000, OnChangeOnly = false)]
[EdgePlcDriverSubscribe("plc1", "ngpSampleCurrent.pallets[0].cavities[2].siteNumber", 
    pollingIntervalMs: 1000, OnChangeOnly = false)]
public class SiteNumberHandler : IMessageSubscriptionHandler<TagValue<int>>
{
    public Task HandleAsync(
        TagValue<int> message,
        IMessageContext context,
        CancellationToken ct)
    {
        // Use message.TagName to identify which tag triggered
        Console.WriteLine($"{message.TagName}: {message.Value}");
        return Task.CompletedTask;
    }
}
```

### EdgePlcDriverSubscribe Attribute Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `connectionName` | `string` | Required | Name of the PLC connection (must match builder configuration) |
| `tagName` | `string` | Required | PLC tag name to subscribe to |
| `pollingIntervalMs` | `int` | 0 (uses default) | Polling interval in milliseconds |
| `OnChangeOnly` | `bool` | `true` | If `true`, handler only fires when value changes. If `false`, fires on every poll |
| `mode` | `TagSubscriptionMode` | `Polling` | Subscription mode: `Polling` (default) or `Unsolicited` (fast polling) |

**OnChangeOnly Examples:**
```csharp
// Fires only when value changes
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", OnChangeOnly = true)]

// Fires on every poll cycle (even if value unchanged)
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", OnChangeOnly = false)]
```

### DisableHandler Attribute

Temporarily disable a handler without removing it:

```csharp
using Sitas.Edge.Core.Attributes;

[DisableHandler]  // Handler will NOT be discovered or registered
[EdgePlcDriverSubscribe("plc1", "Test_Tag", pollingIntervalMs: 1000)]
public class TestHandler : IMessageSubscriptionHandler<TagValue<int>>
{
    // This handler is completely ignored
}
```

---

## Handler Discovery

Sitas.Edge automatically discovers handlers from assemblies. You can load handlers from the current assembly or external assemblies.

### WithHandlersFromEntryAssembly()

Discovers handlers in the entry assembly (your main application):

```csharp
var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0)
        .WithHandlersFromEntryAssembly())  // Discovers handlers in main assembly
    .Build();
```

### WithHandlersFromAssemblies()

Load handlers from multiple assemblies:

```csharp
using System.Reflection;

var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0)
        .WithHandlersFromAssemblies(
            Assembly.GetEntryAssembly()!,           // Handlers from main application
            typeof(ExternalHandlers.SomeHandler).Assembly  // Handlers from external assembly
        ))
    .Build();
```

### Loading from External DLL at Runtime

Load handlers from an external DLL compiled separately (avoids circular dependencies):

```csharp
using System.Reflection;

// Load external handlers assembly from DLL path
var externalHandlersPath = Path.Combine(AppContext.BaseDirectory, "ExternalHandlers.dll");
var externalHandlersAssembly = Assembly.LoadFrom(externalHandlersPath);

var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0)
        .WithHandlersFromAssemblies(
            Assembly.GetEntryAssembly()!,  // Handlers from current assembly
            externalHandlersAssembly       // Handlers from external DLL
        ))
    .Build();
```

**Complete Example:**
```csharp
using System.Reflection;

var externalHandlersPath = Path.Combine(AppContext.BaseDirectory, "ExternalHandlers.dll");
Assembly? externalHandlersAssembly = null;

if (File.Exists(externalHandlersPath))
{
    externalHandlersAssembly = Assembly.LoadFrom(externalHandlersPath);
    Console.WriteLine($"✅ Loaded ExternalHandlers assembly");
}
else
{
    Console.WriteLine($"⚠️  ExternalHandlers.dll not found");
}

var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc =>
    {
        plc.WithConnectionName("plc1")
           .WithPlc("192.168.1.10", cpuSlot: 0)
           .WithHandlersFromEntryAssembly();  // Always load from entry assembly
        
        // Add external handlers if available
        if (externalHandlersAssembly != null)
        {
            plc.WithHandlersFromAssemblies(externalHandlersAssembly);
        }
    })
    .Build();
```

---

## Dependency Injection

Sitas.Edge supports flexible dependency injection with any DI container.

### WithActivator

The activator receives a `Func<Type, object>` that creates instances:

```csharp
var sitasEdge = SitasEdgeBuilder.Create()
    .WithActivator(type => 
    {
        // Your custom activation logic
        // Return an instance of the type
        return Activator.CreateInstance(type)!;
    })
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0)
        .WithHandlersFromEntryAssembly())
    .Build();
```

### Examples from Different DI Containers

**Microsoft.Extensions.DependencyInjection (NativeDI):**
```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<IMyService, MyService>();
var serviceProvider = services.BuildServiceProvider();

var sitasEdge = SitasEdgeBuilder.Create()
    .WithActivator(type => 
        ActivatorUtilities.GetServiceOrCreateInstance(serviceProvider, type))
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0)
        .WithHandlersFromEntryAssembly())
    .Build();
```

**Autofac:**
```csharp
using Autofac;

var containerBuilder = new ContainerBuilder();
containerBuilder.RegisterType<MyService>().As<IMyService>();
var container = containerBuilder.Build();

var sitasEdge = SitasEdgeBuilder.Create()
    .WithActivator(type => container.Resolve(type))
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0)
        .WithHandlersFromEntryAssembly())
    .Build();
```

**SimpleInjector:**
```csharp
using SimpleInjector;

var container = new Container();
container.Register<IMyService, MyService>();
container.Verify();

var sitasEdge = SitasEdgeBuilder.Create()
    .WithActivator(type => container.GetInstance(type))
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0)
        .WithHandlersFromEntryAssembly())
    .Build();
```

### Auto-Injection

Sitas.Edge automatically injects common dependencies into your handlers **without requiring manual DI registration**.

#### Automatically Injected Types

| Type | Namespace | Description |
|------|-----------|-------------|
| `ISitasEdge` | `Sitas.Edge.Core.Abstractions` | The main Sitas.Edge instance |
| `IMqttConnection` | `Sitas.Edge.Mqtt` | MQTT connection (if configured) |
| `IEdgePlcDriver` | `Sitas.Edge.EdgePlcDriver` | PLC connection (if configured) |
| `IMessagePublisher` | `Sitas.Edge.Core.Abstractions` | Generic publisher from any configured connection |
| `IMqttPublisher` | `Sitas.Edge.Mqtt` | MQTT-specific publisher |
| `IEdgePlcDriverPublisher` | `Sitas.Edge.EdgePlcDriver` | PLC-specific publisher |
| `ILogger<T>` | `Microsoft.Extensions.Logging` | Logger (uses `NullLogger` if not registered) |

#### Auto-Injection Without DI Provider

Even without a DI container, Sitas.Edge can inject the types listed above:

```csharp
// No DI container needed!
var sitasEdge = SitasEdgeBuilder.Create()
    .AddEdgePlcDriverConnection(plc => plc
        .WithConnectionName("plc1")
        .WithPlc("192.168.1.10", cpuSlot: 0)
        .WithHandlersFromEntryAssembly())
    .Build();  // ✅ Handlers with auto-injected dependencies work without .WithActivator()
```

**Example Handler with Auto-Injection:**
```csharp
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", pollingIntervalMs: 100)]
public class TemperatureHandler : IMessageSubscriptionHandler<TagValue<float>>
{
    private readonly ILogger<TemperatureHandler> _logger;  // Auto-injected
    private readonly IEdgePlcDriver _plc;                 // Auto-injected
    private readonly ISitasEdge _sitasEdge;                // Auto-injected
    
    public TemperatureHandler(
        ILogger<TemperatureHandler> logger,
        IEdgePlcDriver plc,
        ISitasEdge sitasEdge)
    {
        _logger = logger;      // ✅ NullLogger if not registered
        _plc = plc;            // ✅ Auto-injected by Sitas.Edge
        _sitasEdge = sitasEdge; // ✅ Auto-injected by Sitas.Edge
    }
    
    public async Task HandleAsync(
        TagValue<float> message,
        IMessageContext context,
        CancellationToken ct)
    {
        _logger.LogInformation("Temperature: {Value}°C", message.Value);
        
        // Use injected PLC connection
        var otherTag = await _plc.ReadTagAsync<int>("Counter", ct);
        
        // Get MQTT connection if available
        try
        {
            var mqtt = _sitasEdge.GetConnection<IMqttConnection>();
            await mqtt.Publisher.PublishAsync("sensors/temp", 
                new { temp = message.Value }, ct);
        }
        catch
        {
            // MQTT not configured, ignore
        }
    }
}
```

---

## Handler Examples by Data Type

Different PLC data types require different handler message types:

| PLC Type | C# Type | Handler Message Type | Example |
|----------|---------|---------------------|---------|
| `float/REAL` | `float` | `TagValue<float>` | Temperature sensor |
| `int/DINT` | `int` | `TagValue<int>` | Site numbers, counters |
| `bool/BOOL` | `bool` | `TagValue<bool>` | Machine running status |
| `UDT` | Custom struct | `TagValue<STRUCT_samples>` | Complex nested UDT |
| `Array` | Array type | `TagValue<float[]>` | Zone temperatures |
| `STRING` | `LOGIX_STRING` | `TagValue<LOGIX_STRING>` | Lot numbers, IDs |

### Example: float/REAL

```csharp
using Sitas.Edge.EdgePlcDriver.Attributes;
using Sitas.Edge.EdgePlcDriver.Messages;
using Sitas.Edge.Core.Abstractions;

[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", pollingIntervalMs: 100)]
public class TemperatureHandler : IMessageSubscriptionHandler<TagValue<float>>
{
    public Task HandleAsync(
        TagValue<float> message,
        IMessageContext context,
        CancellationToken ct)
    {
        Console.WriteLine($"Temperature: {message.Value}°C");
        return Task.CompletedTask;
    }
}
```

### Example: int/DINT

```csharp
[EdgePlcDriverSubscribe("plc1", "Counter_Production", pollingIntervalMs: 500)]
public class CounterHandler : IMessageSubscriptionHandler<TagValue<int>>
{
    public Task HandleAsync(
        TagValue<int> message,
        IMessageContext context,
        CancellationToken ct)
    {
        Console.WriteLine($"Count: {message.Value}");
        return Task.CompletedTask;
    }
}
```

### Example: bool/BOOL

```csharp
[EdgePlcDriverSubscribe("plc1", "Machine_Running", pollingIntervalMs: 1000)]
public class MachineStatusHandler : IMessageSubscriptionHandler<TagValue<bool>>
{
    public Task HandleAsync(
        TagValue<bool> message,
        IMessageContext context,
        CancellationToken ct)
    {
        Console.WriteLine($"Machine Running: {message.Value}");
        return Task.CompletedTask;
    }
}
```

### Example: STRING (LOGIX_STRING)

```csharp
using Sitas.Edge.EdgePlcDriver.DataTypes;

[EdgePlcDriverSubscribe("plc1", "LotNumber", pollingIntervalMs: 1000)]
public class LotNumberHandler : IMessageSubscriptionHandler<TagValue<LOGIX_STRING>>
{
    public Task HandleAsync(
        TagValue<LOGIX_STRING> message,
        IMessageContext context,
        CancellationToken ct)
    {
        var lotNumber = message.Value?.Value ?? string.Empty;
        Console.WriteLine($"Lot Number: {lotNumber}");
        return Task.CompletedTask;
    }
}
```

### Example: UDT (User-Defined Type)

```csharp
using System.Runtime.InteropServices;
using Sitas.Edge.EdgePlcDriver.DataTypes;

[StructLayout(LayoutKind.Sequential)]
public class MachineStatus
{
    public Boolean running;
    public Boolean faulted;
    public Int32 productCount;
    public Single cycleTime;
    public LogixString operatorName = new();
}

[EdgePlcDriverSubscribe("plc1", "Machine1_Status", pollingIntervalMs: 1000)]
public class MachineStatusHandler : IMessageSubscriptionHandler<TagValue<MachineStatus>>
{
    public Task HandleAsync(
        TagValue<MachineStatus> message,
        IMessageContext context,
        CancellationToken ct)
    {
        var status = message.Value;
        Console.WriteLine($"Running: {status.running}, Count: {status.productCount}");
        Console.WriteLine($"Operator: {status.operatorName.Value}");
        return Task.CompletedTask;
    }
}
```

### Example: Array

```csharp
[EdgePlcDriverSubscribe("plc1", "Zone_Temperatures", pollingIntervalMs: 500)]
public class ZoneTemperatureHandler : IMessageSubscriptionHandler<TagValue<float[]>>
{
    public Task HandleAsync(
        TagValue<float[]> message,
        IMessageContext context,
        CancellationToken ct)
    {
        for (int i = 0; i < message.Value.Length; i++)
        {
            Console.WriteLine($"Zone {i}: {message.Value[i]}°C");
        }
        return Task.CompletedTask;
    }
}
```

---

## EventMediator

Sitas.Edge includes a built-in event system for decoupling business logic and orchestrating workflows across handlers.

### Basic Event Usage

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
    
    public TemperatureChangedHandler(ILogger<TemperatureChangedHandler> logger)
    {
        _logger = logger;
    }
    
    public Task HandleAsync(
        TemperatureChangedEvent eventData,
        TagReadResults tags,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Temperature changed: {Temp}°C", eventData.Temperature);
        return Task.CompletedTask;
    }
}

// 3. Emit the event from anywhere
await EventMediator.Global.EmitAsync("tempChanged", new TemperatureChangedEvent(25.5f));
```

### Auto-Reading PLC Tags with EdgePlcDriverRead

Event handlers can automatically read PLC tags before execution:

```csharp
using Sitas.Edge.EdgePlcDriver.Attributes;
using Sitas.Edge.Core.Events;
using Sitas.Edge.Core.Events.Attributes;

[Event("tempChanged")]
[EdgePlcDriverRead("plc1", "Sensor_Temperature", typeof(float))]
public class TemperatureChangedHandler : IEventHandler<TemperatureChangedEvent>
{
    private readonly ILogger<TemperatureChangedHandler> _logger;
    
    public TemperatureChangedHandler(ILogger<TemperatureChangedHandler> logger)
    {
        _logger = logger;
    }
    
    public Task HandleAsync(
        TemperatureChangedEvent eventData,
        TagReadResults tags,
        CancellationToken cancellationToken = default)
    {
        // Access tag value from TagReadResults
        var plcTemp = tags.Get<float>("Sensor_Temperature");
        if (plcTemp.HasValue)
        {
            _logger.LogInformation("PLC Temperature: {Temp}°C", plcTemp.Value);
        }
        
        _logger.LogInformation("Event Temperature: {Temp}°C", eventData.Temperature);
        return Task.CompletedTask;
    }
}
```

### TagReadResults

The `TagReadResults` class (`Sitas.Edge.Core.Events`) provides access to tag values:

**Methods:**
- `Get<T>(string tagName)`: Returns `T?` - Gets tag value, returns `default(T)` if not found
- `GetTagValue<T>(string tagName)`: Returns `TagReadValue<T>?` - Gets tag value with full metadata (quality, timestamp)
- `GetRequired<T>(string tagName)`: Returns `T` - Gets tag value, throws `KeyNotFoundException` if not found
- `Contains(string tagName)`: Returns `bool` - Checks if tag exists

**Properties:**
- `TagNames` (`IEnumerable<string>`): All tag names in results
- `Count` (`int`): Number of tag values
- `AllGoodQuality` (`bool`): True if all tags have good quality
- `BadQualityTags` (`IEnumerable<string>`): Tags with bad quality

**Example:**
```csharp
[Event("getMachineData")]
[EdgePlcDriverRead("plc1", "Sensor_Temperature", typeof(float))]
[EdgePlcDriverRead("plc1", "Motor_Speed", typeof(int))]
public class MachineDataHandler : IEventHandler<object>
{
    public Task HandleAsync(
        object eventData,
        TagReadResults tags,
        CancellationToken ct)
    {
        // Get tag values
        var temp = tags.Get<float>("Sensor_Temperature");
        var speed = tags.Get<int>("Motor_Speed");
        
        // Get with metadata
        var tempWithMeta = tags.GetTagValue<float>("Sensor_Temperature");
        if (tempWithMeta?.IsGood == true)
        {
            Console.WriteLine($"Temp: {tempWithMeta.Value}°C (Quality: {tempWithMeta.Quality})");
        }
        
        // Check if all tags are good
        if (tags.AllGoodQuality)
        {
            Console.WriteLine("All tags have good quality");
        }
        
        return Task.CompletedTask;
    }
}
```

---

## Getting Connections

Access connections from `ISitasEdge` to use them in your code.

### GetConnection<T>

Get a connection by type:

```csharp
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.EdgePlcDriver;
using Sitas.Edge.Mqtt;

var mqttConnection = sitasEdge.GetConnection<IMqttConnection>();
var plcConnection = sitasEdge.GetConnection<IEdgePlcDriver>();
```

### Methods Available on IEdgePlcDriver

The `IEdgePlcDriver` interface (`Sitas.Edge.EdgePlcDriver`) provides:

**Reading:**
- `ReadTagAsync<T>(string tagName, CancellationToken ct = default)`: Read single tag
- `ReadTagsAsync<T>(IEnumerable<string> tagNames, CancellationToken ct = default)`: Read multiple tags (same type)
- `ReadTagsAsync(IEnumerable<string> tagNames, CancellationToken ct = default)`: Read multiple tags (mixed types)

**Writing:**
- `WriteTagAsync<T>(string tagName, T value, CancellationToken ct = default)`: Write single tag
- `WriteTagsAsync(IReadOnlyDictionary<string, object> tagValues, CancellationToken ct = default)`: Write multiple tags

**Subscribing:**
- `SubscribeAsync<T>(string tagName, Func<TagValue<T>, IEdgePlcDriverMessageContext, CancellationToken, Task> handler, int pollingIntervalMs = 100, CancellationToken ct = default)`: Inline subscription

**Properties:**
- `Publisher` (`IEdgePlcDriverPublisher`): Publisher for writing tags
- `ConnectionName` (`string`): Connection name
- `IsConnected` (`bool`): Connection status
- `State` (`ConnectionState`): Connection state
- `IpAddress` (`string`): PLC IP address
- `RoutePath` (`string`): Route path to PLC

**Example:**
```csharp
var plc = sitasEdge.GetConnection<IEdgePlcDriver>();

// Read a tag
var temp = await plc.ReadTagAsync<float>("Sensor_Temperature");

// Write a tag
await plc.WriteTagAsync("Setpoint_Temperature", 75.5f);

// Subscribe inline
var sub = await plc.SubscribeAsync<int>("Counter", 
    async (tagValue, context, ct) => 
    {
        Console.WriteLine($"Count: {tagValue.Value}");
    },
    pollingIntervalMs: 500);
```

### Methods Available on IMqttConnection

The `IMqttConnection` interface (`Sitas.Edge.Mqtt`) provides:

**Subscribing:**
- `SubscribeAsync<TMessage>(string topic, Func<TMessage, IMessageContext, CancellationToken, Task> handler, QualityOfService qos = QualityOfService.AtLeastOnce, CancellationToken ct = default)`: Typed subscription
- `SubscribeAsync(string topic, Func<ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task> handler, QualityOfService qos = QualityOfService.AtLeastOnce, CancellationToken ct = default)`: Raw bytes subscription
- `SubscribeAsync(string topic, Func<string, ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task> handler, QualityOfService qos = QualityOfService.AtLeastOnce, CancellationToken ct = default)`: Topic-aware subscription

**Properties:**
- `Publisher` (`IMqttPublisher`): Publisher for publishing messages

**Example:**
```csharp
var mqtt = sitasEdge.GetConnection<IMqttConnection>();

// Subscribe
var sub = await mqtt.SubscribeAsync<TemperatureReading>(
    "sensors/temperature",
    async (message, context, ct) =>
    {
        Console.WriteLine($"Temp: {message.Value}°C");
    });

// Publish
await mqtt.Publisher.PublishAsync("sensors/temperature",
    new TemperatureReading { Value = 25.5f });
```

---

## MQTT Integration

### Basic MQTT Usage

```csharp
using Sitas.Edge.Core;
using Sitas.Edge.Mqtt;
using Sitas.Edge.Mqtt.Attributes;

// Handler for MQTT messages
[MqttSubscribe("mqtt", "sensors/temperature")]
public class MqttTemperatureHandler : IMessageSubscriptionHandler<TemperatureReading>
{
    public Task HandleAsync(
        TemperatureReading message,
        IMessageContext context,
        CancellationToken ct)
    {
        Console.WriteLine($"Temp: {message.Value}°C from topic: {context.Topic}");
        return Task.CompletedTask;
    }
}

// Configure MQTT connection
var sitasEdge = SitasEdgeBuilder.Create()
    .AddMqttConnection(mqtt => mqtt
        .WithConnectionName("mqtt")
        .WithBroker("broker.hivemq.com", 1883)
        .WithHandlersFromEntryAssembly())
    .Build();
```

### Calling Other Connections from Handlers

From a PLC handler, you can access other connections (like MQTT) using `ISitasEdge`:

```csharp
using Sitas.Edge.EdgePlcDriver.Attributes;
using Sitas.Edge.EdgePlcDriver.Messages;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Mqtt;

[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", pollingIntervalMs: 100)]
public class TemperatureHandler : IMessageSubscriptionHandler<TagValue<float>>
{
    private readonly ISitasEdge _sitasEdge;
    private readonly ILogger<TemperatureHandler> _logger;
    
    public TemperatureHandler(ISitasEdge sitasEdge, ILogger<TemperatureHandler> logger)
    {
        _sitasEdge = sitasEdge;  // Auto-injected
        _logger = logger;
    }
    
    public async Task HandleAsync(
        TagValue<float> message,
        IMessageContext context,
        CancellationToken ct)
    {
        _logger.LogInformation("PLC Temperature: {Temp}°C", message.Value);
        
        // Get MQTT connection and publish
        try
        {
            var mqtt = _sitasEdge.GetConnection<IMqttConnection>();
            await mqtt.Publisher.PublishAsync("sensors/temperature",
                new { temperature = message.Value, timestamp = DateTimeOffset.UtcNow },
                cancellationToken: ct);
            
            _logger.LogInformation("Published to MQTT");
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning("MQTT connection not configured");
        }
    }
}
```

**Alternative: Inject IMqttConnection Directly**

You can also inject `IMqttConnection` directly (if configured):

```csharp
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", pollingIntervalMs: 100)]
public class TemperatureHandler : IMessageSubscriptionHandler<TagValue<float>>
{
    private readonly IMqttConnection _mqtt;  // Auto-injected if MQTT is configured
    private readonly ILogger<TemperatureHandler> _logger;
    
    public TemperatureHandler(IMqttConnection mqtt, ILogger<TemperatureHandler> logger)
    {
        _mqtt = mqtt;
        _logger = logger;
    }
    
    public async Task HandleAsync(
        TagValue<float> message,
        IMessageContext context,
        CancellationToken ct)
    {
        // Publish directly using injected connection
        await _mqtt.Publisher.PublishAsync("sensors/temperature",
            new { temperature = message.Value },
            cancellationToken: ct);
    }
}
```

---

## Advanced Topics

### Subscription Modes

Edge PLC Driver supports two subscription modes:

**Polling Mode (Default)**
- Tag is read at regular intervals (e.g., every 100ms)
- Lower PLC CPU overhead
- Suitable for most scenarios
- Default mode

```csharp
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", 
    pollingIntervalMs: 100, 
    mode: TagSubscriptionMode.Polling)]
public class TemperatureHandler : IMessageSubscriptionHandler<TagValue<float>>
{
    // ...
}
```

**Unsolicited Mode (Fast Polling)**
- Very fast polling (10ms) for near real-time response
- Lower latency (10ms vs 100-1000ms typical polling)
- Higher PLC CPU overhead
- Use selectively for critical tags only

```csharp
[EdgePlcDriverSubscribe("plc1", "Emergency_Stop", 
    pollingIntervalMs: 10,
    mode: TagSubscriptionMode.Unsolicited)]
public class EmergencyStopHandler : IMessageSubscriptionHandler<TagValue<bool>>
{
    // ...
}
```

### Supported PLC Families

- **ControlLogix** (L6x, L7x, L8x series)
- **CompactLogix** (L3x series)
- **GuardPLC** (Safety controllers)
- **SoftLogix** (PC-based emulator)
- **Micro800** (Micro820, Micro830, Micro850, Micro870, Micro880)

### Nested Tag Paths

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
[EdgePlcDriverSubscribe("plc1", "ngpSampleCurrent.pallets[0].cavities[0].siteNumber", 
    pollingIntervalMs: 1000)]
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

### Working with UDTs (User-Defined Types)

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

// Read a UDT
var tag = await connection.ReadTagAsync<MachineStatus>("Machine1_Status");
Console.WriteLine($"Running: {tag.Value.running}, Count: {tag.Value.productCount}");

// Write a UDT (not recommended - prefer individual fields)
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

### Data Type Mapping

| PLC Type | C# Type | Bytes |
|----------|---------|-------|
| BOOL | Boolean | 4 (in UDT) |
| DINT | Int32 | 4 |
| REAL | Single/float | 4 |
| LREAL | Double | 8 |
| STRING | LogixString | 88 |

### Best Practices

1. **Use Attribute-Based Handlers for Production**: More maintainable and testable
2. **Use Inline Subscriptions for Debugging**: Quick to add/remove
3. **Prefer Writing Individual Fields**: Instead of entire UDTs
4. **Check TagQuality**: Always verify `Quality == TagQuality.Good` before using values
5. **Use OnChangeOnly for Efficiency**: Reduces unnecessary handler invocations
6. **Handle Missing Connections Gracefully**: Use try-catch when accessing optional connections

### Troubleshooting

**Tag not found:**
- Verify tag name matches PLC exactly (case-sensitive)
- Check tag scope (Program:MainProgram.TagName vs TagName)
- Ensure PLC connection is established

**Quality is Bad/CommError:**
- Check PLC connection status
- Verify IP address and CPU slot
- Check PLC permissions/security settings

**Handler not firing:**
- Verify handler is discovered (check logs)
- Ensure `connectionName` in attribute matches builder configuration
- Check if handler is disabled with `[DisableHandler]`

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
