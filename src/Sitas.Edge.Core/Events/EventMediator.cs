using System.Collections.Concurrent;
using System.Reflection;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Events.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sitas.Edge.Core.Events;

/// <summary>
/// Default implementation of <see cref="IEventMediator"/>.
/// Discovers and invokes event handlers based on [Event] attributes.
/// </summary>
public class EventMediator : IEventMediator
{
    private static readonly object GlobalLock = new();
    private static EventMediator? _global;

    /// <summary>
    /// Gets the global EventMediator instance (initialized automatically when SitasEdgeBuilder.Build() is called).
    /// </summary>
    public static EventMediator Global
    {
        get
        {
            var instance = _global;
            if (instance is null)
            {
                throw new InvalidOperationException(
                    "EventMediator is not initialized. Build a Sitas.Edge instance first (SitasEdgeBuilder.Create().Build()).");
            }
            return instance;
        }
    }

    internal static void SetGlobal(EventMediator mediator)
    {
        ArgumentNullException.ThrowIfNull(mediator);

        lock (GlobalLock)
        {
            _global = mediator;
        }
    }

    /// <summary>
    /// Static convenience: emits an event using the global mediator.
    /// </summary>
    public static Task Emit(string eventName, CancellationToken cancellationToken = default)
        => Global.EmitAsync(eventName, cancellationToken);

    /// <summary>
    /// Static convenience: emits an event using the global mediator.
    /// </summary>
    public static Task Emit<TEvent>(string eventName, TEvent eventData, CancellationToken cancellationToken = default)
        => Global.EmitAsync(eventName, eventData, cancellationToken);

    /// <summary>
    /// Static convenience: emits an event and returns a result using the global mediator.
    /// </summary>
    public static Task<TResult?> Emit<TEvent, TResult>(
        string eventName,
        TEvent eventData,
        CancellationToken cancellationToken = default)
        => Global.EmitAsync<TEvent, TResult>(eventName, eventData, cancellationToken);

    private readonly ISitasEdge _conduit;
    private readonly ILogger<EventMediator> _logger;
    private readonly IHandlerActivator? _handlerActivator;
    private readonly ConcurrentDictionary<string, List<EventHandlerRegistration>> _handlers = new();
    private bool _isInitialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventMediator"/> class.
    /// This uses the Conduit activator for handler creation (DI-friendly) and does not require
    /// registering EventMediator in any DI container.
    /// </summary>
    public EventMediator(
        ISitasEdge conduit)
    {
        _conduit = conduit ?? throw new ArgumentNullException(nameof(conduit));
        _handlerActivator = conduit.Activator;

        // Use NullLogger by default - ILogger<> should not be created as a handler via CreateInstance
        // If logging is needed, it should be provided via DI container or passed as constructor parameter
        _logger = NullLogger<EventMediator>.Instance;
    }

