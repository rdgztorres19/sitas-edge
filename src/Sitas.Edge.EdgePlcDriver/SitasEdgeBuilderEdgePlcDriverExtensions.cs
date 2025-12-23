using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sitas.Edge.Core;

namespace Sitas.Edge.EdgePlcDriver;

/// <summary>
/// Extension methods for adding Edge PLC Driver connections to SitasEdgeBuilder.
/// </summary>
public static class SitasEdgeBuilderEdgePlcDriverExtensions
{
    /// <summary>
    /// Adds an Edge PLC Driver connection to the SitasEdge builder.
    /// </summary>
    /// <example>
    /// <code>
    /// var conduit = SitasEdgeBuilder.Create()
    ///     .WithServiceProvider(serviceProvider)
    ///     .AddEdgePlcDriver(plc => plc
    ///         .WithConnectionName("plc1")
    ///         .WithPlc("192.168.1.10", cpuSlot: 0)
    ///         .WithDefaultPollingInterval(100)
    ///         .WithHandlersFromEntryAssembly())
    ///     .Build();
    /// </code>
    /// </example>
    public static SitasEdgeBuilder AddEdgePlcDriver(
        this SitasEdgeBuilder builder,
        Action<IEdgePlcDriverBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddConnection((activator, serviceProvider) =>
        {
            var edgePlcBuilder = EdgePlcDriverBuilder.Create();
            edgePlcBuilder.WithHandlerActivator(activator);

            // Configure logging if IServiceProvider is available
            if (serviceProvider is not null)
            {
                var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                if (loggerFactory is not null)
                {
                    ((EdgePlcDriverBuilder)edgePlcBuilder).WithLoggerFactory(loggerFactory);
                }
            }

            configure(edgePlcBuilder);
            return edgePlcBuilder.Build();
        });
    }

    /// <summary>
    /// Adds an Edge PLC Driver connection with a specific connection name.
    /// </summary>
    /// <param name="builder">The SitasEdge builder.</param>
    /// <param name="connectionName">The logical name for this driver.</param>
    /// <param name="configure">Configuration action for the driver.</param>
    /// <returns>The builder for chaining.</returns>
    public static SitasEdgeBuilder AddEdgePlcDriver(
        this SitasEdgeBuilder builder,
        string connectionName,
        Action<IEdgePlcDriverBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(connectionName);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddEdgePlcDriver(plc =>
        {
            plc.WithConnectionName(connectionName);
            configure(plc);
        });
    }

    /// <summary>
    /// Adds an Edge PLC Driver connection with minimal configuration.
    /// </summary>
    /// <param name="builder">The SitasEdge builder.</param>
    /// <param name="connectionName">The logical name for this driver.</param>
    /// <param name="ipAddress">The IP address of the PLC.</param>
    /// <param name="cpuSlot">The CPU slot number (default 0).</param>
    /// <param name="pollingIntervalMs">Default polling interval in milliseconds (default 100).</param>
    /// <returns>The builder for chaining.</returns>
    public static SitasEdgeBuilder AddEdgePlcDriver(
        this SitasEdgeBuilder builder,
        string connectionName,
        string ipAddress,
        int cpuSlot = 0,
        int pollingIntervalMs = 100)
    {
        return builder.AddEdgePlcDriver(connectionName, plc => plc
            .WithPlc(ipAddress, cpuSlot)
            .WithDefaultPollingInterval(pollingIntervalMs)
            .WithHandlersFromEntryAssembly());
    }
}
