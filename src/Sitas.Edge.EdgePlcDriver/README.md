# Conduit.EdgePlcDriver

Edge PLC Driver for Conduit - Allen-Bradley ControlLogix PLC communication with clean abstraction layer for industrial edge computing scenarios.

## Overview

Conduit.EdgePlcDriver provides high-level abstractions for PLC communication with Allen-Bradley ControlLogix controllers using the ASComm IoT library from Automated Solutions. It supports attribute-based tag subscription discovery, automatic polling, and type-safe tag reading/writing for industrial edge computing applications.

## Supported PLCs

- **ControlLogix** (L6x, L7x, L8x series)
- **CompactLogix** (L3x series)
- **GuardPLC** (Safety controllers)
- **SoftLogix** (PC-based emulator)
- **Micro800** (Micro820, Micro830, Micro850, Micro870, Micro880)

## Prerequisites

1. **ASComm IoT License**: Purchase from [Automated Solutions](https://automatedsolutions.com/products/iot/ascommiot/)
2. **.NET 8.0** or later

## Installation

```bash
dotnet add package Conduit.EdgePlcDriver
```

## Quick Start

### Basic Connection

```csharp
using Conduit.EdgePlcDriver;

var driver = EdgePlcDriverBuilder.Create()
    .WithConnectionName("plc1")
    .WithPlc("192.168.1.10", cpuSlot: 0)
    .Build();

await driver.ConnectAsync();

// Read a tag
var temp = await driver.ReadTagAsync<float>("Sensor_Temp");
Console.WriteLine($"Temperature: {temp.Value}");

// Write a tag
await driver.WriteTagAsync("Setpoint", 75.5f);

await driver.DisposeAsync();
```

### Attribute-Based Handlers

```csharp
using Conduit.EdgePlcDriver.Attributes;
using Conduit.EdgePlcDriver.Messages;
using Conduit.Core.Abstractions;
using Conduit.EdgePlcDriver;

[EdgePlcDriverSubscribe("plc1", "Alarm_Status", pollingIntervalMs: 100, OnChangeOnly = true)]
public class AlarmHandler : IMessageSubscriptionHandler<TagValue<bool>>
{
    public Task HandleAsync(TagValue<bool> message, IMessageContext context, CancellationToken ct)
    {
        if (message.Value)
            Console.WriteLine("⚠️ ALARM ACTIVE!");
        return Task.CompletedTask;
    }
}
```

## Handler Examples by Data Type

### Primitive Types

#### float/REAL - Temperature Sensor
```csharp
[EdgePlcDriverSubscribe("plc1", "Sensor_Temperature", pollingIntervalMs: 100, OnChangeOnly = true)]
public class TemperatureSensorHandler : IMessageSubscriptionHandler<TagValue<float>>
{
    private readonly IConduit _conduit;

    public TemperatureSensorHandler(IConduit conduit)
    {
        _conduit = conduit;
    }

    public async Task HandleAsync(TagValue<float> message, IMessageContext context, CancellationToken ct)
    {
        Console.WriteLine($"🌡️ Temperature: {message.Value:F2}°C");
        
        if (message.Value > 85.0f)
        {
            // No uses casts/instanceof con context: inyecta dependencias.
            var plc = _conduit.GetConnection<IEdgePlcDriver>();
            await plc.WriteTagAsync("Alarm_HighTemp", true, ct);
        }
    }
}
```

#### double/LREAL - Motor Speed with Deadband
```csharp
[EdgePlcDriverSubscribe("plc1", "Motor_Speed", pollingIntervalMs: 50, OnChangeOnly = true, Deadband = 0.5)]
public class MotorSpeedHandler : IMessageSubscriptionHandler<TagValue<double>>
{
    public Task HandleAsync(TagValue<double> message, IMessageContext context, CancellationToken ct)
    {
        var delta = message.PreviousValue is double prev ? message.Value - prev : 0.0;
        Console.WriteLine($"Motor: {message.Value:F1} RPM (Δ{delta:+0.0;-0.0})");
        return Task.CompletedTask;
    }
}
```

#### int/DINT - Production Counter
```csharp
[EdgePlcDriverSubscribe("plc1", "Production_Count", pollingIntervalMs: 1000, OnChangeOnly = true)]
public class ProductionCounterHandler : IMessageSubscriptionHandler<TagValue<int>>
{
    public Task HandleAsync(TagValue<int> message, IMessageContext context, CancellationToken ct)
    {
        if (message.IsInitialRead)
            Console.WriteLine($"📊 Counter initialized: {message.Value}");
        else
            Console.WriteLine($"📊 Count: {message.Value} (+{(message.PreviousValue is int prev ? message.Value - prev : 0)})");
        return Task.CompletedTask;
    }
}
```

#### bool/BOOL - Machine Status
```csharp
[EdgePlcDriverSubscribe("plc1", "Machine_Running", pollingIntervalMs: 200, OnChangeOnly = true)]
public class MachineStatusHandler : IMessageSubscriptionHandler<TagValue<bool>>
{
    public Task HandleAsync(TagValue<bool> message, IMessageContext context, CancellationToken ct)
    {
        Console.WriteLine($"🏭 Machine: {(message.Value ? "🟢 RUNNING" : "🔴 STOPPED")}");
        return Task.CompletedTask;
    }
}
```

### UDTs (User-Defined Types)

#### Simple UDT - Machine Status Structure
```csharp
// 1. Define UDT (must match PLC exactly)
[StructLayout(LayoutKind.Sequential)]
public class MachineStatusUdt
{
    public Boolean running;
    public Boolean faulted;
    public Int32 productCount;
    public Single cycleTime;
    public LogixString operatorName = new();
}

// 2. Create handler with TagValue<YourUdt>
[EdgePlcDriverSubscribe("plc1", "Machine_Status_UDT", pollingIntervalMs: 500)]
public class MachineStatusUdtHandler : IMessageSubscriptionHandler<TagValue<MachineStatusUdt>>
{
    public Task HandleAsync(TagValue<MachineStatusUdt> message, IMessageContext context, CancellationToken ct)
    {
        var status = message.Value; // Directly typed!
        Console.WriteLine($"📦 Running: {status.running} | Count: {status.productCount} | Operator: {status.operatorName.Value}");
        return Task.CompletedTask;
    }
}
```

#### UDT with Arrays - Production Batch
```csharp
[StructLayout(LayoutKind.Sequential)]
public class ProductionBatchUdt
{
    public LogixString batchId = new();
    public Int32 targetQuantity;
    public Int32 completedQuantity;
    public Single[] hourlyProduction = new Single[24]; // REAL[24]
    public Boolean isComplete;
}

[EdgePlcDriverSubscribe("plc1", "Current_Batch", pollingIntervalMs: 2000, OnChangeOnly = true)]
public class ProductionBatchHandler : IMessageSubscriptionHandler<TagValue<ProductionBatchUdt>>
{
    public Task HandleAsync(TagValue<ProductionBatchUdt> message, IMessageContext context, CancellationToken ct)
    {
        var batch = message.Value;
        var progress = batch.targetQuantity > 0 ? (batch.completedQuantity * 100.0 / batch.targetQuantity) : 0;
        Console.WriteLine($"📦 Batch {batch.batchId.Value}: {progress:F1}% complete");
        return Task.CompletedTask;
    }
}
```

### Arrays

#### float[]/REAL[] - Zone Temperatures
```csharp
[EdgePlcDriverSubscribe("plc1", "Zone_Temperatures", pollingIntervalMs: 500, OnChangeOnly = true)]
public class ZoneTemperaturesHandler : IMessageSubscriptionHandler<TagValue<float[]>>
{
    public Task HandleAsync(TagValue<float[]> message, IMessageContext context, CancellationToken ct)
    {
        var temps = message.Value;
        Console.WriteLine($"🌡️ Zones: [{string.Join(", ", temps.Select(t => $"{t:F1}°C"))}]");
        Console.WriteLine($"📊 Avg: {temps.Average():F1}°C | Min: {temps.Min():F1}°C | Max: {temps.Max():F1}°C");
        return Task.CompletedTask;
    }
}
```

### Strings

#### LogixString - Status Messages
```csharp
[EdgePlcDriverSubscribe("plc1", "Status_Message", pollingIntervalMs: 1000, OnChangeOnly = true)]
public class StatusMessageHandler : IMessageSubscriptionHandler<TagValue<LogixString>>
{
    public Task HandleAsync(TagValue<LogixString> message, IMessageContext context, CancellationToken ct)
    {
        var text = message.Value.Value; // LogixString.Value returns string
        Console.WriteLine($"💬 PLC Message: \"{text}\" (Length: {message.Value.stringLength})");
        return Task.CompletedTask;
    }
}
```

### Writing UDTs Back to PLC

#### Read, Modify, and Write UDT
```csharp
[StructLayout(LayoutKind.Sequential)]
public class RecipeDataUdt
{
    public Int16 recipeId;
    public LogixString recipeName = new();
    public Single temperature;
    public Boolean isActive;
}

[EdgePlcDriverSubscribe("plc1", "Active_Recipe", pollingIntervalMs: 2000)]
public class RecipeHandler : IMessageSubscriptionHandler<TagValue<RecipeDataUdt>>
{
    private readonly IConduit _conduit;

    public RecipeHandler(IConduit conduit)
    {
        _conduit = conduit;
    }

    public async Task HandleAsync(TagValue<RecipeDataUdt> message, IMessageContext context, CancellationToken ct)
    {
        var recipe = message.Value;
        var plc = _conduit.GetConnection<IEdgePlcDriverConnection>();

        // Modify and write back to same tag
        recipe.temperature = 85.0f;
        recipe.isActive = true;
        await plc.WriteTagAsync("Active_Recipe", recipe, ct);

        // Or create new UDT and write to different tag
        var newRecipe = new RecipeDataUdt
        {
            recipeId = (short)(recipe.recipeId + 1),
            temperature = recipe.temperature * 1.05f,
            isActive = true
        };
        newRecipe.recipeName.SetString("NewRecipe");  // Use SetString for LogixString
        
        await plc.WriteTagAsync("New_Recipe", newRecipe, ct);
    }
}
```

## Route Path Format

```
IP,Backplane,Slot

Examples:
- 192.168.1.10,1,0  → CPU in slot 0
- 192.168.1.10,1,3  → CPU in slot 3
```

## Data Types

### Primitive Types

| PLC | C# | Size |
|-----|-------|------|
| BOOL | Boolean | 4 bytes (in UDT) |
| SINT | SByte | 1 byte |
| INT | Int16 | 2 bytes |
| DINT | Int32 | 4 bytes |
| LINT | Int64 | 8 bytes |
| REAL | Single | 4 bytes |
| LREAL | Double | 8 bytes |
| STRING | LogixString | 88 bytes |

### User-Defined Types (UDTs)

```csharp
using System.Runtime.InteropServices;
using Conduit.EdgePlcDriver.DataTypes;

[StructLayout(LayoutKind.Sequential)]
public class MyUDT
{
    // Use PUBLIC FIELDS in the EXACT ORDER as the PLC UDT
    public Boolean enabled;
    public Int32 count;
    public Single rate;
    public LogixString name = new();
}
```

## API Reference

### IEdgePlcDriverConnection

| Method | Description |
|--------|-------------|
| `ConnectAsync()` | Connect to the PLC |
| `DisconnectAsync()` | Disconnect from the PLC |
| `ReadTagAsync<T>(tagName)` | Read a tag value |
| `WriteTagAsync<T>(tagName, value)` | Write a tag value |
| `SubscribeAsync<T>(tagName, handler)` | Dynamic subscription |

### EdgePlcDriverConnectionOptions

| Property | Default | Description |
|----------|---------|-------------|
| `ConnectionName` | "default" | Logical name for handler matching |
| `IpAddress` | "" | PLC IP address |
| `CpuSlot` | 0 | CPU slot number |
| `Backplane` | 1 | Backplane number |
| `DefaultPollingIntervalMs` | 100 | Default polling rate |
| `ConnectionTimeoutSeconds` | 10 | Connection timeout |
| `AutoReconnect` | true | Enable auto-reconnection |

## License

Requires valid ASComm IoT license from Automated Solutions.

## Resources

- [ASComm IoT Documentation](https://automatedsolutions.com/products/iot/ascommiot/)
- [Conduit Documentation](../README.md)