    /// <summary>
    /// Registers handlers from the specified assemblies.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for handlers.</param>
    public void RegisterHandlersFromAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            RegisterHandlersFromAssembly(assembly);
        }
        _isInitialized = true;
    }

    /// <summary>
    /// Registers handlers from a single assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    public void RegisterHandlersFromAssembly(Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<EventAttribute>() is not null)
            .ToList();

        foreach (var handlerType in handlerTypes)
        {
            RegisterHandler(handlerType);
        }
    }

    /// <summary>
    /// Registers a single handler type.
    /// </summary>
    /// <param name="handlerType">The type of the handler to register.</param>
    public void RegisterHandler(Type handlerType)
    {
        var eventAttr = handlerType.GetCustomAttribute<EventAttribute>();
        if (eventAttr is null)
        {
            throw new ArgumentException($"Type {handlerType.Name} does not have [Event] attribute.", nameof(handlerType));
        }

        // Get all tag read attributes
        var tagReadAttrs = handlerType.GetCustomAttributes<TagReadAttribute>().ToList();

        // Determine the event data type and result type from the interface
        var eventHandlerInterface = handlerType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(IEventHandler<>) ||
                 i.GetGenericTypeDefinition() == typeof(IEventHandler<,>)));

        if (eventHandlerInterface is null)
        {
            throw new ArgumentException(
                $"Type {handlerType.Name} must implement IEventHandler<TEvent> or IEventHandler<TEvent, TResult>.",
                nameof(handlerType));
        }

        var genericArgs = eventHandlerInterface.GetGenericArguments();
        var eventDataType = genericArgs[0];
        var resultType = genericArgs.Length > 1 ? genericArgs[1] : null;

        var registration = new EventHandlerRegistration
        {
            EventName = eventAttr.EventName,
            HandlerType = handlerType,
            EventDataType = eventDataType,
            ResultType = resultType,
            Priority = eventAttr.Priority,
            FireAndForget = eventAttr.FireAndForget,
            TagReadAttributes = tagReadAttrs
        };

        var handlers = _handlers.GetOrAdd(eventAttr.EventName, _ => new List<EventHandlerRegistration>());
        lock (handlers)
        {
            handlers.Add(registration);
            handlers.Sort((a, b) => b.Priority.CompareTo(a.Priority)); // Higher priority first
        }

        // Removed debug log to reduce noise during startup
    }

    /// <inheritdoc/>
    public Task EmitAsync(string eventName, CancellationToken cancellationToken = default)
    {
        return EmitInternalAsync<object?, object?>(eventName, null, cancellationToken);
    }

    /// <inheritdoc/>
    public Task EmitAsync<TEvent>(string eventName, TEvent eventData, CancellationToken cancellationToken = default)
    {
        return EmitInternalAsync<TEvent, object?>(eventName, eventData, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TResult?> EmitAsync<TEvent, TResult>(
        string eventName,
        TEvent eventData,
        CancellationToken cancellationToken = default)
    {
        var results = await EmitToAllAsync<TEvent, TResult>(eventName, eventData, cancellationToken);
        return results.FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TResult>> EmitToAllAsync<TEvent, TResult>(
        string eventName,
        TEvent eventData,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        if (!_handlers.TryGetValue(eventName, out var handlers) || handlers.Count == 0)
        {
            _logger.LogWarning("No handlers registered for event '{EventName}'", eventName);
            return Array.Empty<TResult>();
        }

        var results = new List<TResult>();
        var fireAndForgetTasks = new List<Task>();

        foreach (var registration in handlers)
        {
            try
            {
                // Read tags based on attributes
                var tagValues = await ReadTagsForHandlerAsync(registration, cancellationToken);

                // Invoke handler
                if (registration.FireAndForget)
                {
                    var task = InvokeHandlerResolvedAsync(registration, eventData, tagValues, cancellationToken);
                    fireAndForgetTasks.Add(task);
                }
                else
                {
                    var result = await InvokeHandlerResolvedAsync(registration, eventData, tagValues, cancellationToken);
                    if (result is TResult typedResult)
                    {
                        results.Add(typedResult);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking handler {HandlerType} for event '{EventName}'",
                    registration.HandlerType.Name, eventName);
            }
        }

        // Fire and forget tasks - don't await but log errors
        if (fireAndForgetTasks.Count > 0)
        {
            _ = Task.WhenAll(fireAndForgetTasks).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.LogError(t.Exception, "Fire-and-forget handler(s) failed for event '{EventName}'", eventName);
                }
            }, TaskScheduler.Default);
        }

        return results;
    }

    private async Task EmitInternalAsync<TEvent, TResult>(
        string eventName,
        TEvent? eventData,
        CancellationToken cancellationToken)
    {
        EnsureInitialized();

        if (!_handlers.TryGetValue(eventName, out var handlers) || handlers.Count == 0)
        {
            _logger.LogWarning("No handlers registered for event '{EventName}'", eventName);
            return;
        }

        _logger.LogDebug("Emitting event '{EventName}' to {Count} handler(s)", eventName, handlers.Count);

        foreach (var registration in handlers)
        {
            try
            {
                // Read tags based on attributes
                var tagValues = await ReadTagsForHandlerAsync(registration, cancellationToken);

                // Invoke handler
                if (registration.FireAndForget)
                {
                    _ = InvokeHandlerResolvedAsync(registration, eventData, tagValues, cancellationToken)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                _logger.LogError(t.Exception, "Fire-and-forget handler failed: {HandlerType}", registration.HandlerType.Name);
                            }
                        }, TaskScheduler.Default);
                }
                else
                {
                    await InvokeHandlerResolvedAsync(registration, eventData, tagValues, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking handler {HandlerType} for event '{EventName}'",
                    registration.HandlerType.Name, eventName);
            }
        }
    }

    private async Task<object?> InvokeHandlerResolvedAsync(
        EventHandlerRegistration registration,
        object? eventData,
        TagReadResults tagValues,
        CancellationToken cancellationToken)
    {
        if (_handlerActivator is not null)
        {
            using var scoped = _handlerActivator.CreateScopedInstance(registration.HandlerType);
            return await InvokeHandlerAsync(scoped.Handler, registration, eventData, tagValues, cancellationToken)
                .ConfigureAwait(false);
        }

        var handler = Activator.CreateInstance(registration.HandlerType);
        if (handler is null)
        {
            _logger.LogError("Failed to create handler instance for {HandlerType}", registration.HandlerType.Name);
            return null;
        }

        return await InvokeHandlerAsync(handler, registration, eventData, tagValues, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TagReadResults> ReadTagsForHandlerAsync(
        EventHandlerRegistration registration,
        CancellationToken cancellationToken)
    {
        var results = new TagReadResults();

        if (registration.TagReadAttributes.Count == 0)
        {
            return results;
        }

        foreach (var attr in registration.TagReadAttributes)
        {
            try
            {
                var connection = FindConnection(attr.ConnectionName);
                if (connection is null)
                {
                    _logger.LogWarning("No connection found for '{ConnectionName}'", attr.ConnectionName);
                    if (!attr.ContinueOnFailure)
                    {
                        throw new InvalidOperationException($"Connection '{attr.ConnectionName}' not found.");
                    }
                    continue;
                }

                var value = await ReadTagOnceAsync(connection, attr, cancellationToken).ConfigureAwait(false);
                results.Add(attr.ResultKey, value);

                _logger.LogDebug("Read tag '{TagName}' = {Value} (Quality: {Quality})",
                    attr.TagName, value.Value, value.Quality);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read tag '{TagName}' for handler {HandlerType}",
                    attr.TagName, registration.HandlerType.Name);

                if (!attr.ContinueOnFailure)
                {
                    throw;
                }

                // Add with bad quality
                results.Add(attr.ResultKey, new TagReadValue<object?>
                {
                    TagName = attr.TagName,
                    Value = null,
                    Quality = TagQuality.CommError,
                    Timestamp = DateTimeOffset.UtcNow
                });
            }
        }

        return results;
    }

    private IServiceBusConnection? FindConnection(string connectionName)
    {
        return _conduit.Connections
            .OfType<IServiceBusConnection>()
            .FirstOrDefault(c => c.ConnectionName.Equals(connectionName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<TagReadValue<object?>> ReadTagOnceAsync(
        IServiceBusConnection connection,
        TagReadAttribute attr,
        CancellationToken cancellationToken)
    {
        try
        {
            var method = connection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                    m.Name == "ReadTagAsync" &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 2 &&
                    m.GetParameters()[0].ParameterType == typeof(string) &&
                    m.GetParameters()[1].ParameterType == typeof(CancellationToken));

            if (method is null)
            {
                return new TagReadValue<object?>
                {
                    TagName = attr.TagName,
                    Value = null,
                    Quality = TagQuality.Bad,
                    Timestamp = DateTimeOffset.UtcNow
                };
            }

            var effectiveType = attr.ValueType ?? typeof(object);
            var generic = method.MakeGenericMethod(effectiveType);

            var taskObj = generic.Invoke(connection, new object[] { attr.TagName, cancellationToken });
            if (taskObj is not Task task)
            {
                return new TagReadValue<object?>
                {
                    TagName = attr.TagName,
                    Value = null,
                    Quality = TagQuality.CommError,
                    Timestamp = DateTimeOffset.UtcNow
                };
            }

            await task.ConfigureAwait(false);

            var result = taskObj.GetType().GetProperty("Result")?.GetValue(taskObj);
            if (result is null)
            {
                return new TagReadValue<object?>
                {
                    TagName = attr.TagName,
                    Value = null,
                    Quality = TagQuality.CommError,
                    Timestamp = DateTimeOffset.UtcNow
                };
            }

            var value = result.GetType().GetProperty("Value")?.GetValue(result);
            var qualityObj = result.GetType().GetProperty("Quality")?.GetValue(result);
            var timestampObj = result.GetType().GetProperty("Timestamp")?.GetValue(result);

            var quality = MapQuality(qualityObj);
            var timestamp = timestampObj is DateTimeOffset dto ? dto : DateTimeOffset.UtcNow;

            return new TagReadValue<object?>
            {
                TagName = attr.TagName,
                Value = value,
                Quality = quality,
                Timestamp = timestamp
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read tag '{TagName}' on connection '{ConnectionName}'",
                attr.TagName, connection.ConnectionName);

            return new TagReadValue<object?>
            {
                TagName = attr.TagName,
                Value = null,
                Quality = TagQuality.CommError,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
    }

    private static TagQuality MapQuality(object? qualityObj)
    {
        if (qualityObj is null) return TagQuality.Bad;

        // Best-effort mapping across different protocol enums (e.g., Conduit.AsComm.Messages.TagQuality)
        if (qualityObj.GetType().IsEnum)
        {
            var name = qualityObj.ToString();
            return name switch
            {
                "Good" => TagQuality.Good,
                "Uncertain" => TagQuality.Uncertain,
                "Bad" => TagQuality.Bad,
                "CommError" => TagQuality.CommError,
                _ => TagQuality.Bad
            };
        }

        return TagQuality.Bad;
    }

    private async Task<object?> InvokeHandlerAsync(
        object handler,
        EventHandlerRegistration registration,
        object? eventData,
        TagReadResults tagValues,
        CancellationToken cancellationToken)
    {
        var method = registration.HandlerType.GetMethod("HandleAsync");
        if (method is null)
        {
            throw new InvalidOperationException($"Handler {registration.HandlerType.Name} does not have HandleAsync method.");
        }

        var task = (Task?)method.Invoke(handler, new[] { eventData, tagValues, cancellationToken });
        if (task is null)
        {
            return null;
        }

        await task;

        // If the task has a result, extract it
        if (registration.ResultType is not null)
        {
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task);
        }

        return null;
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            // Auto-initialize from entry assembly
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly is not null)
            {
                RegisterHandlersFromAssembly(entryAssembly);
            }
            _isInitialized = true;
        }
    }
}

/// <summary>
/// Registration information for an event handler.
/// </summary>
internal class EventHandlerRegistration
{
    public required string EventName { get; init; }
    public required Type HandlerType { get; init; }
    public required Type EventDataType { get; init; }
    public Type? ResultType { get; init; }
    public int Priority { get; init; }
    public bool FireAndForget { get; init; }
    public List<TagReadAttribute> TagReadAttributes { get; init; } = new();
}
