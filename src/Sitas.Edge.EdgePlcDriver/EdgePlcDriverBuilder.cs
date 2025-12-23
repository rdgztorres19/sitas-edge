using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sitas.Edge.EdgePlcDriver.Attributes;
using Sitas.Edge.EdgePlcDriver.Configuration;
using Sitas.Edge.EdgePlcDriver.Internal;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Activators;
using Sitas.Edge.Core.Internal;
using Sitas.Edge.Core.Serialization;

namespace Sitas.Edge.EdgePlcDriver;

/// <summary>
/// Builder for configuring and creating Edge PLC Driver connections.
/// </summary>
public sealed class EdgePlcDriverBuilder : IEdgePlcDriverBuilder
{
    private readonly EdgePlcDriverOptions _options = new();
    private readonly List<TagHandlerRegistration> _handlerRegistrations = [];

    private IMessageSerializer _serializer = JsonMessageSerializer.Default;
    private IHandlerResolver _handlerResolver = ActivatorHandlerResolver.Instance;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    /// <summary>
    /// Creates a new Edge PLC Driver builder.
    /// </summary>
    public static IEdgePlcDriverBuilder Create() => new EdgePlcDriverBuilder();

    private EdgePlcDriverBuilder()
    {
    }

    /// <inheritdoc />
    public IEdgePlcDriverBuilder WithConnectionName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _options.ConnectionName = name;
        return this;
    }

    /// <inheritdoc />
    public IEdgePlcDriverBuilder WithPlc(string ipAddress, int cpuSlot = 0, int backplane = 1)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);
        _options.IpAddress = ipAddress;
        _options.CpuSlot = cpuSlot;
        _options.Backplane = backplane;
        return this;
    }

    /// <inheritdoc />
    public IEdgePlcDriverBuilder WithDefaultPollingInterval(int intervalMs)
    {
        if (intervalMs < 1)
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "Polling interval must be at least 1ms");

        _options.DefaultPollingIntervalMs = intervalMs;
        return this;
    }

    /// <inheritdoc />
    public IEdgePlcDriverBuilder WithConnectionTimeout(int timeoutSeconds)
    {
        _options.ConnectionTimeoutSeconds = timeoutSeconds;
        return this;
    }

    /// <inheritdoc />
    public IEdgePlcDriverBuilder WithAutoReconnect(bool enabled = true, int maxDelaySeconds = 30)
    {
        _options.AutoReconnect = enabled;
        _options.MaxReconnectDelaySeconds = maxDelaySeconds;
        return this;
    }

    /// <inheritdoc />
    public IEdgePlcDriverBuilder WithSerializer(IMessageSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        return this;
    }

    /// <inheritdoc />
    public IEdgePlcDriverBuilder WithHandlerActivator(IHandlerActivator activator)
    {
        ArgumentNullException.ThrowIfNull(activator);
        _handlerResolver = new HandlerActivatorAdapter(activator);
        return this;
    }

    /// <summary>
    /// Configures the logger factory for logging.
    /// </summary>
    public IEdgePlcDriverBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        return this;
    }

    /// <inheritdoc />
    public IEdgePlcDriverBuilder WithHandlersFromAssemblies(params Assembly[] assemblies)
    {
        var registrations = DiscoverHandlers(assemblies);

        // Filter registrations for this connection
        var relevantRegistrations = registrations
            .Where(r => r.ConnectionName.Equals(_options.ConnectionName, StringComparison.OrdinalIgnoreCase))
            .Select(r => new TagHandlerRegistration
            {
                TagName = r.TagName,
                HandlerType = r.HandlerType,
                MessageType = r.MessageType,
                PollingIntervalMs = r.PollingIntervalMs,
                OnChangeOnly = r.OnChangeOnly,
                Deadband = r.Deadband,
                Mode = r.Mode
            });

        _handlerRegistrations.AddRange(relevantRegistrations);
        return this;
    }

    /// <inheritdoc />
    public IEdgePlcDriverBuilder WithHandlersFromEntryAssembly()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        return entryAssembly is not null
            ? WithHandlersFromAssemblies(entryAssembly)
            : this;
    }

    /// <inheritdoc />
    public IEdgePlcDriverBuilder WithOptions(EdgePlcDriverOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options.ConnectionName = options.ConnectionName;
        _options.IpAddress = options.IpAddress;
        _options.CpuSlot = options.CpuSlot;
        _options.Backplane = options.Backplane;
        _options.DefaultPollingIntervalMs = options.DefaultPollingIntervalMs;
        _options.ConnectionTimeoutSeconds = options.ConnectionTimeoutSeconds;
        _options.AutoReconnect = options.AutoReconnect;
        _options.MaxReconnectDelaySeconds = options.MaxReconnectDelaySeconds;

        return this;
    }

    /// <summary>
    /// Builds the Edge PLC Driver with the configured options.
    /// </summary>
    public IEdgePlcDriver Build()
    {
        if (string.IsNullOrEmpty(_options.IpAddress))
        {
            throw new InvalidOperationException(
                "PLC IP address must be configured. Call WithPlc() before Build().");
        }

        return new EdgePlcDriver(
            _options,
            _handlerRegistrations,
            _serializer,
            _handlerResolver,
            _loggerFactory.CreateLogger<EdgePlcDriver>());
    }

    IEdgePlcDriver IServiceBusBuilder<IEdgePlcDriverBuilder, IEdgePlcDriver>.Build() => Build();

    private static IEnumerable<EdgePlcDriverHandlerInfo> DiscoverHandlers(Assembly[] assemblies)
    {
        var handlerInterface = typeof(IMessageSubscriptionHandler<>);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                // 🚫 Skip handlers marked with [DisableHandler]
                if (type.IsDefined(typeof(Core.Attributes.DisableHandlerAttribute), inherit: false))
                    continue;

                var attributes = type.GetCustomAttributes<EdgePlcDriverSubscribeAttribute>();

                foreach (var attr in attributes)
                {
                    // Find the IMessageHandler<T> interface to get the message type
                    var messageType = type.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterface)
                        .Select(i => i.GetGenericArguments()[0])
                        .FirstOrDefault();

                    if (messageType is null)
                        continue;

                    yield return new EdgePlcDriverHandlerInfo
                    {
                        ConnectionName = attr.ConnectionName,
                        TagName = attr.Topic, // Topic is the tag name for Edge PLC Driver
                        HandlerType = type,
                        MessageType = messageType,
                        PollingIntervalMs = attr.PollingIntervalMs,
                        OnChangeOnly = attr.OnChangeOnly,
                        Deadband = attr.Deadband,
                        Mode = attr.Mode
                    };
                }
            }
        }
    }

    private sealed class EdgePlcDriverHandlerInfo
    {
        public required string ConnectionName { get; init; }
        public required string TagName { get; init; }
        public required Type HandlerType { get; init; }
        public required Type MessageType { get; init; }
        public int PollingIntervalMs { get; init; }
        public bool OnChangeOnly { get; init; }
        public double Deadband { get; init; }
        public Attributes.TagSubscriptionMode Mode { get; init; }
    }
}
