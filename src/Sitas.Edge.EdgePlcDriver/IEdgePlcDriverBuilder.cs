using System.Reflection;
using Sitas.Edge.EdgePlcDriver.Configuration;
using Sitas.Edge.Core.Abstractions;

namespace Sitas.Edge.EdgePlcDriver;

/// <summary>
/// Builder interface for configuring Edge PLC Driver connections.
/// </summary>
public interface IEdgePlcDriverBuilder : IServiceBusBuilder<IEdgePlcDriverBuilder, IEdgePlcDriver>
{
    /// <summary>
    /// Sets the logical name for this driver.
    /// Used to match handlers decorated with [EdgePlcDriverSubscribe] to this driver.
    /// </summary>
    /// <param name="name">The driver name.</param>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithConnectionName(string name);

    /// <summary>
    /// Configures the PLC endpoint.
    /// </summary>
    /// <param name="ipAddress">The IP address of the PLC or ENET/ENBT module.</param>
    /// <param name="cpuSlot">The slot number of the CPU. Default is 0.</param>
    /// <param name="backplane">The backplane number. Default is 1.</param>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithPlc(string ipAddress, int cpuSlot = 0, int backplane = 1);

    /// <summary>
    /// Sets the default polling interval for tag subscriptions.
    /// </summary>
    /// <param name="intervalMs">Polling interval in milliseconds.</param>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithDefaultPollingInterval(int intervalMs);

    /// <summary>
    /// Configures connection timeout.
    /// </summary>
    /// <param name="timeoutSeconds">Timeout in seconds.</param>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithConnectionTimeout(int timeoutSeconds);

    /// <summary>
    /// Configures automatic reconnection behavior.
    /// </summary>
    /// <param name="enabled">Whether to enable auto-reconnect.</param>
    /// <param name="maxDelaySeconds">Maximum delay between reconnection attempts.</param>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithAutoReconnect(bool enabled = true, int maxDelaySeconds = 30);

    /// <summary>
    /// Configures a custom message serializer for UDT handling.
    /// </summary>
    /// <param name="serializer">The serializer to use.</param>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithSerializer(IMessageSerializer serializer);

    /// <summary>
    /// Configures the logger factory for internal logging.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to use.</param>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithLoggerFactory(Microsoft.Extensions.Logging.ILoggerFactory loggerFactory);

    /// <summary>
    /// Configures the handler activator for dependency injection.
    /// </summary>
    /// <param name="activator">The handler activator.</param>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithHandlerActivator(IHandlerActivator activator);

    /// <summary>
    /// Discovers and registers message handlers from the specified assemblies.
    /// Handlers must be decorated with [EdgePlcDriverSubscribe] attribute.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for handlers.</param>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithHandlersFromAssemblies(params Assembly[] assemblies);

    /// <summary>
    /// Discovers and registers message handlers from the entry assembly.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithHandlersFromEntryAssembly();

    /// <summary>
    /// Applies configuration from an options object.
    /// </summary>
    /// <param name="options">The driver options.</param>
    /// <returns>The builder for chaining.</returns>
    IEdgePlcDriverBuilder WithOptions(EdgePlcDriverOptions options);
}
