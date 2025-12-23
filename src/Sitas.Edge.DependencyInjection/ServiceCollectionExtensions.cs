using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sitas.Edge.Core;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Activators;
using Sitas.Edge.Core.Discovery;
using Sitas.Edge.Mqtt;

namespace Sitas.Edge.DependencyInjection;

/// <summary>
/// Extension methods for configuring Nexus Service Bus with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Nexus Service Bus to the service collection using the fluent builder API.
    /// </summary>
    /// <example>
    /// services.AddSitasEdge(nexus => nexus
    ///     .AddMqttConnection(mqtt => mqtt
    ///         .WithBroker("localhost", 1883)
    ///         .WithHandlersFromEntryAssembly()));
    /// </example>
    public static IServiceCollection AddSitasEdge(
        this IServiceCollection services,
        Action<SitasEdgeBuilder> configure)
    {
        return services.AddSitasEdge(configure, Assembly.GetCallingAssembly());
    }

    /// <summary>
    /// Adds Nexus Service Bus to the service collection with handler discovery from specified assemblies.
    /// </summary>
    public static IServiceCollection AddSitasEdge(
        this IServiceCollection services,
        Action<SitasEdgeBuilder> configure,
        params Assembly[] handlerAssemblies)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Register handlers from assemblies
        RegisterHandlers(services, handlerAssemblies);

        // Build and register ISitasEdge
        services.AddSingleton<ISitasEdge>(sp =>
        {
            var builder = SitasEdgeBuilder.Create()
                .WithServiceProvider(sp);
            
            configure(builder);
            
            return builder.Build();
        });

        // Register individual connections for easy access
        services.AddSingleton(sp => sp.GetRequiredService<ISitasEdge>().GetConnection<IMqttConnection>());
        services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<IMqttConnection>().Publisher);
        services.AddSingleton<IMqttPublisher>(sp => sp.GetRequiredService<IMqttConnection>().Publisher);

        // Auto-connect on startup
        services.AddHostedService<SitasEdgeHostedService>();

        return services;
    }

    /// <summary>
    /// Adds Nexus Service Bus with a custom activator for any DI container.
    /// </summary>
    /// <example>
    /// // Autofac
    /// services.AddSitasEdge(
    ///     type => container.Resolve(type),
    ///     nexus => nexus.AddMqttConnection(mqtt => mqtt.WithBroker("localhost")));
    /// </example>
    public static IServiceCollection AddSitasEdge(
        this IServiceCollection services,
        Func<Type, object> activator,
        Action<SitasEdgeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(activator);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton<ISitasEdge>(sp =>
        {
            var builder = SitasEdgeBuilder.Create()
                .WithActivator(activator);
            
            configure(builder);
            
            return builder.Build();
        });

        services.AddSingleton(sp => sp.GetRequiredService<ISitasEdge>().GetConnection<IMqttConnection>());
        services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<IMqttConnection>().Publisher);
        services.AddSingleton<IMqttPublisher>(sp => sp.GetRequiredService<IMqttConnection>().Publisher);
        services.AddHostedService<SitasEdgeHostedService>();

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly[] assemblies)
    {
        var discoveryService = new HandlerDiscoveryService();
        var registrations = discoveryService.DiscoverHandlers(assemblies);

        foreach (var registration in registrations)
        {
            services.TryAddTransient(registration.HandlerType);
        }
    }
}
