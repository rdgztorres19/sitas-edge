using System.Linq;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Packets;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Discovery;
using Sitas.Edge.Core.Enums;
using Sitas.Edge.Core.Internal;
using Sitas.Edge.Mqtt.Configuration;
using Sitas.Edge.Mqtt.Internal;

namespace Sitas.Edge.Mqtt;

/// <summary>
/// MQTT connection implementation using MQTTnet.
/// </summary>
internal sealed class MqttConnection : IMqttConnection
{
    private readonly MqttConnectionOptions _options;
    private readonly IReadOnlyList<HandlerRegistration> _handlerRegistrations;
    private readonly IMessageSerializer _serializer;
    private readonly IHandlerResolver _handlerResolver;
    private readonly ILogger<MqttConnection> _logger;
    private readonly IMqttClient _client;
    private readonly MqttPublisher _publisher;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Dictionary<string, List<DynamicHandler>> _dynamicHandlers = new();

    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _disposed;

    public string ConnectionName => _options.ConnectionName;
    public string ConnectionId { get; }
    public ConnectionState State => _state;
    public bool IsConnected => _state == ConnectionState.Connected && _client.IsConnected;
    public IMqttPublisher Publisher => _publisher;
    IMessagePublisher IServiceBusConnection.Publisher => _publisher;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public MqttConnection(
        MqttConnectionOptions options,
        IReadOnlyList<HandlerRegistration> handlerRegistrations,
        IMessageSerializer serializer,
        IHandlerResolver handlerResolver,
        ILogger<MqttConnection> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _handlerRegistrations = handlerRegistrations ?? throw new ArgumentNullException(nameof(handlerRegistrations));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _handlerResolver = handlerResolver ?? throw new ArgumentNullException(nameof(handlerResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ConnectionId = $"{_options.ConnectionName}-{Guid.NewGuid():N}";
        
        _logger.LogInformation("📡 MQTT Connection '{ConnectionName}' initialized with {Count} handler(s)", 
            _options.ConnectionName, _handlerRegistrations.Count);
        
        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();
        _publisher = new MqttPublisher(_client, _serializer, _logger);

        // Wire up events
        _client.ConnectedAsync += OnConnectedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        try
        {
            if (_state == ConnectionState.Connected)
            {
                _logger.LogDebug("Already connected to MQTT broker");
                return;
            }

            SetState(ConnectionState.Connecting);

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(_options.Host, _options.Port)
                .WithClientId(_options.ClientId)
                .WithCleanSession(_options.CleanSession)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
                .WithTimeout(TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds));

            // Configure protocol version
            optionsBuilder = _options.ProtocolVersion switch
            {
                Configuration.MqttProtocolVersion.V311 => optionsBuilder.WithProtocolVersion(MqttProtocolVersion311),
                Configuration.MqttProtocolVersion.V500 => optionsBuilder.WithProtocolVersion(MqttProtocolVersion500),
                _ => optionsBuilder.WithProtocolVersion(MqttProtocolVersion500)
            };

            // Configure credentials
            if (!string.IsNullOrEmpty(_options.Username))
            {
                optionsBuilder.WithCredentials(_options.Username, _options.Password);
            }

            // Configure TLS
            if (_options.UseTls)
            {
                optionsBuilder.WithTlsOptions(tls =>
                {
                    if (!_options.ValidateCertificate)
                    {
                        tls.WithCertificateValidationHandler(_ => true);
                    }
                });
            }

            var mqttOptions = optionsBuilder.Build();

            _logger.LogInformation(
                "Connecting to MQTT broker at {Host}:{Port} with client ID {ClientId}",
                _options.Host,
                _options.Port,
                _options.ClientId);

            await _client.ConnectAsync(mqttOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MQTT broker");
            SetState(ConnectionState.Faulted, ex);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        try
        {
            if (_state == ConnectionState.Disconnected)
            {
                return;
            }

            SetState(ConnectionState.Disconnecting);

            _logger.LogInformation("Disconnecting from MQTT broker");

            var disconnectOptions = new MqttClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                .Build();

            await _client.DisconnectAsync(disconnectOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        Func<ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task> handler,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(handler);

        // Register dynamic handler
        lock (_dynamicHandlers)
        {
            if (!_dynamicHandlers.TryGetValue(topic, out var handlers))
            {
                handlers = [];
                _dynamicHandlers[topic] = handlers;
            }
            
            handlers.Add(new DynamicHandler(null, handler));
        }

        // Subscribe to topic
        await SubscribeToTopicAsync(topic, qos, cancellationToken).ConfigureAwait(false);

        return new DynamicSubscription(topic, UnsubscribeDynamicAsync);
    }

    public async Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        Func<string, ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task> handler,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(handler);

        // Register dynamic handler with topic awareness
        lock (_dynamicHandlers)
        {
            if (!_dynamicHandlers.TryGetValue(topic, out var handlers))
            {
                handlers = [];
                _dynamicHandlers[topic] = handlers;
            }
            
            handlers.Add(new DynamicHandler(null, null, handler));
        }

        // Subscribe to topic
        await SubscribeToTopicAsync(topic, qos, cancellationToken).ConfigureAwait(false);

        return new DynamicSubscription(topic, UnsubscribeDynamicAsync);
    }

    public async Task<IAsyncDisposable> SubscribeAsync<TMessage>(
        string topic,
        Func<TMessage, IMessageContext, CancellationToken, Task> handler,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        Task RawHandler(ReadOnlyMemory<byte> payload, IMessageContext context, CancellationToken ct)
        {
            var message = _serializer.Deserialize<TMessage>(payload);
            return handler(message, context, ct);
        }

        return await SubscribeAsync(topic, RawHandler, qos, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        SetState(ConnectionState.Connected);
        _logger.LogInformation("Connected to MQTT broker");

        // Subscribe to all registered handlers
        await SubscribeToRegisteredHandlersAsync().ConfigureAwait(false);
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        var previousState = _state;
        
        if (args.Exception is not null)
        {
            _logger.LogWarning(args.Exception, "Disconnected from MQTT broker due to error");
            SetState(ConnectionState.Faulted, args.Exception);
        }
        else
        {
            SetState(ConnectionState.Disconnected);
            _logger.LogInformation("Disconnected from MQTT broker");
        }

        // Handle auto-reconnect
        if (_options.AutoReconnect && previousState == ConnectionState.Connected && !_disposed)
        {
            await HandleReconnectAsync().ConfigureAwait(false);
        }
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var payload = args.ApplicationMessage.PayloadSegment;
        
        _logger.LogDebug("📥 MQTT message received on topic '{Topic}' ({Length} bytes)", topic, payload.Count);

        try
        {
            var context = new DefaultMessageContext(
                topic,
                payload,
                _publisher,
                correlationId: GetCorrelationId(args.ApplicationMessage),
                metadata: GetUserProperties(args.ApplicationMessage));

            // Dispatch to attribute-based handlers
            await DispatchToHandlersAsync(topic, payload, context).ConfigureAwait(false);

            // Dispatch to dynamic handlers
            await DispatchToDynamicHandlersAsync(topic, payload, context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing message on topic '{Topic}'", topic);
        }
    }

    private async Task DispatchToHandlersAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        IMessageContext context)
    {
        if (_handlerRegistrations.Count == 0)
        {
            _logger.LogWarning("⚠️ No registered handlers found for connection '{ConnectionName}'", _options.ConnectionName);
            return;
        }

        _logger.LogDebug("🔍 Checking {Count} registered handler(s) for topic {Topic}", _handlerRegistrations.Count, topic);

        var matchedRegistrations = 0;
        foreach (var registration in _handlerRegistrations)
        {
            if (!TopicMatcher.Matches(registration.Topic, topic))
            {
                _logger.LogTrace("❌ Handler {HandlerType} pattern '{Pattern}' does not match topic '{Topic}'", 
                    registration.HandlerType.Name, registration.Topic, topic);
                continue;
            }

            matchedRegistrations++;
            _logger.LogInformation("✅ Dispatching to handler {HandlerType} for topic '{Topic}'", 
                registration.HandlerType.Name, topic);

            try
            {
                // Use scoped resolution to support scoped services in handlers
                using var scopedHandler = _handlerResolver.ResolveScoped(registration.HandlerType);
                var handler = scopedHandler.Handler;
                
                if (handler == null)
                {
                    Console.WriteLine($"❌ MqttConnection: Handler {registration.HandlerType.Name} resolved to null for topic '{topic}'");
                    _logger.LogError("Handler {HandlerType} resolved to null", registration.HandlerType.Name);
                    continue;
                }
                
                var message = _serializer.Deserialize(payload, registration.MessageType);

                // Get the HandleAsync method and invoke it
                var method = registration.HandlerType.GetMethod("HandleAsync");
                
                if (method is not null)
                {
                    var task = (Task?)method.Invoke(handler, [message, context, _disposeCts.Token]);
                    
                    if (task is not null)
                    {
                        await task.ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ HandleAsync method returned null task for handler {HandlerType}", registration.HandlerType.Name);
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ HandleAsync method not found on handler {HandlerType}", registration.HandlerType.Name);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ MqttConnection: Error creating/invoking handler {registration.HandlerType.Name} for topic '{topic}'");
                Console.WriteLine($"   Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   Inner: {ex.InnerException.Message}");
                }
                _logger.LogError(ex, "Error dispatching message to handler {HandlerType} for topic '{Topic}'", 
                    registration.HandlerType.Name, topic);
            }
        }

        if (matchedRegistrations == 0)
        {
            _logger.LogWarning("⚠️ No handlers matched topic {Topic}. Registered topics: {Topics}", 
                topic, 
                string.Join(", ", _handlerRegistrations.Select(r => r.Topic).Distinct()));
        }
    }

    private async Task DispatchToDynamicHandlersAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        IMessageContext context)
    {
        List<DynamicHandler>? handlers;
        
        lock (_dynamicHandlers)
        {
            var matchingTopics = _dynamicHandlers.Keys
                .Where(pattern => TopicMatcher.Matches(pattern, topic));

            handlers = matchingTopics
                .SelectMany(t => _dynamicHandlers[t])
                .ToList();
        }

        foreach (var handler in handlers)
        {
            try
            {
                if (handler.HandlerWithTopic is not null)
                {
                    await handler.HandlerWithTopic(topic, payload, context, _disposeCts.Token).ConfigureAwait(false);
                }
                else if (handler.Handler is not null)
                {
                    await handler.Handler(payload, context, _disposeCts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching message to dynamic handler for topic {Topic}", topic);
            }
        }
    }

    private async Task SubscribeToRegisteredHandlersAsync()
    {
        _logger.LogInformation(
            "📡 Subscribing to {Count} handler(s) for connection '{ConnectionName}'",
            _handlerRegistrations.Count,
            _options.ConnectionName);

        var topics = _handlerRegistrations
            .Select(r => (r.Topic, r.QualityOfService))
            .Distinct();

        foreach (var (topic, qos) in topics)
        {
            _logger.LogInformation("🔔 Subscribing to topic: {Topic} (QoS: {QoS})", topic, qos);
            await SubscribeToTopicAsync(topic, qos, _disposeCts.Token).ConfigureAwait(false);
        }
    }

    private async Task SubscribeToTopicAsync(string topic, QualityOfService qos, CancellationToken cancellationToken)
    {
        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f
                .WithTopic(topic)
                .WithQualityOfServiceLevel(MapQos(qos)))
            .Build();

        await _client.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);
        
        _logger.LogInformation("📡 Subscribed to topic '{Topic}' with QoS {QoS}", topic, qos);
    }

    private async Task UnsubscribeDynamicAsync(string topic)
    {
        lock (_dynamicHandlers)
        {
            _dynamicHandlers.Remove(topic);
        }

        if (_client.IsConnected)
        {
            var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
                .WithTopicFilter(topic)
                .Build();

            await _client.UnsubscribeAsync(unsubscribeOptions).ConfigureAwait(false);
        }
    }

    private async Task HandleReconnectAsync()
    {
        SetState(ConnectionState.Reconnecting);
        
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(_options.MaxReconnectDelaySeconds);

        while (!_disposed && _state == ConnectionState.Reconnecting)
        {
            try
            {
                _logger.LogInformation("Attempting to reconnect to MQTT broker in {Delay}s", delay.TotalSeconds);
                
                await Task.Delay(delay, _disposeCts.Token).ConfigureAwait(false);
                await ConnectAsync(_disposeCts.Token).ConfigureAwait(false);
                
                return;
            }
            catch (OperationCanceledException) when (_disposed)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnection attempt failed");
                
                // Exponential backoff
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
            }
        }
    }

    private void SetState(ConnectionState newState, Exception? exception = null)
    {
        var previousState = _state;
        _state = newState;

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(previousState, newState, exception));
    }

    private static MQTTnet.Protocol.MqttQualityOfServiceLevel MapQos(QualityOfService qos)
    {
        return qos switch
        {
            QualityOfService.AtMostOnce => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce,
            QualityOfService.AtLeastOnce => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce,
            QualityOfService.ExactlyOnce => MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce
        };
    }

    private static string? GetCorrelationId(MqttApplicationMessage message)
    {
        if (message.CorrelationData is { Length: > 0 })
        {
            return Convert.ToBase64String(message.CorrelationData);
        }
        return null;
    }

    private static IReadOnlyDictionary<string, string> GetUserProperties(MqttApplicationMessage message)
    {
        if (message.UserProperties is null || message.UserProperties.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return message.UserProperties.ToDictionary(p => p.Name, p => p.Value);
    }

    private static MQTTnet.Formatter.MqttProtocolVersion MqttProtocolVersion311 => MQTTnet.Formatter.MqttProtocolVersion.V311;
    private static MQTTnet.Formatter.MqttProtocolVersion MqttProtocolVersion500 => MQTTnet.Formatter.MqttProtocolVersion.V500;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _disposeCts.Cancel();
        
        _client.Dispose();
        _connectionLock.Dispose();
        _disposeCts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _disposeCts.Cancel();

        try
        {
            if (_client.IsConnected)
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _client.Dispose();
            _connectionLock.Dispose();
            _disposeCts.Dispose();
        }
    }

    private sealed record DynamicHandler(
        Type? MessageType,
        Func<ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task>? Handler,
        Func<string, ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task>? HandlerWithTopic = null);
}

