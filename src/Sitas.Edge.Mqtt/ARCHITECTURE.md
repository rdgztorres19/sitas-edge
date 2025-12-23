# Conduit.Mqtt - Internal Code Architecture

Documentation of **how the code is implemented internally**: handler discovery, resolution, topic matching, data flow, internal structures.

## Table of Contents

1. [Handler Discovery](#handler-discovery)
2. [Handler Resolution and Activation](#handler-resolution-and-activation)
3. [Internal Data Structures](#internal-data-structures)
4. [Topic Matching Engine](#topic-matching-engine)
5. [Subscription Flow](#subscription-flow)
6. [Message Dispatching](#message-dispatching)
7. [MQTTnet Client Management](#mqttnet-client-management)
8. [Serialization and Deserialization](#serialization-and-deserialization)

---

## Handler Discovery

### How Handlers Are Found

**Código:** `HandlerDiscoveryService.DiscoverInAssembly()` (Conduit.Core)

```csharp
private static IEnumerable<HandlerRegistration> DiscoverInAssembly(Assembly assembly)
{
    // 1. Get all types from the assembly
    var handlerTypes = assembly.GetTypes()
        .Where(IsValidHandlerType);

    foreach (var handlerType in handlerTypes)
    {
        // 2. Look for [MqttSubscribe] attributes (inherits from [Subscribe])
        var subscribeAttributes = handlerType.GetCustomAttributes<SubscribeAttribute>();
        
        // 3. Extract message type from IMessageSubscriptionHandler<TMessage>
        var messageType = GetMessageType(handlerType);

        // 4. Create HandlerRegistration for each attribute
        foreach (var attribute in subscribeAttributes)
        {
            yield return new HandlerRegistration(
                handlerType,                     // typeof(TemperatureHandler)
                messageType,                     // typeof(MqttMessage)
                attribute.ConnectionName,         // "mqtt1"
                attribute.Topic,                  // "sensors/temperature/#"
                attribute.QualityOfService,       // QoS.AtLeastOnce
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
        && type.GetCustomAttributes<SubscribeAttribute>().Any()      // Has [MqttSubscribe]
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
// class TemperatureHandler : IMessageSubscriptionHandler<MqttMessage>
// messageHandlerInterface = IMessageSubscriptionHandler<MqttMessage>
// GetGenericArguments()[0] = MqttMessage
```

### When Discovery is Executed

```
MqttClientBuilder.WithHandlersFromEntryAssembly()
  └─> HandlerDiscoveryService.DiscoverHandlersFromEntryAssembly()
      └─> DiscoverInAssembly(Assembly.GetEntryAssembly())
          └─> [List<HandlerRegistration>]
              └─> Passed to MqttConnection constructor
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
    
    if (paramType == typeof(IMqttConnection))
        return GetConnectionFromConduit<IMqttConnection>();
    
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
MQTTnet client receives message
  └─> ApplicationMessageReceivedAsync event
      └─> MqttConnection.OnApplicationMessageReceivedAsync()
          └─> DispatchToAttributeHandlersAsync(topic, payload)
              └─> foreach handler in _handlerRegistrations
                  └─> TopicMatcher.IsMatch(handler.Topic, receivedTopic)?
                      └─> using var scopedHandler = _handlerResolver.ResolveScoped(handlerType)
                          └─> FuncActivator.CreateInstance(handlerType)
                              └─> Greedy constructor resolution
```

---

## Internal Data Structures

### Main Fields of MqttConnection

```csharp
// MqttConnection.cs (lines 31-52)
internal sealed class MqttConnection : IMqttConnection
{
    // Configuration
    private readonly MqttConnectionOptions _options;
    private readonly IReadOnlyList<HandlerRegistration> _handlerRegistrations;  // Discovered
    
    // Dependencies
    private readonly IMessageSerializer _serializer;
    private readonly IHandlerResolver _handlerResolver;
    private readonly ILogger<MqttConnection> _logger;
    private readonly MqttPublisher _publisher;
    
    // Thread safety
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    
    // Runtime subscriptions (programmatic)
    private readonly Dictionary<string, List<DynamicHandler>> _dynamicHandlers = new();
    
    // MQTTnet client
    private IMqttClient? _mqttClient;
    
    // State
    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _disposed;
    
    // Subscription tracking
    private readonly HashSet<string> _subscribedTopics = new();
}
```

### HandlerRegistration

```csharp
// Discovery/HandlerRegistration.cs (from Conduit.Core)
public class HandlerRegistration
{
    public Type HandlerType { get; }           // typeof(TemperatureHandler)
    public Type MessageType { get; }           // typeof(MqttMessage)
    public string ConnectionName { get; }      // "mqtt1"
    public string Topic { get; }               // "sensors/temperature/#"
    public QualityOfService QualityOfService { get; }  // QoS 0, 1, 2
    public SubscribeAttribute Attribute { get; }  // [MqttSubscribe] original
}
```

### DynamicHandler (para subscripciones programáticas)

```csharp
// Internal
internal class DynamicHandler
{
    public Type MessageType { get; set; }
    public Func<object, IMessageContext, CancellationToken, Task> Handler { get; set; }
}
```

---

## Topic Matching Engine

### TopicMatcher - Algoritmo de Wildcards

**Código:** `TopicMatcher.IsMatch()` (Internal)

```csharp
// Internal/TopicMatcher.cs
internal static class TopicMatcher
{
    /// <summary>
    /// Determines if a received topic matches a subscription filter.
    /// Supports MQTT wildcards: + (single level) and # (multi-level)
    /// </summary>
    public static bool IsMatch(string filter, string topic)
    {
        // Trivial cases
        if (filter == topic)
            return true;
        
        if (filter == "#")  // Global wildcard
            return true;
        
        // Split by level separator
        var filterLevels = filter.Split('/');
        var topicLevels = topic.Split('/');
        
        int filterIndex = 0;
        int topicIndex = 0;
        
        while (filterIndex < filterLevels.Length && topicIndex < topicLevels.Length)
        {
            var filterLevel = filterLevels[filterIndex];
            
            // Multi-level wildcard (must be last level)
            if (filterLevel == "#")
            {
                return filterIndex == filterLevels.Length - 1;  // # is only valid at the end
            }
            
            // Single-level wildcard
            if (filterLevel == "+")
            {
                filterIndex++;
                topicIndex++;
                continue;
            }
            
            // Exact match required
            if (filterLevel != topicLevels[topicIndex])
            {
                return false;
            }
            
            filterIndex++;
            topicIndex++;
        }
        
        // Both indexes must be at the end (same length)
        return filterIndex == filterLevels.Length && topicIndex == topicLevels.Length;
    }
}

// Examples:
// IsMatch("sensors/+/temperature", "sensors/room1/temperature") → true
// IsMatch("sensors/+/temperature", "sensors/room1/humidity") → false
// IsMatch("sensors/#", "sensors/room1/temperature") → true
// IsMatch("sensors/#", "sensors/room1/temperature/current") → true
// IsMatch("sensors/+", "sensors/room1/temperature") → false (+ is single level)
```

### Topic Matching en Dispatch

```csharp
// MqttConnection.DispatchToAttributeHandlersAsync()
private async Task DispatchToAttributeHandlersAsync(string topic, byte[] payload, IMessageContext context)
{
    // Filter handlers matching the topic
    var matchingHandlers = _handlerRegistrations
        .Where(r => TopicMatcher.IsMatch(r.Topic, topic))
        .ToList();
    
    _logger.LogDebug("Found {Count} matching handlers for topic {Topic}", 
        matchingHandlers.Count, topic);
    
    foreach (var registration in matchingHandlers)
    {
        await InvokeHandlerAsync(registration, payload, context);
    }
}
```

---

## Subscription Flow

### Attribute-Based Subscriptions

```
1. ConnectAsync()
   │
   ├─> CreateMqttClient()
   │   └─> _mqttClient = new MqttFactory().CreateMqttClient()
   │
   ├─> BuildMqttClientOptions()
   │   ├─> Server, Port, ClientId
   │   ├─> Credentials (username/password)
   │   ├─> TLS options
   │   └─> Clean session, keep alive
   │
   ├─> await _mqttClient.ConnectAsync(options)
   │
   ├─> SubscribeToRegisteredHandlersAsync()
   │   │
   │   └─> Group unique topics from _handlerRegistrations
   │       └─> var topicsToSubscribe = _handlerRegistrations
   │               .Select(r => r.Topic)
   │               .Distinct();
   │       │
   │       └─> foreach topic in topicsToSubscribe:
   │           ├─> var qos = DetermineHighestQos(topic)
   │           └─> await _mqttClient.SubscribeAsync(
   │                   new MqttTopicFilterBuilder()
   │                       .WithTopic(topic)
   │                       .WithQualityOfServiceLevel(qos)
   │                       .Build()
   │               );
   │           └─> _subscribedTopics.Add(topic)
   │
   └─> Attach event handler
       _mqttClient.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
```

### Programmatic Subscriptions (Runtime)

```csharp
// MqttConnection.SubscribeAsync<TMessage>()
public async Task<IAsyncDisposable> SubscribeAsync<TMessage>(
    string topic,
    Func<TMessage, IMessageContext, CancellationToken, Task> handler,
    QualityOfService qos,
    CancellationToken ct)
{
    // 1. Add handler to dynamic dictionary
    if (!_dynamicHandlers.TryGetValue(topic, out var handlers))
    {
        handlers = new List<DynamicHandler>();
        _dynamicHandlers[topic] = handlers;
    }
    
    var dynamicHandler = new DynamicHandler
    {
        MessageType = typeof(TMessage),
        Handler = async (msg, ctx, ct) =>
        {
            var typedMsg = (TMessage)msg;
            await handler(typedMsg, ctx, ct);
        }
    };
    
    handlers.Add(dynamicHandler);
    
    // 2. If first subscription to this topic, subscribe on broker
    if (handlers.Count == 1 && !_subscribedTopics.Contains(topic))
    {
        await _mqttClient.SubscribeAsync(
            new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(MapQoS(qos))
                .Build(),
            ct);
        
        _subscribedTopics.Add(topic);
    }
    
    // 3. Return IAsyncDisposable for cleanup
    return new DynamicSubscription(topic, dynamicHandler, this);
}

// DynamicSubscription.DisposeAsync() removes the handler from dictionary
```

---

## Message Dispatching

### MQTTnet Event → Handlers

```
MQTTnet Library
  └─> MQTT message received from broker
      └─> ApplicationMessageReceivedAsync event fires
          └─> MqttConnection.OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
              │
              ├─> var topic = e.ApplicationMessage.Topic
              ├─> var payload = e.ApplicationMessage.Payload
              ├─> var qos = e.ApplicationMessage.QualityOfServiceLevel
              ├─> var retain = e.ApplicationMessage.Retain
              │
              └─> var context = new MqttMessageContext
                  {
                      Topic = topic,
                      QualityOfService = MapQoS(qos),
                      Retain = retain,
                      Timestamp = DateTimeOffset.UtcNow
                  };
              │
              ├─> await DispatchToAttributeHandlersAsync(topic, payload, context)
              └─> await DispatchToDynamicHandlersAsync(topic, payload, context)

private async Task DispatchToAttributeHandlersAsync(string topic, byte[] payload, IMessageContext context)
{
    // Filter handlers using TopicMatcher
    var matchingHandlers = _handlerRegistrations
        .Where(r => TopicMatcher.IsMatch(r.Topic, topic))
        .ToList();

    foreach (var registration in matchingHandlers)
    {
        try
        {
            // Create scope for handler
            using var scopedHandler = _handlerResolver.ResolveScoped(registration.HandlerType);
            var handler = scopedHandler.Handler;
            
            // Deserialize payload
            var message = _serializer.Deserialize(payload, registration.MessageType);
            
            // Invoke HandleAsync via reflection
            var method = registration.HandlerType.GetMethod("HandleAsync");
            var task = (Task)method.Invoke(handler, new[] { message, context, _disposeCts.Token });
            await task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error dispatching message from topic {Topic} to handler {HandlerType}", 
                topic, 
                registration.HandlerType.Name);
        }
    }
}

private async Task DispatchToDynamicHandlersAsync(string topic, byte[] payload, IMessageContext context)
{
    // Find dynamic handlers that match (exact or with wildcards)
    var matchingDynamicHandlers = _dynamicHandlers
        .Where(kvp => TopicMatcher.IsMatch(kvp.Key, topic))
        .SelectMany(kvp => kvp.Value);

    foreach (var dynamicHandler in matchingDynamicHandlers)
    {
        try
        {
            // Deserialize payload
            var message = _serializer.Deserialize(payload, dynamicHandler.MessageType);
            
            // Invoke lambda handler
            await dynamicHandler.Handler(message, context, _disposeCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching to dynamic handler for topic {Topic}", topic);
        }
    }
}
```

---

## MQTTnet Client Management

### Client Creation

```csharp
// MqttConnection.CreateMqttClient()
private void CreateMqttClient()
{
    var factory = new MqttFactory();
    _mqttClient = factory.CreateMqttClient();
    
    // Attach event handlers
    _mqttClient.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
    _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
    _mqttClient.ConnectedAsync += OnConnectedAsync;
}
```

### Connection Options Construction

```csharp
// MqttConnection.BuildMqttClientOptions()
private MqttClientOptions BuildMqttClientOptions()
{
    var builder = new MqttClientOptionsBuilder()
        .WithTcpServer(_options.Server, _options.Port)
        .WithClientId(_options.ClientId ?? Guid.NewGuid().ToString())
        .WithCleanSession(_options.CleanSession)
        .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds));

    // Credentials
    if (!string.IsNullOrEmpty(_options.Username))
    {
        builder.WithCredentials(_options.Username, _options.Password);
    }

    // TLS
    if (_options.UseTls)
    {
        builder.WithTls(new MqttClientOptionsBuilderTlsParameters
        {
            UseTls = true,
            AllowUntrustedCertificates = _options.AllowUntrustedCertificates,
            IgnoreCertificateChainErrors = _options.IgnoreCertificateChainErrors,
            IgnoreCertificateRevocationErrors = _options.IgnoreCertificateRevocationErrors
        });
    }

    // Last Will (unexpected disconnection message)
    if (_options.LastWill != null)
    {
        builder.WithWillTopic(_options.LastWill.Topic)
               .WithWillPayload(_options.LastWill.Payload)
               .WithWillQualityOfServiceLevel(MapQoS(_options.LastWill.QoS))
               .WithWillRetain(_options.LastWill.Retain);
    }

    return builder.Build();
}
```

### Automatic Reconnection

```csharp
// MqttConnection.OnDisconnectedAsync()
private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
{
    _logger.LogWarning("Disconnected from MQTT broker: {Reason}", e.Reason);
    
    SetState(ConnectionState.Disconnected);
    
    // Auto-reconnection if enabled
    if (_options.AutoReconnect && !_disposed)
    {
        _logger.LogInformation("Attempting to reconnect in {Delay}s...", _options.ReconnectDelaySeconds);
        
        await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds));
        
        try
        {
            await ConnectAsync(_disposeCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconnection failed");
        }
    }
}
```

---

## Serialization and Deserialization

### IMessageSerializer

```csharp
// Abstractions/IMessageSerializer.cs
public interface IMessageSerializer
{
    byte[] Serialize<T>(T message);
    object Deserialize(byte[] data, Type type);
    T Deserialize<T>(byte[] data);
}
```

### JsonMessageSerializer (default implementation)

```csharp
// Internal/JsonMessageSerializer.cs
internal class JsonMessageSerializer : IMessageSerializer
{
    private readonly JsonSerializerOptions _options;

    public JsonMessageSerializer()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    public byte[] Serialize<T>(T message)
    {
        var json = JsonSerializer.Serialize(message, _options);
        return Encoding.UTF8.GetBytes(json);
    }

    public object Deserialize(byte[] data, Type type)
    {
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize(json, type, _options)
            ?? throw new InvalidOperationException("Deserialization returned null");
    }

    public T Deserialize<T>(byte[] data)
    {
        return (T)Deserialize(data, typeof(T));
    }
}
```

### Usage in Dispatch

```csharp
// In DispatchToAttributeHandlersAsync()
var message = _serializer.Deserialize(payload, registration.MessageType);

// In PublishAsync()
var payload = _serializer.Serialize(message);
await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
    .WithTopic(topic)
    .WithPayload(payload)
    .Build());
```

---

## Internal Flows Summary

### Complete Subscription Flow (Attribute-Based)

```
1. Startup
   └─> HandlerDiscoveryService.DiscoverHandlers()
       └─> Finds [MqttSubscribe] en handlers
           └─> Creates HandlerRegistration list

2. Build
   └─> MqttConnection constructor receives HandlerRegistration list

3. ConnectAsync()
   ├─> Creates MqttClient (MQTTnet)
   ├─> Builds MqttClientOptions
   ├─> await _mqttClient.ConnectAsync(options)
   ├─> For each unique topic in HandlerRegistration:
   │   └─> await _mqttClient.SubscribeAsync(topic, qos)
   └─> Attach ApplicationMessageReceivedAsync event

4. Mensaje MQTT Recibido
   └─> MQTTnet dispara ApplicationMessageReceivedAsync
       └─> OnApplicationMessageReceivedAsync(e)
           ├─> Extracts topic, payload, qos del evento
           ├─> Filters handlers usando TopicMatcher.IsMatch()
           └─> Para cada handler matching:
               ├─> Resolve handler instance (FuncActivator)
               ├─> Deserializa payload usando IMessageSerializer
               └─> Invoca HandleAsync() via reflection
```

### Publish Flow

```
PublishAsync<T>("topic/name", message)
  └─> Serialize message: byte[] payload = _serializer.Serialize(message)
  └─> Create MqttApplicationMessage
      ├─> Topic = "topic/name"
      ├─> Payload = payload
      ├─> QoS = AtLeastOnce
      └─> Retain = false
  └─> await _mqttClient.PublishAsync(mqttMessage)
  └─> Return success/failure
```

### Dynamic Subscribe Flow

```
SubscribeAsync("sensors/+/temperature", handler)
  ├─> Agregar lambda handler a _dynamicHandlers dictionary
  ├─> If first handler for this topic:
  │   └─> await _mqttClient.SubscribeAsync("sensors/+/temperature")
  └─> Retornar DynamicSubscription (IAsyncDisposable)
      └─> On Dispose:
          ├─> Remove handler from dictionary
          └─> If it was the last handler for this topic:
              └─> await _mqttClient.UnsubscribeAsync(topic)
```

---

## Conclusion

The internal architecture of **Conduit.Mqtt** is based on:

1. **Reflection-based Discovery:** Automatically finds handlers by scanning assemblies
2. **Greedy Constructor Injection:** Resolves dependencies without explicitly registering handlers
3. **Topic Matching Engine:** Custom algorithm for MQTT wildcards (+ and #)
4. **Event-Driven Dispatch:** MQTTnet fires events, Conduit routes them using TopicMatcher
5. **Dual Handler Support:** Attribute-based (compile-time) and Dynamic (runtime)
6. **JSON Serialization:** By default, with support for custom serializers
7. **Auto-Reconnection:** Automatic disconnection handling with configurable retry
8. **QoS Mapping:** Conversion between Conduit and MQTTnet QoS

The code is designed to completely abstract MQTTnet complexity while maintaining flexibility for advanced pub/sub patterns.
