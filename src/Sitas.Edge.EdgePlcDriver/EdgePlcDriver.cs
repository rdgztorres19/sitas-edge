using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AutomatedSolutions.ASCommStd;
using ABLogix = AutomatedSolutions.ASCommStd.AB.Logix;
using Sitas.Edge.EdgePlcDriver.Configuration;
using Sitas.Edge.EdgePlcDriver.Internal;
using Sitas.Edge.EdgePlcDriver.Messages;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Enums;
using Quality = AutomatedSolutions.ASCommStd.Quality;

namespace Sitas.Edge.EdgePlcDriver;

/// <summary>
/// Edge PLC Driver implementation for Allen-Bradley/ControlLogix PLCs.
/// Provides high-level abstractions for PLC communication using ASComm IoT library from Automated Solutions.
/// </summary>
/// <remarks>
/// Supports:
/// - ControlLogix, CompactLogix, GuardPLC, SoftLogix, Micro800 families
/// - Primitive types (BOOL, INT, DINT, REAL, etc.)
/// - User-Defined Types (UDTs/structures)
/// - Arrays and string types
/// - Polling and DataChanged event subscriptions
/// </remarks>
internal sealed class EdgePlcDriver : IEdgePlcDriver
{
    private readonly EdgePlcDriverOptions _options;
    private readonly IReadOnlyList<TagHandlerRegistration> _handlerRegistrations;
    private readonly IMessageSerializer _serializer;
    private readonly IHandlerResolver _handlerResolver;
    private readonly ILogger<EdgePlcDriver> _logger;
    private readonly EdgePlcDriverPublisher _publisher;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ConcurrentDictionary<string, TagHandler> _dynamicHandlers = new();
    private readonly ConcurrentDictionary<string, object?> _lastTagValues = new();
    private readonly ConcurrentDictionary<string, ABLogix.Item> _tagItems = new();

    // ASComm IoT objects hierarchy: Channel → Device → Group → Item
    private ABLogix.Net.Channel? _channel;
    private ABLogix.Device? _device;
    private ABLogix.Group? _pollingGroup;
    private ABLogix.Group? _unsolicitedGroup;
    private ABLogix.Group? _writeGroup; // Group separado para escrituras (Active = false, no hace polling)

    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _disposed;
    private Task? _reconnectTask;

    public string ConnectionName => _options.ConnectionName;
    public string ConnectionId { get; }
    public ConnectionState State => _state;
    public bool IsConnected => _state == ConnectionState.Connected;
    public string IpAddress => _options.IpAddress;
    public string RoutePath => _options.RoutePath;

