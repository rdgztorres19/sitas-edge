using System.Reflection;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Mqtt.Configuration;

namespace Sitas.Edge.Mqtt;

/// <summary>
/// Builder interface for configuring MQTT connections.
/// Follows the fluent builder pattern for intuitive configuration.
/// </summary>
public interface IMqttClientBuilder : IServiceBusBuilder<IMqttClientBuilder, IMqttConnection>
{
    /// <summary>
    /// Sets the connection name for identification when using multiple connections.
    /// </summary>
    /// <param name="name">The connection name.</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithConnectionName(string name);

    /// <summary>
    /// Configures the MQTT broker endpoint.
    /// </summary>
    /// <param name="host">The broker host address.</param>
    /// <param name="port">The broker port (default: 1883).</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithBroker(string host, int port = 1883);

    /// <summary>
    /// Configures authentication credentials.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithCredentials(string username, string password);

    /// <summary>
    /// Enables or disables TLS/SSL encryption.
    /// </summary>
    /// <param name="enabled">Whether to enable TLS.</param>
    /// <param name="validateCertificate">Whether to validate the server certificate.</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithTls(bool enabled = true, bool validateCertificate = true);

    /// <summary>
    /// Sets the MQTT client identifier.
    /// </summary>
    /// <param name="clientId">The client ID.</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithClientId(string clientId);

    /// <summary>
    /// Configures the clean session behavior.
    /// </summary>
    /// <param name="cleanSession">Whether to use clean sessions.</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithCleanSession(bool cleanSession = true);

    /// <summary>
    /// Configures the keep-alive interval.
    /// </summary>
    /// <param name="seconds">The keep-alive interval in seconds.</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithKeepAlive(int seconds);

    /// <summary>
    /// Enables automatic reconnection with exponential backoff.
    /// </summary>
    /// <param name="enabled">Whether to enable auto-reconnect.</param>
    /// <param name="maxDelaySeconds">Maximum delay between reconnection attempts.</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithAutoReconnect(bool enabled = true, int maxDelaySeconds = 30);

    /// <summary>
    /// Configures a custom message serializer.
    /// </summary>
    /// <param name="serializer">The serializer to use.</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithSerializer(IMessageSerializer serializer);

    /// <summary>
    /// Configures a handler resolver for dependency injection.
    /// </summary>
    /// <param name="resolver">The handler resolver.</param>
    /// <returns>The builder for chaining.</returns>
    [Obsolete("Use WithHandlerActivator instead for better DI container integration.")]
    IMqttClientBuilder WithHandlerResolver(IHandlerResolver resolver);

    /// <summary>
    /// Configures a handler activator for dependency injection.
    /// This is the preferred method for integrating with any DI container.
    /// </summary>
    /// <param name="activator">The handler activator.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// // With IServiceProvider (works with any container)
    /// builder.WithHandlerActivator(new ServiceProviderActivator(serviceProvider));
    /// 
    /// // With custom factory function
    /// builder.WithHandlerActivator(new FuncActivator(type => container.Resolve(type)));
    /// </example>
    IMqttClientBuilder WithHandlerActivator(IHandlerActivator activator);

    /// <summary>
    /// Discovers and registers message handlers from the specified assemblies.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for handlers.</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithHandlersFromAssemblies(params Assembly[] assemblies);

    /// <summary>
    /// Discovers and registers message handlers from the entry assembly.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithHandlersFromEntryAssembly();

    /// <summary>
    /// Configures the MQTT connection using a pre-configured options object.
    /// This allows for configuration from external sources (e.g., appsettings.json).
    /// </summary>
    /// <param name="options">The MQTT connection options.</param>
    /// <returns>The builder for chaining.</returns>
    IMqttClientBuilder WithOptions(MqttConnectionOptions options);
}

