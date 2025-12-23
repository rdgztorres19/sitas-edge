using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sitas.Edge.Core;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Activators;
using Sitas.Edge.Core.Events;
using Sitas.Edge.Mqtt.Events;

namespace Sitas.Edge.Mqtt;

/// <summary>
/// Extension methods for adding MQTT connections to SitasEdgeBuilder.
/// </summary>
public static class SitasEdgeBuilderMqttExtensions
{
    /// <summary>
    /// Adds an MQTT connection to the Nexus builder.
    /// </summary>
    /// <example>
    /// var nexus = SitasEdgeBuilder.Create()
    ///     .WithServiceProvider(serviceProvider)
    ///     .AddMqttConnection(mqtt => mqtt
    ///         .WithConnectionName("mqtt")
    ///         .WithBroker("localhost", 1883)
    ///         .WithCredentials("user", "password")
    ///         .WithHandlersFromEntryAssembly())
    ///     .Build();
    /// </example>
    public static SitasEdgeBuilder AddMqttConnection(
        this SitasEdgeBuilder builder,
        Action<IMqttClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddConnection((activator, serviceProvider) =>
        {
            var mqttBuilder = MqttClientBuilder.Create();
            mqttBuilder.WithHandlerActivator(activator);
            
            // Configure logging if IServiceProvider is available
            if (serviceProvider is not null)
            {
                var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                if (loggerFactory is not null)
                {
                    ((MqttClientBuilder)mqttBuilder).WithLoggerFactory(loggerFactory);
                }
            }
            
            configure(mqttBuilder);
            var connection = mqttBuilder.Build();
            
            // Register MQTT event context factory if TagReaderProvider is available
            if (serviceProvider is not null && connection is IMqttConnection mqttConn)
            {
                var tagReaderProvider = serviceProvider.GetService<ITagReaderProvider>();
                if (tagReaderProvider is not null)
                {
                    // Get connection name from the builder's options
                    var connectionName = ((MqttClientBuilder)mqttBuilder)._options.ConnectionName;
                    if (!string.IsNullOrEmpty(connectionName))
                    {
                        tagReaderProvider.RegisterContextFactory(connectionName, eventName =>
                            new MqttEventContext(eventName, mqttConn.Publisher));
                    }
                }
            }
            
            return connection;
        });
    }

    /// <summary>
    /// Adds an MQTT connection with a specific connection name.
    /// </summary>
    public static SitasEdgeBuilder AddMqttConnection(
        this SitasEdgeBuilder builder,
        string connectionName,
        Action<IMqttClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(connectionName);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddMqttConnection(mqtt =>
        {
            mqtt.WithConnectionName(connectionName);
            configure(mqtt);
        });
    }
}
