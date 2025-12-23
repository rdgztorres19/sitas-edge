# Conduit.EdgePlcDriver - Internal Code Architecture

Documentation of **how the code is implemented internally**: handler discovery, resolution, data flow, internal structures.

## Table of Contents

1. [Handler Discovery](#handler-discovery)
2. [Handler Resolution and Activation](#handler-resolution-and-activation)
3. [Internal Data Structures](#internal-data-structures)
4. [Subscription Flow](#subscription-flow)
5. [Message Dispatching](#message-dispatching)
6. [Read and Write Operations](#read-and-write-operations)
7. [ASComm IoT Object Management](#ascomm-iot-object-management)
8. [Type Conversion and Marshalling](#type-conversion-and-marshalling)
9. [Event System Integration](#event-system-integration)

---

## Handler Discovery

### How Handlers Are Found

**Code:** `HandlerDiscoveryService.DiscoverInAssembly()` (Conduit.Core)

```csharp
private static IEnumerable<HandlerRegistration> DiscoverInAssembly(Assembly assembly)
{
    // 1. Get all types from the assembly
    var handlerTypes = assembly.GetTypes()
        .Where(IsValidHandlerType);

    foreach (var handlerType in handlerTypes)
    {
        // 2. Look for [EdgePlcDriverSubscribe] attributes (inherits from [Subscribe])
        var subscribeAttributes = handlerType.GetCustomAttributes<SubscribeAttribute>();
        
        // 3. Extract message type from IMessageSubscriptionHandler<TMessage>
        var messageType = GetMessageType(handlerType);

        // 4. Create HandlerRegistration for each attribute
        foreach (var attribute in subscribeAttributes)
        {
            yield return new HandlerRegistration(
                handlerType,                     // typeof(TemperatureHandler)
                messageType,                     // typeof(TagValue<float>)
                attribute.ConnectionName,         // "plc1"
                attribute.Topic,                  // "Sensor_Temperature"
                attribute.QualityOfService,
                attribute);
        }
    }
}
```

### Handler Validation

```csharp
private static bool IsValidHandlerType(Type type)
{
    return type.IsClass                                              // Is a class
        && !type.IsAbstract                                          // Not abstract
        && !type.IsGenericTypeDefinition                             // Not an open generic
        && ImplementsMessageHandler(type)                            // Has IMessageSubscriptionHandler<T>
        && type.GetCustomAttributes<SubscribeAttribute>().Any()      // Has [EdgePlcDriverSubscribe]
        && !type.IsDefined(typeof(DisableHandlerAttribute));         // Doesn't have [DisableHandler]
}

private static bool ImplementsMessageHandler(Type type)
{
    return type.GetInterfaces().Any(i => 
        i.IsGenericType && 
        i.GetGenericTypeDefinition() == typeof(IMessageSubscriptionHandler<>));
}
```

### Message Type Extraction

```csharp
private static Type? GetMessageType(Type handlerType)
{
    // Find IMessageSubscriptionHandler<TMessage> in interfaces
    var messageHandlerInterface = handlerType.GetInterfaces()
        .FirstOrDefault(i => 
            i.IsGenericType && 
            i.GetGenericTypeDefinition() == typeof(IMessageSubscriptionHandler<>));

    // Return TMessage
    return messageHandlerInterface?.GetGenericArguments()[0];
}

// Example:
// class TemperatureHandler : IMessageSubscriptionHandler<TagValue<float>>
// messageHandlerInterface = IMessageSubscriptionHandler<TagValue<float>>
// GetGenericArguments()[0] = TagValue<float>
```

### When Discovery is Executed

```
EdgePlcDriverBuilder.WithHandlersFromEntryAssembly()
  └─> HandlerDiscoveryService.DiscoverHandlersFromEntryAssembly()
      └─> DiscoverInAssembly(Assembly.GetEntryAssembly())
          └─> [List<HandlerRegistration>]
              └─> Passed to EdgePlcDriver constructor
```

---

## Handler Resolution and Activation

### FuncActivator - How Instances Are Created

**Code:** `FuncActivator.CreateInstance()` (Conduit.Core)

```csharp
public object CreateInstance(Type handlerType)
{
    // STEP 1: Auto-inject IConduit (without DI registration)
    if (handlerType == typeof(IConduit))
        return _conduitInstance;

    // STEP 2: Try to resolve from DI container
    try
    {
        var handler = _factory(handlerType);  // ActivatorUtilities.GetServiceOrCreateInstance()
        if (handler != null)
            return handler;
    }
    catch { }

    // STEP 3: Manual creation with dependency resolution
    return CreateInstanceWithDependencies(handlerType);
}

private object CreateInstanceWithDependencies(Type handlerType)
{
    // Greedy constructor: the one with MOST parameters
    var constructor = handlerType.GetConstructors()
        .OrderByDescending(c => c.GetParameters().Length)
        .First();

    var parameters = constructor.GetParameters();
    var args = new object[parameters.Length];

    // Resolve each parameter
    for (int i = 0; i < parameters.Length; i++)
    {
        args[i] = ResolveParameter(parameters[i].ParameterType);
    }

    return constructor.Invoke(args);
}

private object ResolveParameter(Type paramType)
{
    // Auto-injection of Conduit types
    if (paramType == typeof(IConduit))
        return _conduitInstance;
    
    if (paramType == typeof(IEdgePlcDriver))
        return GetConnectionFromConduit<IEdgePlcDriver>();
    
    if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(ILogger<>))
        return CreateNullLogger(paramType);
    
    // Resolve from DI container
    try
    {
        return _factory(paramType);
    }
    catch
    {
        // Fallback: create with new() if it has parameterless constructor
        return Activator.CreateInstance(paramType);
    }
}
```

### HandlerResolver Wrapper

```csharp
// Internal/ActivatorHandlerResolver.cs (wraps IHandlerActivator)
public class ActivatorHandlerResolver : IHandlerResolver
{
    private readonly IHandlerActivator _activator;

    public IScopedHandler ResolveScoped(Type handlerType)
    {
        var handler = _activator.CreateInstance(handlerType);
        return new ScopedHandlerWrapper(handler);
    }
}
```

### When Handlers Are Resolved

```
Item.DataChanged event fires (on ASComm's internal thread)
  └─> EdgePlcDriver.Item_DataChanged(sender, e)
      └─> Lookup tagName in _dynamicHandlers dictionary
          └─> if found: handler.Handler is Func<ABLogix.Item, CancellationToken, Task>
              └─> Task.Run(async () => await handler.Handler(theItem, ct))
                  └─> For attribute-based handlers:
                      ├─> CreateAttributeHandlerDelegate() was called during StartRegisteredHandlersAsync()
                      ├─> The delegate calls: _handlerResolver.ResolveScoped(handlerType)
                      │   └─> FuncActivator.CreateInstance(handlerType)
                      │       └─> Greedy constructor resolution
                      └─> Invoke HandleAsync() via reflection
```

---

## Internal Data Structures

### Main Fields of EdgePlcDriver

```csharp
// EdgePlcDriver.cs (lines 31-47)
internal sealed class EdgePlcDriver : IEdgePlcDriver
{
    // Configuration
    private readonly EdgePlcDriverOptions _options;
    private readonly IReadOnlyList<TagHandlerRegistration> _handlerRegistrations;  // Discovered
    
    // Dependencies
    private readonly IMessageSerializer _serializer;
    private readonly IHandlerResolver _handlerResolver;
    private readonly ILogger<EdgePlcDriver> _logger;
    private readonly EdgePlcDriverPublisher _publisher;
    
    // Thread safety
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    
    // Runtime subscriptions
    private readonly ConcurrentDictionary<string, TagHandler> _dynamicHandlers = new();
    
    // Value cache for OnChangeOnly
    private readonly ConcurrentDictionary<string, object?> _lastTagValues = new();
    
    // ASComm Items cache
    private readonly ConcurrentDictionary<string, ABLogix.Item> _tagItems = new();
    
    // Timers for dynamic subscription polling
    private readonly ConcurrentDictionary<string, Timer> _pollingTimers = new();
    
    // ASComm IoT hierarchy
    private ABLogix.Net.Channel? _channel;
    private ABLogix.Device? _device;
    private ABLogix.Group? _pollingGroup;      // Normal polling (100ms+)
    private ABLogix.Group? _unsolicitedGroup;  // Fast polling (10ms)
    
    // State
    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _disposed;
}
```

### TagHandlerRegistration (Internal)

```csharp
// Internal/TagHandlerRegistration.cs
internal sealed class TagHandlerRegistration
{
    public required string TagName { get; init; }              // "Sensor_Temperature"
    public required Type HandlerType { get; init; }           // typeof(TemperatureHandler)
    public required Type MessageType { get; init; }           // typeof(TagValue<float>)
    public required int PollingIntervalMs { get; init; }      // 100
    public bool OnChangeOnly { get; init; } = true;           // Only fire on value change
    public double Deadband { get; init; } = 0.0;              // Deadband for numeric types
    public Attributes.TagSubscriptionMode Mode { get; init; } // Polling or Unsolicited
}
```

### TagHandler (Runtime Subscription Storage)

```csharp
// Internal/TagSubscription.cs
internal sealed class TagHandler
{
    public required string TagName { get; init; }              // "Sensor_Temperature"
    public required Type MessageType { get; init; }             // typeof(float) or typeof(TagValue<float>)
    public required Delegate Handler { get; init; }           // Func<ABLogix.Item, CancellationToken, Task>
    public required int PollingIntervalMs { get; init; }       // Polling interval in ms
    public bool OnChangeOnly { get; init; } = true;            // Only fire on value change
    public double Deadband { get; init; } = 0.0;               // Deadband threshold
    public object? LastValue { get; set; }                     // Last value for OnChangeOnly comparison
    public Attributes.TagSubscriptionMode Mode { get; init; }  // Polling or Unsolicited
}
```

**Key Point:** `TagHandler` stores a **single Delegate** (not a list). This delegate is created by `CreateAttributeHandlerDelegate()` for attribute-based handlers, or wrapped from the user's lambda in `SubscribeAsync<T>()`.

### TagSubscription (Disposable Wrapper)

```csharp
// Internal/TagSubscription.cs
internal sealed class TagSubscription : IAsyncDisposable
{
    private readonly string _tagName;
    private readonly Func<string, Task> _unsubscribeAction;
    
    // When disposed, calls UnsubscribeAsync(tagName) to remove from _dynamicHandlers
}
```

---

## Subscription Flow

### Attribute-Based Subscriptions

```
1. ConnectAsync()
   │
   ├─> CreateAsCommObjects()
   │   ├─> _channel = new ABLogix.Net.Channel()
   │   ├─> _device = new ABLogix.Device()
   │   ├─> _pollingGroup = new ABLogix.Group { PollingRate = 100 }
   │   └─> _unsolicitedGroup = new ABLogix.Group { PollingRate = 10 }
   │
   ├─> StartRegisteredHandlersAsync()
       │
       └─> foreach (var registration in _handlerRegistrations)
           │
           ├─> GetOrCreateItem(registration.TagName, registration.Mode)
           │   ├─> Creates ABLogix.Item if not in _tagItems cache
           │   ├─> Adds Item to appropriate Group (polling or unsolicited)
           │   └─> Attaches event handlers:
           │       item.DataChanged += Item_DataChanged;
           │       item.Error += Item_Error;
           │
           ├─> CreateAttributeHandlerDelegate(registration)
           │   └─> Returns Func<ABLogix.Item, CancellationToken, Task> delegate
           │       └─> This delegate handles:
           │           ├─> OnChangeOnly filtering
           │           ├─> Deadband checking
           │           ├─> Type conversion (UDTs, arrays, primitives)
           │           ├─> Handler resolution via _handlerResolver
           │           ├─> TagValue<T> creation via reflection
           │           └─> HandleAsync() invocation
           │
           ├─> Create TagHandler instance
           │   var tagHandler = new TagHandler
           │   {
           │       TagName = registration.TagName,
           │       MessageType = registration.MessageType,
           │       Handler = CreateAttributeHandlerDelegate(registration),
           │       PollingIntervalMs = registration.PollingIntervalMs,
           │       OnChangeOnly = registration.OnChangeOnly,
           │       Deadband = registration.Deadband,
           │       Mode = registration.Mode
           │   };
           │
           └─> Store in _dynamicHandlers dictionary
               _dynamicHandlers[registration.TagName] = tagHandler;
       
       └─> Activate Groups
           if (hasPollingSubscriptions) _pollingGroup.Active = true;
           if (hasUnsolicitedSubscriptions) _unsolicitedGroup.Active = true;
```

### Programmatic Subscriptions (Runtime)

**Code:** `EdgePlcDriver.SubscribeAsync<T>()`

```csharp
public Task<IAsyncDisposable> SubscribeAsync<T>(
    string tagName,
    Func<TagValue<T>, IEdgePlcDriverMessageContext, CancellationToken, Task> handler,
    int pollingIntervalMs = 100,
    CancellationToken cancellationToken = default)
{
    // STEP 1: Create wrapped handler delegate
    async Task WrappedHandler(ABLogix.Item theItem, CancellationToken ct)
    {
        var quality = MapQuality(theItem.Quality);
        T value = default!;
        
        // Convert ASComm values to T (handles UDTs, arrays, primitives)
        if (quality == TagQuality.Good && theItem.Values?.Length > 0)
        {
            if (IsStructuredType<T>())
            {
                value = Activator.CreateInstance<T>();
                theItem.GetStructuredValues(value);
            }
            else if (typeof(T).IsArray)
            {
                value = ConvertToArray<T>(theItem.Values);
            }
            else
            {
                var rawValue = theItem.Values.Length == 1 ? theItem.Values[0] : theItem.Values;
                value = ConvertValue<T>(rawValue);
            }
        }
        
        // Create TagValue<T> with previous value tracking
        var tagValue = new TagValue<T>
        {
            TagName = tagName,
            Value = value,
            Timestamp = DateTimeOffset.UtcNow,
            Quality = quality
        };
        
        if (_lastTagValues.TryGetValue(tagName, out var lastValue) && lastValue is T typedLast)
        {
            tagValue.PreviousValue = typedLast;
        }
        _lastTagValues[tagName] = value;
        
        // Create context with publisher
        var rawPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        var context = new EdgePlcDriverMessageContext(tagName, rawPayload, _publisher, this);
        
        // Invoke user's handler
        await handler(tagValue, context, ct);
    }
    
    // STEP 2: Store handler in _dynamicHandlers dictionary
    var tagHandler = new TagHandler
    {
        TagName = tagName,
        MessageType = typeof(T),
        Handler = (Func<ABLogix.Item, CancellationToken, Task>)WrappedHandler,
        PollingIntervalMs = effectiveInterval
    };
    
    _dynamicHandlers[tagName] = tagHandler;  // Key: tagName, Value: TagHandler
    
    // STEP 3: Create ASComm Item if it doesn't exist
    var item = GetOrCreateItem(tagName);  // Adds to _pollingGroup, attaches Item_DataChanged
    
    // STEP 4: Activate polling group if needed
    if (_pollingGroup is not null && !_pollingGroup.Active)
    {
        _pollingGroup.Active = true;
    }
    
    // STEP 5: Return disposable for cleanup
    return Task.FromResult<IAsyncDisposable>(
        new TagSubscription(tagName, UnsubscribeAsync));
}
```

**Key Points:**
- The user's `Func<TagValue<T>, ...>` handler is **wrapped** into a `Func<ABLogix.Item, CancellationToken, Task>` delegate
- This wrapped delegate is stored in `_dynamicHandlers[tagName]` as a `TagHandler` instance
- When `Item_DataChanged` fires, it looks up the tagName in `_dynamicHandlers` and invokes the stored delegate
- The delegate handles type conversion, TagValue creation, and context setup before calling the user's handler

---

## Message Dispatching

### ASComm Event → Handlers

**Code:** `EdgePlcDriver.Item_DataChanged()`

```
ASComm IoT Library (internal polling thread)
  └─> Item.DataChanged event fires
      └─> EdgePlcDriver.Item_DataChanged(object? sender, ItemDataChangedEventArgs e)
          │
          ├─> var theItem = (ABLogix.Item)sender
          ├─> var tagName = theItem.HWTagName  // Actual PLC tag name
          │
          └─> Lookup handler in _dynamicHandlers dictionary
              if (_dynamicHandlers.TryGetValue(tagName, out var handler))
              {
                  // handler.Handler is Func<ABLogix.Item, CancellationToken, Task>
                  if (handler.Handler is Func<ABLogix.Item, CancellationToken, Task> asyncHandler)
                  {
                      // Fire and forget on background thread
                      Task.Run(async () =>
                      {
                          await asyncHandler(theItem, _disposeCts.Token);
                      });
                  }
              }
```

**Important:** The actual implementation does **NOT** use separate `DispatchToRegisteredHandlersAsync` and `DispatchToDynamicHandlersAsync` methods. Instead:

1. **Attribute-based handlers** are converted to `TagHandler` instances during `StartRegisteredHandlersAsync()` and stored in `_dynamicHandlers`
2. **Programmatic subscriptions** are also stored in `_dynamicHandlers`
3. **Both types** are handled by the same `Item_DataChanged` event handler

### Handler Delegate Creation for Attribute-Based Handlers

**Code:** `EdgePlcDriver.CreateAttributeHandlerDelegate()`

```csharp
private Delegate CreateAttributeHandlerDelegate(TagHandlerRegistration registration)
{
    return async (ABLogix.Item theItem, CancellationToken ct) =>
    {
        var quality = MapQuality(theItem.Quality);
        
        // Extract inner type from TagValue<T> (e.g., TagValue<float> → float)
        var innerType = registration.MessageType.GetGenericArguments().FirstOrDefault() ?? typeof(object);
        
        // OnChangeOnly filtering
        if (registration.OnChangeOnly)
        {
            var currentValue = quality == TagQuality.Good && theItem.Values?.Length > 0
                ? theItem.Values[0]
                : null;
            
            if (_dynamicHandlers.TryGetValue(registration.TagName, out var handler))
            {
                var lastValue = handler.LastValue;
                if (Equals(currentValue, lastValue))
                    return; // Skip if unchanged
                
                // Deadband check for numeric types
                if (registration.Deadband > 0 && IsNumeric(currentValue) && IsNumeric(lastValue))
                {
                    var diff = Math.Abs(Convert.ToDouble(currentValue) - Convert.ToDouble(lastValue));
                    if (diff <= registration.Deadband)
                        return; // Within deadband, skip
                }
                
                handler.LastValue = currentValue;
            }
        }
        
        // Resolve handler instance with dependency injection
        using var scopedHandler = _handlerResolver.ResolveScoped(registration.HandlerType);
        var handlerInstance = scopedHandler.Handler;
        
        // Convert ASComm values to inner type (handles UDTs, arrays, primitives)
        object? deserializedValue = null;
        if (quality == TagQuality.Good && theItem.Values?.Length > 0)
        {
            if (IsStructuredTypeRuntime(innerType))
            {
                deserializedValue = Activator.CreateInstance(innerType);
                theItem.GetStructuredValues(deserializedValue);
            }
            else if (innerType.IsArray)
            {
                var elementType = innerType.GetElementType()!;
                var array = Array.CreateInstance(elementType, theItem.Values.Length);
                for (int i = 0; i < theItem.Values.Length; i++)
                {
                    array.SetValue(Convert.ChangeType(theItem.Values[i], elementType), i);
                }
                deserializedValue = array;
            }
            else
            {
                var rawValue = theItem.Values.Length == 1 ? theItem.Values[0] : theItem.Values;
                deserializedValue = Convert.ChangeType(rawValue, innerType);
            }
        }
        
        // Create TagValue<T> via reflection (because innerType is runtime-known)
        var tagValueType = typeof(TagValue<>).MakeGenericType(innerType);
        var tagValue = Activator.CreateInstance(tagValueType);
        
        tagValueType.GetProperty("TagName")?.SetValue(tagValue, registration.TagName);
        tagValueType.GetProperty("Value")?.SetValue(tagValue, deserializedValue);
        tagValueType.GetProperty("Timestamp")?.SetValue(tagValue, DateTimeOffset.UtcNow);
        tagValueType.GetProperty("Quality")?.SetValue(tagValue, quality);
        
        // Create context
        var rawPayload = deserializedValue is not null
            ? Encoding.UTF8.GetBytes(JsonSerializer.Serialize(deserializedValue))
            : ReadOnlyMemory<byte>.Empty;
        
        var context = new EdgePlcDriverMessageContext(registration.TagName, rawPayload, _publisher, this);
        
        // Invoke HandleAsync via reflection
        var method = registration.HandlerType.GetMethod("HandleAsync");
        var task = (Task?)method.Invoke(handlerInstance, [tagValue, context, ct]);
        if (task is not null)
        {
            await task.ConfigureAwait(false);
        }
    };
}
```

**Key Points:**
- The delegate handles **generic type conversion** at runtime using reflection
- `TagValue<T>` is created dynamically using `MakeGenericType()` because `T` is only known at runtime
- OnChangeOnly and Deadband filtering happens **before** handler resolution (saves resources)
- Handler instance is resolved **per event** (supports scoped DI services)

---

## ASComm IoT Object Management

### Object Hierarchy

```
Channel (Network Protocol - Ethernet/IP)
  └─> Device (PLC)
      ├─> Group (Polling - 100ms+)
      │   └─> Item (Tag: "Sensor_Temperature")
      │   └─> Item (Tag: "Motor_Speed")
      │
      └─> Group (Unsolicited - 10ms)
          └─> Item (Tag: "Emergency_Stop")
```

### Object Creation

```csharp
// EdgePlcDriver.CreateAsCommObjects()
private void CreateAsCommObjects()
{
    // 1. Channel: Defines the Ethernet/IP connection
    _channel = new ABLogix.Net.Channel();
    _channel.RoutePath = _options.RoutePath;  // "192.168.1.10,1,0"
    _channel.Error += Channel_Error;

    // 2. Device: Represents the PLC
    _device = new ABLogix.Device
    {
        Name = _options.ConnectionName,
        Channel = _channel
    };
    _device.Error += Device_Error;

    // 3. Normal Polling Group
    _pollingGroup = new ABLogix.Group
    {
        Name = "PollingGroup",
        Device = _device,
        PollingRate = _options.DefaultPollingIntervalMs,
        Active = false  // Activated after adding Items
    };

    // 4. Unsolicited Group (fast polling)
    _unsolicitedGroup = new ABLogix.Group
    {
        Name = "UnsolicitedGroup",
        Device = _device,
        PollingRate = 10,  // Fixed 10ms
        Active = false
    };
}
```

### GetOrCreateItem Pattern

```csharp
private ABLogix.Item GetOrCreateItem(string tagName, ABLogix.Group group)
{
    return _tagItems.GetOrAdd(tagName, _ =>
    {
        var item = new ABLogix.Item
        {
            Name = tagName,
            HWTagName = tagName,  // PLC tag name
            DataType = DetermineDataType<T>(),
            Elements = DetermineElements<T>()
        };
        
        // Add to group
        group.Items.Add(item);
        
        // Attach event handlers
        item.DataChanged += OnItemDataChanged;
        item.Error += OnItemError;
        
        return item;
    });
}
```

### Group Activation

```csharp
// EdgePlcDriver.ConnectAsync()
private async Task ConnectAsync()
{
    CreateAsCommObjects();
    
    // Open channel and device
    await Task.Run(() =>
    {
        _channel.Open();
        _device.Open();
    });
    
    // Subscribe to registered handlers (creates Items)
    await SubscribeToRegisteredHandlersAsync();
    
    // Activate groups AFTER adding all Items
    _pollingGroup.Active = true;
    _unsolicitedGroup.Active = true;
    
    SetState(ConnectionState.Connected);
}
```

---

## Type Conversion and Marshalling

### PLC Type Mapping → C#

```csharp
// EdgePlcDriver.MapDataType<T>()
private ABLogix.DataType MapDataType<T>()
{
    var type = typeof(T);
    
    if (type == typeof(bool))
        return ABLogix.DataType.BOOL;
    if (type == typeof(short))
        return ABLogix.DataType.INT;
    if (type == typeof(int))
        return ABLogix.DataType.DINT;
    if (type == typeof(float))
        return ABLogix.DataType.REAL;
    if (type == typeof(double))
        return ABLogix.DataType.LREAL;
    if (type == typeof(LogixString))
        return ABLogix.DataType.STRING;
    if (type.IsArray)
        return MapDataType(type.GetElementType());
    
    // UDT (User-Defined Type)
    return ABLogix.DataType.UserDefined;
}
```

### Value Conversion

```csharp
// EdgePlcDriver.ConvertItemValue<T>()
private T ConvertItemValue<T>(ABLogix.Item item)
{
    if (item.Values == null || item.Values.Length == 0)
        return default(T);
    
    var type = typeof(T);
    
    // Primitive types
    if (type.IsPrimitive || type == typeof(LogixString))
    {
        return (T)item.Values[0];
    }
    
    // Arrays
    if (type.IsArray)
    {
        var elementType = type.GetElementType();
        var array = Array.CreateInstance(elementType, item.Values.Length);
        Array.Copy(item.Values, array, item.Values.Length);
        return (T)(object)array;
    }
    
    // UDTs
    if (IsStructuredType<T>())
    {
        return ConvertBytesToUdt<T>((byte[])item.Values[0]);
    }
    
    throw new NotSupportedException($"Type {type.Name} not supported");
}
```

### UDT Marshalling

```csharp
private T ConvertBytesToUdt<T>(byte[] bytes)
{
    var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
    try
    {
        var ptr = handle.AddrOfPinnedObject();
        return Marshal.PtrToStructure<T>(ptr);
    }
    finally
    {
        handle.Free();
    }
}

private byte[] ConvertUdtToBytes<T>(T udt)
{
    var size = Marshal.SizeOf<T>();
    var bytes = new byte[size];
    var ptr = Marshal.AllocHGlobal(size);
    
    try
    {
        Marshal.StructureToPtr(udt, ptr, false);
        Marshal.Copy(ptr, bytes, 0, size);
        return bytes;
    }
    finally
    {
        Marshal.FreeHGlobal(ptr);
    }
}
```

### Quality Mapping

```csharp
private TagQuality MapQuality(Quality asCommQuality)
{
    return asCommQuality switch
    {
        Quality.Good => TagQuality.Good,
        Quality.Bad => TagQuality.Bad,
        Quality.Uncertain => TagQuality.Uncertain,
        Quality.ConfigError => TagQuality.ConfigError,
        Quality.NotConnected => TagQuality.NotConnected,
        _ => TagQuality.Unknown
    };
}
```

---

## Internal Flows Summary

### Complete Subscription Flow (Attribute-Based)

```
1. Startup
   └─> HandlerDiscoveryService.DiscoverHandlers()
       └─> Finds [EdgePlcDriverSubscribe] in handlers
           └─> Creates HandlerRegistration list

2. Build
   └─> EdgePlcDriver constructor receives HandlerRegistration list

3. ConnectAsync()
   ├─> Creates Channel, Device, Groups
   ├─> For each HandlerRegistration:
   │   ├─> Creates ABLogix.Item
   │   ├─> Adds Item to Group (polling or unsolicited)
   │   └─> Attach Item.DataChanged event
   ├─> Activates Groups (starts polling)

4. Tag Changes in PLC
   └─> ASComm library polling detects change (Group.Active = true)
       └─> Fires Item.DataChanged event (on ASComm's internal thread)
           └─> Item_DataChanged(sender, e)
               ├─> Extract tagName from theItem.HWTagName
               ├─> Lookup _dynamicHandlers[tagName]
               └─> if found:
                   └─> Task.Run(async () => await handler.Handler(theItem, ct))
                       └─> The stored delegate (from CreateAttributeHandlerDelegate or SubscribeAsync):
                           ├─> Converts ASComm values to typed value
                           ├─> Creates TagValue<T> (via reflection for attribute handlers)
                           ├─> For attribute handlers: Resolves handler instance via _handlerResolver
                           ├─> For attribute handlers: Invokes HandleAsync() via reflection
                           └─> For programmatic: Invokes user's Func<TagValue<T>, ...> directly
```

## Read and Write Operations

### ReadTagAsync<T> - Single Tag Read

**Code:** `EdgePlcDriver.ReadTagAsync<T>()`

```csharp
public async Task<TagValue<T>> ReadTagAsync<T>(string tagName, CancellationToken cancellationToken = default)
{
    // STEP 1: Get or create ASComm Item (cached in _tagItems)
    var item = GetOrCreateItem(tagName);
    
    // STEP 2: Perform async read from PLC
    await item.ReadAsync().ConfigureAwait(false);
    
    // STEP 3: Map ASComm quality to TagQuality
    var quality = MapQuality(item.Quality);
    
    // STEP 4: Convert ASComm values to T
    T value = default!;
    if (quality == TagQuality.Good && item.Values is not null && item.Values.Length > 0)
    {
        if (IsStructuredType<T>())
        {
            // UDT: Use GetStructuredValues to populate object
            value = Activator.CreateInstance<T>();
            item.GetStructuredValues(value);
        }
        else if (typeof(T).IsArray)
        {
            // Array: Convert object[] to T[]
            value = ConvertToArray<T>(item.Values);
        }
        else
        {
            // Primitive: Extract single value or array
            var rawValue = item.Values.Length == 1 ? item.Values[0] : item.Values;
            value = ConvertValue<T>(rawValue);
        }
    }
    
    // STEP 5: Create TagValue<T> with previous value tracking
    var tagValue = new TagValue<T>
    {
        TagName = tagName,
        Value = value,
        Timestamp = DateTimeOffset.UtcNow,
        Quality = quality
    };
    
    if (_lastTagValues.TryGetValue(tagName, out var lastValue) && lastValue is T typedLastValue)
    {
        tagValue.PreviousValue = typedLastValue;
    }
    _lastTagValues[tagName] = value;
    
    return tagValue;
}
```

**Flow:**
```
ReadTagAsync<T>("Sensor_Temperature")
  └─> GetOrCreateItem("Sensor_Temperature")
      ├─> Check _tagItems cache
      ├─> If not found: Create ABLogix.Item, add to _pollingGroup, cache in _tagItems
      └─> Return cached or new Item
  └─> item.ReadAsync()  // ASComm async read
  └─> Convert item.Values to T (handles UDTs, arrays, primitives)
  └─> Create TagValue<T> with quality, timestamp, previous value
  └─> Return TagValue<T>
```

### ReadTagsAsync - Batch Read (Untyped)

**Code:** `EdgePlcDriver.ReadTagsAsync()`

```csharp
public async Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(
    IEnumerable<string> tagNames,
    CancellationToken cancellationToken = default)
{
    // STEP 1: Create temporary Group for batch read
    var tempGroup = new ABLogix.Group(true, 100);  // Active = true
    _device?.Groups.Add(tempGroup);
    
    var tempItems = new Dictionary<string, ABLogix.Item>();
    
    try
    {
        // STEP 2: Create Items for all tags in the temporary group
        foreach (var tagName in tagNamesList)
        {
            var item = new ABLogix.Item($"temp_{tagName}", tagName);
            tempGroup.Items.Add(item);
            tempItems[tagName] = item;
        }
        
        // STEP 3: Read all items asynchronously (ASComm batches these efficiently)
        var readTasks = tempItems.Values.Select(item => item.ReadAsync()).ToList();
        await Task.WhenAll(readTasks).ConfigureAwait(false);
        
        // STEP 4: Collect values (as object? - no type conversion)
        var results = new Dictionary<string, object?>();
        foreach (var tagName in tagNamesList)
        {
            if (tempItems.TryGetValue(tagName, out var item))
            {
                var quality = MapQuality(item.Quality);
                object? value = null;
                
                if (quality == TagQuality.Good && item.Values is not null && item.Values.Length > 0)
                {
                    value = item.Values.Length == 1 ? item.Values[0] : item.Values;
                }
                
                results[tagName] = value;
            }
        }
        
        return results;
    }
    finally
    {
        // STEP 5: Cleanup temporary group
        tempGroup.Active = false;
        _device?.Groups.Remove(tempGroup);
    }
}
```

**Flow:**
```
ReadTagsAsync(["Tag1", "Tag2", "Tag3"])
  └─> Create temporary ABLogix.Group
  └─> Create ABLogix.Item for each tag in temp group
  └─> Task.WhenAll(item.ReadAsync() for all items)  // Parallel batch read
  └─> Extract values from item.Values (no type conversion)
  └─> Return Dictionary<string, object?>
  └─> Remove temporary group
```

### ReadTagsAsync<T> - Batch Read (Typed)

**Code:** `EdgePlcDriver.ReadTagsAsync<T>()`

Similar to `ReadTagsAsync()` but:
- Performs type conversion to `T` for each tag
- Returns `IReadOnlyDictionary<string, T>`
- Handles UDTs, arrays, and primitives using the same conversion logic as `ReadTagAsync<T>`

### WriteTagAsync<T> - Single Tag Write

**Code:** `EdgePlcDriver.WriteTagInternalAsync()`

```csharp
private async Task WriteTagInternalAsync(string tagName, object value, CancellationToken cancellationToken)
{
    // STEP 1: Get or create ASComm Item
    var item = GetOrCreateItem(tagName);
    
    // STEP 2: Determine write format based on value type
    if (value is not null && IsStructuredTypeRuntime(value.GetType()))
    {
        // UDT/Struct: Write directly (NOT wrapped in array)
        await item.WriteAsync(value).ConfigureAwait(false);
    }
    else if (value is Array array)
    {
        // Array: Convert to object[]
        var valuesToWrite = new object[array.Length];
        array.CopyTo(valuesToWrite, 0);
        await item.WriteAsync(valuesToWrite).ConfigureAwait(false);
    }
    else
    {
        // Primitive: Wrap in object[] array
        await item.WriteAsync(new object[] { value }).ConfigureAwait(false);
    }
}
```

**Flow:**
```
WriteTagAsync<T>("Sensor_Temperature", value)
  └─> GetOrCreateItem("Sensor_Temperature")
  └─> Determine value type:
      ├─> UDT: item.WriteAsync(value)  // Direct write
      ├─> Array: item.WriteAsync(object[])  // Convert array
      └─> Primitive: item.WriteAsync(new object[] { value })  // Wrap in array
  └─> Return success/failure
```

### WriteTagsAsync - Batch Write

**Code:** `EdgePlcDriver.WriteTagsAsync()`

```csharp
public async Task WriteTagsAsync(
    IReadOnlyDictionary<string, object> tagValues,
    CancellationToken cancellationToken = default)
{
    // Write tags sequentially to maintain order
    foreach (var (tagName, value) in tagValues)
    {
        await WriteTagInternalAsync(tagName, value, cancellationToken).ConfigureAwait(false);
    }
}
```

**Note:** Currently writes tags **sequentially** (not in parallel) to maintain write order. This could be optimized in the future for independent tags.

---

## Event System Integration

### EdgePlcDriverRead Attribute

**Purpose:** Automatically read PLC tags when an event is triggered (not continuously polled).

**Code:** `EdgePlcDriverReadAttribute` (inherits from `TagReadAttribute`)

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class EdgePlcDriverReadAttribute : TagReadAttribute
{
    public int Elements { get; set; } = 1;  // For array tags
    
    public EdgePlcDriverReadAttribute(string connectionName, string tagName)
        : base(connectionName, tagName)
    {
    }
}
```

**Usage Example:**
```csharp
[Event("GetMachineStatus")]
[EdgePlcDriverRead("plc1", "Machine_Status")]
[EdgePlcDriverRead("plc1", "Temperature", typeof(float))]
public class GetMachineStatusHandler : IEventHandler<MachineRequest>
{
    public Task HandleAsync(MachineRequest request, TagReadResults tags, CancellationToken ct)
    {
        var status = tags.Get<MachineStatusUdt>("Machine_Status");
        var temp = tags.Get<float>("Temperature");
        // ...
    }
}
```

### EdgePlcDriverTagReader

**Code:** `EdgePlcDriverTagReader` (implements `ITagReader`)

```csharp
public class EdgePlcDriverTagReader : ITagReader
{
    private readonly IEdgePlcDriver _connection;
    
    public async Task<TagReadValue<object?>> ReadTagAsync(
        string tagName,
        Type? valueType,
        CancellationToken cancellationToken = default)
    {
        // Use reflection to call generic ReadTagAsync<T>
        var method = typeof(IEdgePlcDriver).GetMethod(nameof(IEdgePlcDriver.ReadTagAsync));
        var genericMethod = method.MakeGenericMethod(valueType ?? typeof(object));
        
        var task = (Task?)genericMethod.Invoke(_connection, new object[] { tagName, cancellationToken });
        await task;
        
        // Extract result from Task<TagValue<T>>
        var resultProperty = task.GetType().GetProperty("Result");
        var result = resultProperty?.GetValue(task);
        
        // Convert TagValue<T> to TagReadValue<object?>
        return MapToTagReadValue(result);
    }
}
```

### Event Flow with EdgePlcDriverRead

```
1. Event is emitted via EventMediator.EmitAsync("GetMachineStatus", request)
   │
   └─> EventMediator finds handlers with [Event("GetMachineStatus")]
       │
       └─> For each handler:
           │
           ├─> Find [EdgePlcDriverRead] attributes on handler class/method
           │
           ├─> For each [EdgePlcDriverRead("plc1", "TagName")]:
           │   ├─> Get IEdgePlcDriver connection named "plc1" from Conduit
           │   ├─> Create EdgePlcDriverTagReader(connection)
           │   └─> Call tagReader.ReadTagAsync("TagName", valueType)
           │       └─> Uses reflection to call connection.ReadTagAsync<T>("TagName")
           │           └─> Returns TagValue<T>
           │
           ├─> Collect all TagReadValue results into TagReadResults
           │
           └─> Invoke handler.HandleAsync(request, tagReadResults, ct)
               └─> Handler can access tags via tagReadResults.Get<T>("TagName")
```

**Key Points:**
- Tags are read **once** when the event is emitted (not continuously)
- Multiple `[EdgePlcDriverRead]` attributes can be used on the same handler
- The `TagReadResults` parameter is automatically injected into the handler
- Type information from the attribute (if provided) is used for proper conversion

### EdgePlcDriverEventContext

**Code:** `EdgePlcDriverEventContext` (implements `IEdgePlcDriverEventContext`)

Provides read/write access to PLC tags during event handling:

```csharp
public class EdgePlcDriverEventContext : IEdgePlcDriverEventContext
{
    private readonly IEdgePlcDriver _connection;
    
    public async Task<TagReadValue<T>> ReadTagAsync<T>(
        string tagName, 
        CancellationToken cancellationToken = default)
    {
        var result = await _connection.ReadTagAsync<T>(tagName, cancellationToken);
        return new TagReadValue<T>
        {
            TagName = tagName,
            Value = result.Value,
            Quality = MapQuality(result.Quality),
            Timestamp = result.Timestamp
        };
    }
    
    public Task WriteTagAsync<T>(string tagName, T value, CancellationToken cancellationToken = default)
    {
        return _connection.WriteTagAsync(tagName, value, cancellationToken);
    }
}
```

**Usage:** Event handlers can receive `IEdgePlcDriverEventContext` as a constructor parameter or method parameter to read/write tags during event processing.

---

## Conclusion

The internal architecture of **Conduit.EdgePlcDriver** is based on:

1. **Reflection-based Discovery:** Automatically finds handlers by scanning assemblies
2. **Greedy Constructor Injection:** Resolves dependencies without explicitly registering handlers
3. **Event-Driven Dispatch:** ASComm IoT fires events, Conduit routes them to handlers
4. **Object Caching:** ASComm Items are cached to avoid recreation
5. **Dual Group Strategy:** Normal polling vs unsolicited (10ms) in separate groups
6. **Quality Tracking:** Each value has quality metadata from the PLC
7. **Automatic Marshalling:** UDTs are converted automatically via Marshal.PtrToStructure

The code is designed to completely abstract ASComm IoT complexity while maintaining flexibility for advanced cases.
