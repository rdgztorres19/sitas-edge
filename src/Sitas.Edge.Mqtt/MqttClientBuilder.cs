using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Activators;
using Sitas.Edge.Core.Discovery;
using Sitas.Edge.Core.Internal;
using Sitas.Edge.Core.Serialization;
using Sitas.Edge.Mqtt.Configuration;

namespace Sitas.Edge.Mqtt;

/// <summary>
/// Builder for configuring and creating MQTT connections.
/// </summary>
public sealed class MqttClientBuilder : IMqttClientBuilder
{
    internal readonly MqttConnectionOptions _options = new();
    private readonly List<HandlerRegistration> _handlerRegistrations = [];
    private readonly HandlerDiscoveryService _discoveryService = new();
    
    private IMessageSerializer _serializer = JsonMessageSerializer.Default;
    private IHandlerResolver _handlerResolver = ActivatorHandlerResolver.Instance;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    /// <summary>
    /// Creates a new MQTT client builder.
    /// </summary>
    public static IMqttClientBuilder Create() => new MqttClientBuilder();

    private MqttClientBuilder()
    {
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithConnectionName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _options.ConnectionName = name;
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithBroker(string host, int port = 1883)
    {
        ArgumentNullException.ThrowIfNull(host);
        _options.Host = host;
        _options.Port = port;
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithCredentials(string username, string password)
    {
        _options.Username = username;
        _options.Password = password;
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithTls(bool enabled = true, bool validateCertificate = true)
    {
        _options.UseTls = enabled;
        _options.ValidateCertificate = validateCertificate;
        
        // Auto-adjust port for TLS if using default port
        if (enabled && _options.Port == 1883)
        {
            _options.Port = 8883;
        }
        
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithClientId(string clientId)
    {
        _options.ClientId = clientId;
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithCleanSession(bool cleanSession = true)
    {
        _options.CleanSession = cleanSession;
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithKeepAlive(int seconds)
    {
        _options.KeepAliveSeconds = seconds;
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithAutoReconnect(bool enabled = true, int maxDelaySeconds = 30)
    {
        _options.AutoReconnect = enabled;
        _options.MaxReconnectDelaySeconds = maxDelaySeconds;
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithSerializer(IMessageSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        return this;
    }

    /// <inheritdoc />
    [Obsolete("Use WithHandlerActivator instead for better DI container integration.")]
    public IMqttClientBuilder WithHandlerResolver(IHandlerResolver resolver)
    {
        _handlerResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithHandlerActivator(IHandlerActivator activator)
    {
        ArgumentNullException.ThrowIfNull(activator);
        _handlerResolver = new HandlerActivatorAdapter(activator);
        return this;
    }

    /// <summary>
    /// Configures the logger factory for logging.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <returns>The builder for chaining.</returns>
    public IMqttClientBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithHandlersFromAssemblies(params Assembly[] assemblies)
    {
        var registrations = _discoveryService.DiscoverHandlers(assemblies);
        
        var logger = _loggerFactory?.CreateLogger<MqttClientBuilder>();
        
        // Filter registrations for this connection
        var relevantRegistrations = registrations
            .Where(r => r.ConnectionName.Equals(_options.ConnectionName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        logger?.LogInformation("📋 Discovered {RelevantCount} handler(s) for connection '{ConnectionName}'", 
            relevantRegistrations.Count, _options.ConnectionName);
        
        _handlerRegistrations.AddRange(relevantRegistrations);
        return this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithHandlersFromEntryAssembly()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        return entryAssembly is not null 
            ? WithHandlersFromAssemblies(entryAssembly) 
            : this;
    }

    /// <inheritdoc />
    public IMqttClientBuilder WithOptions(MqttConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options.ConnectionName = options.ConnectionName;
        _options.Host = options.Host;
        _options.Port = options.Port;
        _options.ClientId = options.ClientId;
        _options.Username = options.Username;
        _options.Password = options.Password;
        _options.UseTls = options.UseTls;
        _options.ValidateCertificate = options.ValidateCertificate;
        _options.CleanSession = options.CleanSession;
        _options.KeepAliveSeconds = options.KeepAliveSeconds;
        _options.ConnectionTimeoutSeconds = options.ConnectionTimeoutSeconds;
        _options.AutoReconnect = options.AutoReconnect;
        _options.MaxReconnectDelaySeconds = options.MaxReconnectDelaySeconds;
        _options.ProtocolVersion = options.ProtocolVersion;

        return this;
    }

    /// <summary>
    /// Builds the MQTT connection with the configured options.
    /// </summary>
    /// <returns>A configured but not yet connected MQTT connection.</returns>
    public IMqttConnection Build()
    {
        // Generate client ID if not specified
        if (string.IsNullOrEmpty(_options.ClientId))
        {
            _options.ClientId = $"nexus-{_options.ConnectionName}-{Guid.NewGuid():N}";
        }

        return new MqttConnection(
            _options,
            _handlerRegistrations,
            _serializer,
            _handlerResolver,
            _loggerFactory.CreateLogger<MqttConnection>());
    }

    IMqttConnection IServiceBusBuilder<IMqttClientBuilder, IMqttConnection>.Build() => Build();
}