    public IEdgePlcDriverPublisher Publisher => _publisher;
    IMessagePublisher IServiceBusConnection.Publisher => _publisher;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public EdgePlcDriver(
        EdgePlcDriverOptions options,
        IReadOnlyList<TagHandlerRegistration> handlerRegistrations,
        IMessageSerializer serializer,
        IHandlerResolver handlerResolver,
        ILogger<EdgePlcDriver> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _handlerRegistrations = handlerRegistrations ?? throw new ArgumentNullException(nameof(handlerRegistrations));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _handlerResolver = handlerResolver ?? throw new ArgumentNullException(nameof(handlerResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ConnectionId = $"{_options.ConnectionName}-{Guid.NewGuid():N}";
        _publisher = new EdgePlcDriverPublisher(_logger);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_state == ConnectionState.Connected)
            {
                _logger.LogDebug("Already connected to PLC at {RoutePath}", _options.RoutePath);
                return;
            }

            SetState(ConnectionState.Connecting);

            _logger.LogInformation(
                "🔌 Connecting to PLC at {RoutePath} (IP: {IpAddress}, Slot: {Slot})",
                _options.RoutePath,
                _options.IpAddress,
                _options.CpuSlot);

            // Create ASComm objects hierarchy
            await Task.Run(() => InitializeAsCommObjects(), cancellationToken).ConfigureAwait(false);

            // Setup publisher write function
            _publisher.SetWriteFunction(WriteTagInternalAsync);
            _publisher.SetChannel(_channel);
            _publisher.SetDevice(_device);

            SetState(ConnectionState.Connected);
            _logger.LogInformation("✅ Connected to PLC at {RoutePath}", _options.RoutePath);

            // Start subscriptions for registered handlers
            await StartRegisteredHandlersAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.LicenseException lex)
        {
            _logger.LogError(lex, "ASComm IoT license error. Please ensure a valid license is installed.");
            SetState(ConnectionState.Faulted, lex);
            throw new InvalidOperationException(
                "ASComm IoT license is invalid or expired. Visit https://automatedsolutions.com/products/iot/ascommiot/ for licensing.", lex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to PLC at {RoutePath}", _options.RoutePath);
            SetState(ConnectionState.Faulted, ex);

            if (_options.AutoReconnect)
            {
                StartReconnectTask();
            }
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void InitializeAsCommObjects()
    {
        // 1. Create Channel (Ethernet connection type)
        _channel = new ABLogix.Net.Channel();
        _channel.NotifyOnRemoteDisconnect = _options.NotifyOnRemoteDisconnect;

        // Attach error event handlers
        _channel.Error += Channel_Error;

        // 2. Create Device with route path: "IP,Backplane,Slot"
        var model = _options.Model switch
        {
            Configuration.PlcModel.ControlLogix => ABLogix.Model.ControlLogix,
            Configuration.PlcModel.Micro800 => ABLogix.Model.Micro800,
            // Note: SoftLogix and GuardPLC may not be available in all ASComm versions
            // Fallback to ControlLogix if not available
            Configuration.PlcModel.SoftLogix => ABLogix.Model.ControlLogix, // Fallback
            Configuration.PlcModel.GuardPLC => ABLogix.Model.ControlLogix,     // Fallback
            _ => ABLogix.Model.ControlLogix
        };

        _device = new ABLogix.Device(_options.RoutePath, model);
        _device.TimeoutConnect = _options.ConnectionTimeoutSeconds * 1000; // Convert to ms
        _device.TimeoutTransaction = _options.TransactionTimeoutSeconds * 1000;
        _device.Simulate = _options.SimulateMode;
        _device.Error += Device_Error;

        // 3. Create polling Group for subscriptions
        // Active = false initially, UpdateRate = default polling interval
        _pollingGroup = new ABLogix.Group(false, _options.DefaultPollingIntervalMs);

        // 4. Create unsolicited Group for fast-polling subscriptions
        // "Unsolicited" mode in Conduit uses very fast polling (10-50ms) for near real-time response
        // This gives lower latency than standard polling while working within ASComm's capabilities
        _unsolicitedGroup = new ABLogix.Group(false, 10); // 10ms update rate for fast response

        // 5. Create write Group (separate, inactive - only for writes, no polling)
        _writeGroup = new ABLogix.Group(false, 100); // Active = false, no polling
        _writeGroup.UpdateRate = 100; // Not used since Active = false

        // 6. Build the hierarchy
        _channel.Devices.Add(_device);
        _device.Groups.Add(_pollingGroup);
        _device.Groups.Add(_unsolicitedGroup);
        _device.Groups.Add(_writeGroup);
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
            _logger.LogInformation("🔌 Disconnecting from PLC at {RoutePath}", _options.RoutePath);

            // Stop polling and unsolicited groups
            if (_pollingGroup is not null)
            {
                _pollingGroup.Active = false;
            }

            if (_unsolicitedGroup is not null)
            {
                _unsolicitedGroup.Active = false;
            }

            // Detach event handlers and dispose
            CleanupAsCommObjects();

            SetState(ConnectionState.Disconnected);
            _logger.LogInformation("✅ Disconnected from PLC");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void CleanupAsCommObjects()
    {
        if (_channel is not null)
        {
            _channel.Error -= Channel_Error;
        }

        if (_device is not null)
        {
            _device.Error -= Device_Error;
        }

        // Detach item events
        foreach (var item in _tagItems.Values)
        {
            item.Error -= Item_Error;
            item.DataChanged -= Item_DataChanged;
        }
        _tagItems.Clear();

        // Dispose channel (this closes all connections and threads)
        _channel?.Dispose();
        _channel = null;
        _device = null;
        _pollingGroup = null;
        _unsolicitedGroup = null;
    }

    public async Task<TagValue<T>> ReadTagAsync<T>(string tagName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagName);
        EnsureConnected();


        try
        {
            // Get or create item for this tag
            var item = GetOrCreateItem(tagName);

            // Perform async read
            await item.ReadAsync().ConfigureAwait(false);

            var quality = MapQuality(item.Quality);
            
            // Log error if quality is not good
            if (quality != TagQuality.Good)
            {
                _logger.LogError(
                    "❌ Tag '{TagName}' has BAD quality: {Quality} (ASComm Quality: {ASCommQuality})\n" +
                    "   Possible causes:\n" +
                    "   1. Tag name is incorrect or doesn't exist in the PLC\n" +
                    "   2. Tag path is wrong (check Program name, scope, etc.)\n" +
                    "   3. Data type mismatch\n" +
                    "   4. PLC permissions/security settings\n" +
                    "   5. Group is not active or item not added to group correctly",
                    tagName,
                    quality,
                    item.Quality);
            }
            
            T value = default!;

            if (quality == TagQuality.Good && item.Values is not null && item.Values.Length > 0)
            {
                // Check if T is a structured type (UDT)
                if (IsStructuredType<T>())
                {
                    // Create instance and populate using GetStructuredValues
                    value = Activator.CreateInstance<T>();
                    item.GetStructuredValues(value);
                }
                else if (typeof(T).IsArray)
                {
                    // Handle array types
                    value = ConvertToArray<T>(item.Values);
                }
                else
                {
                    // Primitive types
                    var rawValue = item.Values.Length == 1 ? item.Values[0] : item.Values;
                    value = ConvertValue<T>(rawValue);
                }
            }

            var tagValue = new TagValue<T>
            {
                TagName = tagName,
                Value = value,
                Timestamp = DateTimeOffset.UtcNow,
                Quality = quality
            };

            // Track previous value
            if (_lastTagValues.TryGetValue(tagName, out var lastValue) && lastValue is T typedLastValue)
            {
                tagValue.PreviousValue = typedLastValue;
            }

            _lastTagValues[tagName] = value;

            return tagValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to read tag '{TagName}'", tagName);
            return new TagValue<T>
            {
                TagName = tagName,
                Value = default!,
                Quality = TagQuality.CommError,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
    }

    public async Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(
        IEnumerable<string> tagNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagNames);
        EnsureConnected();

        var tagNamesList = tagNames.ToList();
        if (tagNamesList.Count == 0)
        {
            return new Dictionary<string, object?>();
        }


        var results = new Dictionary<string, object?>();

        try
        {
            // Create temporary group for batch read
            var tempGroup = new ABLogix.Group(true, 100); // Active = true to enable reading
            _device?.Groups.Add(tempGroup);

            var tempItems = new Dictionary<string, ABLogix.Item>();

            try
            {
                // Add all tags as items to the group
                foreach (var tagName in tagNamesList)
                {
                    var item = new ABLogix.Item($"temp_{tagName}", tagName);
                    tempGroup.Items.Add(item);
                    tempItems[tagName] = item;
                }

                // Read all items asynchronously - ASComm will batch these reads efficiently when in the same group
                var readTasks = tempItems.Values.Select(item => item.ReadAsync()).ToList();
                await Task.WhenAll(readTasks).ConfigureAwait(false);

                // Collect values from all items
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
                        
                        if (quality != TagQuality.Good)
                        {
                            _logger.LogWarning(
                                "Tag '{TagName}' in batch read has quality: {Quality}",
                                tagName,
                                quality);
                        }
                    }
                }

                _logger.LogDebug(
                    "📖 Batch read completed: {SuccessCount}/{TotalCount} tags",
                    results.Count,
                    tagNamesList.Count);
            }
            finally
            {
                // Cleanup: deactivate and remove temporary group
                tempGroup.Active = false;
                _device?.Groups.Remove(tempGroup);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to perform batch read for {Count} tags", tagNamesList.Count);
            
            // Fallback: try individual reads for any missing tags
            foreach (var tagName in tagNamesList.Where(t => !results.ContainsKey(t)))
            {
                try
                {
                    var tagValue = await ReadTagAsync<object>(tagName, cancellationToken).ConfigureAwait(false);
                    results[tagName] = tagValue.Value;
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning(fallbackEx, "Failed fallback read for tag '{TagName}'", tagName);
                    results[tagName] = null;
                }
            }
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<string, T>> ReadTagsAsync<T>(
        IEnumerable<string> tagNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagNames);
        EnsureConnected();

        var tagNamesList = tagNames.ToList();
        if (tagNamesList.Count == 0)
        {
            return new Dictionary<string, T>();
        }


        var results = new Dictionary<string, T>();

        try
        {
            // Create temporary group for batch read
            var tempGroup = new ABLogix.Group(true, 100); // Active = true to enable reading
            _device?.Groups.Add(tempGroup);

            var tempItems = new Dictionary<string, ABLogix.Item>();

            try
            {
                // Add all tags as items to the group
                foreach (var tagName in tagNamesList)
                {
                    var item = new ABLogix.Item($"temp_{tagName}", tagName);
                    tempGroup.Items.Add(item);
                    tempItems[tagName] = item;
                }

                // Read all items asynchronously - ASComm will batch these reads efficiently when in the same group
                var readTasks = tempItems.Values.Select(item => item.ReadAsync()).ToList();
                await Task.WhenAll(readTasks).ConfigureAwait(false);

                // Collect values from all items with type conversion
                foreach (var tagName in tagNamesList)
                {
                    if (tempItems.TryGetValue(tagName, out var item))
                    {
                        var quality = MapQuality(item.Quality);
                        
                        T value = default!;

                        if (quality == TagQuality.Good && item.Values is not null && item.Values.Length > 0)
                        {
                            // Check if T is a structured type (UDT)
                            if (IsStructuredType<T>())
                            {
                                value = Activator.CreateInstance<T>();
                                item.GetStructuredValues(value);
                            }
                            else if (typeof(T).IsArray)
                            {
                                value = ConvertToArray<T>(item.Values);
                            }
                            else
                            {
                                var rawValue = item.Values.Length == 1 ? item.Values[0] : item.Values;
                                value = ConvertValue<T>(rawValue);
                            }
                        }

                        results[tagName] = value;
                        
                        if (quality != TagQuality.Good)
                        {
                            _logger.LogWarning(
                                "Tag '{TagName}' in batch read has quality: {Quality}",
                                tagName,
                                quality);
                        }
                    }
                }

                _logger.LogDebug(
                    "📖 Batch read completed: {SuccessCount}/{TotalCount} tags",
                    results.Count,
                    tagNamesList.Count);
            }
            finally
            {
                // Cleanup: deactivate and remove temporary group
                tempGroup.Active = false;
                _device?.Groups.Remove(tempGroup);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to perform batch read for {Count} tags of type {Type}", tagNamesList.Count, typeof(T).Name);
            
            // Fallback: try individual reads for any missing tags
            foreach (var tagName in tagNamesList.Where(t => !results.ContainsKey(t)))
            {
                try
                {
                    var tagValue = await ReadTagAsync<T>(tagName, cancellationToken).ConfigureAwait(false);
                    results[tagName] = tagValue.Value;
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning(fallbackEx, "Failed fallback read for tag '{TagName}'", tagName);
                    results[tagName] = default!;
                }
            }
        }

        return results;
    }

    public Task WriteTagAsync<T>(string tagName, T value, CancellationToken cancellationToken = default)
    {
        return WriteTagInternalAsync(tagName, value!, cancellationToken);
    }

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

    public Task<IAsyncDisposable> SubscribeAsync<T>(
        string tagName,
        Func<TagValue<T>, IEdgePlcDriverMessageContext, CancellationToken, Task> handler,
        int pollingIntervalMs = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagName);
        ArgumentNullException.ThrowIfNull(handler);

        var effectiveInterval = pollingIntervalMs > 0 ? pollingIntervalMs : _options.DefaultPollingIntervalMs;

        _logger.LogInformation(
            "📡 Subscribing to tag '{TagName}' with {Interval}ms polling interval",
            tagName,
            effectiveInterval);

        // Create item and add to polling group
        var item = GetOrCreateItem(tagName);

        // Wrap the typed handler
        async Task WrappedHandler(ABLogix.Item theItem, CancellationToken ct)
        {
            var quality = MapQuality(theItem.Quality);
            
            T value = default!;
            object? rawValue = null;

            if (quality == TagQuality.Good && theItem.Values is not null && theItem.Values.Length > 0)
            {
                // Check if T is a structured type (UDT)
                if (IsStructuredType<T>())
                {
                    // Create instance and populate using GetStructuredValues
                    value = Activator.CreateInstance<T>();
                    theItem.GetStructuredValues(value);
                    rawValue = value;
                }
                else if (typeof(T).IsArray)
                {
                    // Handle array types
                    value = ConvertToArray<T>(theItem.Values);
                    rawValue = theItem.Values;
                }
                else
                {
                    // Primitive types
                    rawValue = theItem.Values.Length == 1 ? theItem.Values[0] : theItem.Values;
                    value = ConvertValue<T>(rawValue);
                }
            }

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

            var rawPayload = rawValue is not null
                ? Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rawValue))
                : ReadOnlyMemory<byte>.Empty;

            var context = new EdgePlcDriverMessageContext(tagName, rawPayload, _publisher, this);

            await handler(tagValue, context, ct).ConfigureAwait(false);
        }

        var tagHandler = new TagHandler
        {
            TagName = tagName,
            MessageType = typeof(T),
            Handler = (Func<ABLogix.Item, CancellationToken, Task>)WrappedHandler,
            PollingIntervalMs = effectiveInterval
        };

        _dynamicHandlers[tagName] = tagHandler;

        // Activate polling group if not already active
        if (_pollingGroup is not null && !_pollingGroup.Active)
        {
            _pollingGroup.Active = true;
        }

        return Task.FromResult<IAsyncDisposable>(new TagSubscription(tagName, UnsubscribeAsync));
    }

    private async Task WriteTagInternalAsync(string tagName, object value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tagName);
        EnsureConnected();


        try
        {
            // Para escrituras, usar un Item temporal en el _writeGroup (Active = false)
            // Esto evita que el Item se agregue al polling group y cause polling automático
            if (_writeGroup is null || _device is null)
            {
                throw new InvalidOperationException("Write group not initialized");
            }

            // Crear Item temporal SOLO para escribir
            var item = new ABLogix.Item($"write_{tagName}", tagName);
            _writeGroup.Items.Add(item); // Agregar al write group (inactivo, no hace polling)

            try
            {

            // Check if it's a UDT (structured type with [StructLayout] attribute)
            if (value is not null && IsStructuredTypeRuntime(value.GetType()))
            {
                // UDT/Struct - write directly (NOT wrapped in array)
                await item.WriteAsync(value).ConfigureAwait(false);
                return;
            }

            // Handle array values - convert to object[] for ASComm
            if (value is Array array)
            {
                var valuesToWrite = new object[array.Length];
                array.CopyTo(valuesToWrite, 0);
                await item.WriteAsync(valuesToWrite).ConfigureAwait(false);
                return;
            }

            // Primitive values - wrap in object[]
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value), $"Cannot write null value to tag '{tagName}'");
            }
            
            // Handle string values - convert to LogixString for ASComm
            // ASComm requires STRING type to be written as LOGIX_STRING structure, not AUTO
            if (value is string stringValue)
            {
                var logixString = new DataTypes.LogixString(stringValue);
                // Write as structured type (not wrapped in array)
                await item.WriteAsync(logixString).ConfigureAwait(false);
                return;
            }
            
                await item.WriteAsync(new object[] { value }).ConfigureAwait(false);
            }
            finally
            {
                // Limpiar el Item temporal del write group después de escribir
                _writeGroup.Items.Remove(item);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to write to tag '{TagName}'", tagName);
            throw;
        }
    }

    private ABLogix.Item GetOrCreateItem(string tagName, Attributes.TagSubscriptionMode mode = Attributes.TagSubscriptionMode.Polling)
    {
        return _tagItems.GetOrAdd(tagName, name =>
        {
            var item = new ABLogix.Item($"item_{name}", name);
            item.Error += Item_Error;
            item.DataChanged += Item_DataChanged;

            // Add to appropriate group based on mode
            if (mode == Attributes.TagSubscriptionMode.Unsolicited)
            {
                _unsolicitedGroup?.Items.Add(item);
            }
            else
            {
                _pollingGroup?.Items.Add(item);
            }

            return item;
        });
    }

    private Task StartRegisteredHandlersAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "📡 Starting {Count} registered tag handler(s) for connection '{ConnectionName}'",
            _handlerRegistrations.Count,
            _options.ConnectionName);

        var hasPollingSubscriptions = false;
        var hasUnsolicitedSubscriptions = false;

        foreach (var registration in _handlerRegistrations)
        {
            // Create item for this subscription with appropriate mode
            var item = GetOrCreateItem(registration.TagName, registration.Mode);

            var tagHandler = new TagHandler
            {
                TagName = registration.TagName,
                MessageType = registration.MessageType,
                Handler = CreateAttributeHandlerDelegate(registration),
                PollingIntervalMs = registration.PollingIntervalMs > 0
                    ? registration.PollingIntervalMs
                    : _options.DefaultPollingIntervalMs,
                OnChangeOnly = registration.OnChangeOnly,
                Deadband = registration.Deadband,
                Mode = registration.Mode
            };

            _dynamicHandlers[registration.TagName] = tagHandler;

            if (registration.Mode == Attributes.TagSubscriptionMode.Unsolicited)
            {
                hasUnsolicitedSubscriptions = true;
            }
            else
            {
                hasPollingSubscriptions = true;
            }
            
            _logger.LogInformation(
                "📡 Subscribed to tag '{TagName}' | Mode: {Mode} | Interval: {Interval}ms | OnChangeOnly: {OnChange}",
                registration.TagName,
                registration.Mode,
                tagHandler.PollingIntervalMs,
                tagHandler.OnChangeOnly);
        }

        // Activate polling group if there are polling subscriptions
        if (hasPollingSubscriptions && _pollingGroup is not null)
        {
            _pollingGroup.UpdateRate = _options.DefaultPollingIntervalMs;
            _pollingGroup.Active = true;
            _logger.LogInformation("📡 Polling group activated with {Rate}ms update rate", _pollingGroup.UpdateRate);
        }

        // Activate unsolicited group if there are unsolicited subscriptions
        if (hasUnsolicitedSubscriptions && _unsolicitedGroup is not null)
        {
            _unsolicitedGroup.Active = true;
            _logger.LogInformation("📡 Unsolicited group activated (10ms update rate)");
        }

        // Note: No need for manual timers. When group.Active = true, ASComm IoT handles
        // automatic polling at the group's UpdateRate and fires DataChanged events.
        // For OnChangeOnly=false handlers, they'll receive all DataChanged events.
        // For OnChangeOnly=true handlers, the handler delegate filters unchanged values.

        return Task.CompletedTask;
    }



    private Delegate CreateAttributeHandlerDelegate(TagHandlerRegistration registration)
    {
        return async (ABLogix.Item theItem, CancellationToken ct) =>
        {
            try
            {
                var quality = MapQuality(theItem.Quality);

                // Get the inner type of TagValue<T>
                var innerType = registration.MessageType.GetGenericArguments().FirstOrDefault() ?? typeof(object);

                // Check if value changed (for OnChangeOnly mode)
                if (registration.OnChangeOnly)
                {
                    var currentValue = quality == TagQuality.Good && theItem.Values?.Length > 0
                        ? theItem.Values[0]
                        : null;

                    if (_dynamicHandlers.TryGetValue(registration.TagName, out var handler))
                    {
                        var lastValue = handler.LastValue;
                        if (Equals(currentValue, lastValue))
                        {
                            return; // No change, skip
                        }

                        // Check deadband for numeric types
                        if (registration.Deadband > 0 && IsNumeric(currentValue) && IsNumeric(lastValue))
                        {
                            var diff = Math.Abs(Convert.ToDouble(currentValue) - Convert.ToDouble(lastValue));
                            if (diff <= registration.Deadband)
                            {
                                return; // Within deadband, skip
                            }
                        }

                        handler.LastValue = currentValue;
                    }
                }

                using var scopedHandler = _handlerResolver.ResolveScoped(registration.HandlerType);
                var handlerInstance = scopedHandler.Handler;

                // Determine the value based on the type
                object? deserializedValue = null;

                if (quality == TagQuality.Good && theItem.Values?.Length > 0)
                {
                    // Check if it's a structured type (UDT)
                    if (IsStructuredTypeRuntime(innerType))
                    {
                        // Create instance and populate using GetStructuredValues
                        deserializedValue = Activator.CreateInstance(innerType);
                        theItem.GetStructuredValues(deserializedValue);
                    }
                    else if (innerType.IsArray)
                    {
                        // Handle array types
                        var elementType = innerType.GetElementType()!;
                        var array = Array.CreateInstance(elementType, theItem.Values.Length);
                        for (int i = 0; i < theItem.Values.Length; i++)
                        {
                            try
                            {
                                array.SetValue(Convert.ChangeType(theItem.Values[i], elementType), i);
                            }
                            catch
                            {
                                array.SetValue(theItem.Values[i], i);
                            }
                        }
                        deserializedValue = array;
                    }
                    else
                    {
                        // Primitive or simple types
                        var rawValue = theItem.Values.Length == 1 ? theItem.Values[0] : theItem.Values;
                        try
                        {
                            deserializedValue = innerType.IsPrimitive || innerType == typeof(decimal) || innerType == typeof(string)
                                ? Convert.ChangeType(rawValue, innerType)
                                : rawValue;
                        }
                        catch
                        {
                            deserializedValue = rawValue;
                        }
                    }
                }

                // Create TagValue<T> instance
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

                // Invoke HandleAsync
                var method = registration.HandlerType.GetMethod("HandleAsync");
                if (method is not null)
                {
                    var task = (Task?)method.Invoke(handlerInstance, [tagValue, context, ct]);
                    if (task is not null)
                    {
                        await task.ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error dispatching tag '{TagName}' to handler {HandlerType}",
                    registration.TagName,
                    registration.HandlerType.Name);
            }
        };
    }



    #region ASComm Event Handlers

    private void Channel_Error(object? sender, ChannelEventArgs e)
    {
        _logger.LogError("Channel error: {Message}", e.Message);

        if (_options.AutoReconnect && _state == ConnectionState.Connected)
        {
            SetState(ConnectionState.Faulted, new Exception(e.Message));
            StartReconnectTask();
        }
    }

    private void Device_Error(object? sender, DeviceEventArgs e)
    {
        _logger.LogError("Device error: {Message}", e.Message);
    }

    // Track error timestamps to avoid spam logging
    private readonly ConcurrentDictionary<string, DateTime> _lastErrorLogTime = new();
    private readonly ConcurrentDictionary<string, int> _errorCounts = new();
    private const int MaxErrorsBeforeSuppress = 5;
    private const int ErrorLogThrottleSeconds = 10; // Only log same error every 10 seconds

    private void Item_Error(object? sender, ItemEventArgs e)
    {
        if (sender is not ABLogix.Item theItem)
            return;

        var tagName = theItem.HWTagName;
        var errorMessage = e.Message;
        var errorKey = $"{tagName}:{errorMessage}";
        
        // Increment error count for this tag
        var errorCount = _errorCounts.AddOrUpdate(tagName, 1, (key, count) => count + 1);
        
        // Check if we should log this error (throttle repeated errors)
        var now = DateTime.UtcNow;
        var shouldLog = _lastErrorLogTime.AddOrUpdate(
            errorKey,
            now,
            (key, lastTime) =>
            {
                // If enough time has passed since last log, allow logging again
                if ((now - lastTime).TotalSeconds >= ErrorLogThrottleSeconds)
                {
                    return now;
                }
                return lastTime; // Keep old time to prevent logging
            });

        // Only log if enough time has passed
        if (shouldLog == now)
        {
            if (errorCount <= MaxErrorsBeforeSuppress)
            {
                _logger.LogWarning(
                    "Item error (tag: {TagName}, count: {Count}): {Message}",
                    tagName,
                    errorCount,
                    errorMessage);
            }
            else if (errorCount == MaxErrorsBeforeSuppress + 1)
            {
                // Log once that we're suppressing further errors
                _logger.LogWarning(
                    "Item error (tag: {TagName}): {Message} - Suppressing further error logs for this tag to reduce spam",
                    tagName,
                    errorMessage);
            }
            
            // If too many errors, try to remove item from polling to stop the spam
            if (errorCount > MaxErrorsBeforeSuppress * 2)
            {
                try
                {
                    // Remove from polling group to stop repeated errors
                    if (_pollingGroup?.Items.Contains(theItem) == true)
                    {
                        _pollingGroup.Items.Remove(theItem);
                        _logger.LogInformation(
                            "Removed tag '{TagName}' from polling group due to persistent errors",
                            tagName);
                    }
                    if (_unsolicitedGroup?.Items.Contains(theItem) == true)
                    {
                        _unsolicitedGroup.Items.Remove(theItem);
                        _logger.LogInformation(
                            "Removed tag '{TagName}' from unsolicited group due to persistent errors",
                            tagName);
                    }
                    
                    // Remove from cache so it can be recreated if needed
                    _tagItems.TryRemove(tagName, out _);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error removing failed tag '{TagName}' from polling", tagName);
                }
            }
        }
    }

    private void Item_DataChanged(object? sender, ItemDataChangedEventArgs e)
    {
        if (sender is not ABLogix.Item theItem)
            return;

        var tagName = theItem.HWTagName;

        if (_dynamicHandlers.TryGetValue(tagName, out var handler))
        {
            try
            {
                if (handler.Handler is Func<ABLogix.Item, CancellationToken, Task> asyncHandler)
                {
                    // Fire and forget with error handling
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await asyncHandler(theItem, _disposeCts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in DataChanged handler for tag '{TagName}'", tagName);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching DataChanged for tag '{TagName}'", tagName);
            }
        }
    }

    #endregion

    #region Reconnection Logic

    private void StartReconnectTask()
    {
        if (_reconnectTask is not null && !_reconnectTask.IsCompleted)
            return;

        _reconnectTask = Task.Run(async () =>
        {
            var delay = 1000; // Start with 1 second
            var maxDelay = _options.MaxReconnectDelaySeconds * 1000;

            while (!_disposeCts.Token.IsCancellationRequested && _state != ConnectionState.Connected)
            {
                _logger.LogInformation("🔄 Attempting reconnection in {Delay}ms...", delay);
                await Task.Delay(delay, _disposeCts.Token).ConfigureAwait(false);

                try
                {
                    CleanupAsCommObjects();
                    await ConnectAsync(_disposeCts.Token).ConfigureAwait(false);
                    _logger.LogInformation("✅ Reconnection successful");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Reconnection attempt failed");
                    delay = Math.Min(delay * 2, maxDelay); // Exponential backoff
                }
            }
        });
    }

    #endregion

    #region Helper Methods

    private Task UnsubscribeAsync(string tagName)
    {
        if (_dynamicHandlers.TryRemove(tagName, out _))
        {
            // Remove item from polling group
            if (_tagItems.TryRemove(tagName, out var item))
            {
                item.Error -= Item_Error;
                item.DataChanged -= Item_DataChanged;
                _pollingGroup?.Items.Remove(item);
            }

            _logger.LogInformation("📡 Unsubscribed from tag '{TagName}'", tagName);
        }
        return Task.CompletedTask;
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                $"Not connected to PLC. Current state: {_state}. Call ConnectAsync first.");
        }
    }

    private void SetState(ConnectionState newState, Exception? exception = null)
    {
        var previousState = _state;
        _state = newState;

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(previousState, newState, exception));
    }

    private static TagQuality MapQuality(Quality ascommQuality)
    {
        return ascommQuality switch
        {
            Quality.GOOD => TagQuality.Good,
            Quality.UNCERTAIN => TagQuality.Uncertain,
            Quality.BAD => TagQuality.Bad,
            Quality.INACTIVE => TagQuality.CommError, // Group or Item is inactive
            _ => TagQuality.Bad
        };
    }

    private static T ConvertValue<T>(object? value)
    {
        if (value is null)
            return default!;

        if (value is T typedValue)
            return typedValue;

        try
        {
            // Handle numeric conversions
            if (typeof(T).IsPrimitive || typeof(T) == typeof(decimal))
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }

            // For complex types, try direct cast
            return (T)value;
        }
        catch
        {
            return default!;
        }
    }

    private static T ConvertToArray<T>(object[] values)
    {
        var elementType = typeof(T).GetElementType();
        if (elementType is null)
            return default!;

        var array = Array.CreateInstance(elementType, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            try
            {
                var convertedValue = Convert.ChangeType(values[i], elementType);
                array.SetValue(convertedValue, i);
            }
            catch
            {
                array.SetValue(values[i], i);
            }
        }

        return (T)(object)array;
    }

    private static bool IsStructuredType<T>()
    {
        return IsStructuredTypeRuntime(typeof(T));
    }

    private static bool IsStructuredTypeRuntime(Type? type)
    {
        if (type is null)
            return false;

        if (type.IsPrimitive || type.IsArray)
            return false;

        if (type == typeof(string) || 
            type == typeof(decimal) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan) ||
            type == typeof(Guid))
            return false;

        var hasStructLayout = type.GetCustomAttributes(
            typeof(System.Runtime.InteropServices.StructLayoutAttribute), true).Length > 0;

        if (hasStructLayout)
            return true;

        if (type.IsClass)
        {
            var publicFields = type.GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return publicFields.Length > 0;
        }

        return false;
    }

    private static bool IsNumeric(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _disposeCts.Cancel();
        CleanupAsCommObjects();
        _connectionLock.Dispose();
        _disposeCts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _disposeCts.Cancel();

        try
        {
            if (IsConnected)
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            CleanupAsCommObjects();
            _connectionLock.Dispose();
            _disposeCts.Dispose();
        }
    }

    #endregion
}
