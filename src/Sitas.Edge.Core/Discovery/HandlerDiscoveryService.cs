using System.Reflection;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Attributes;

namespace Sitas.Edge.Core.Discovery;

/// <summary>
/// Discovers message handlers using reflection by scanning assemblies for types
/// decorated with subscription attributes.
/// </summary>
public sealed class HandlerDiscoveryService
{
#pragma warning disable CS0618 // IMessageHandler<> is obsolete but supported for backward compatibility.
    private static readonly Type MessageHandlerOpenType = typeof(IMessageHandler<>);
#pragma warning restore CS0618
    private static readonly Type MessageSubscriptionHandlerOpenType = typeof(IMessageSubscriptionHandler<>);

    /// <summary>
    /// Discovers all handlers in the specified assemblies.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>A collection of discovered handler registrations.</returns>
    public IReadOnlyList<HandlerRegistration> DiscoverHandlers(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var registrations = new List<HandlerRegistration>();

        foreach (var assembly in assemblies)
        {
            registrations.AddRange(DiscoverInAssembly(assembly));
        }

        return registrations;
    }

    /// <summary>
    /// Discovers handlers in the calling assembly.
    /// </summary>
    /// <returns>A collection of discovered handler registrations.</returns>
    public IReadOnlyList<HandlerRegistration> DiscoverHandlersFromCallingAssembly()
    {
        return DiscoverHandlers(Assembly.GetCallingAssembly());
    }

    /// <summary>
    /// Discovers handlers in the entry assembly.
    /// </summary>
    /// <returns>A collection of discovered handler registrations.</returns>
    public IReadOnlyList<HandlerRegistration> DiscoverHandlersFromEntryAssembly()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        return entryAssembly is not null 
            ? DiscoverHandlers(entryAssembly) 
            : [];
    }

    private static IEnumerable<HandlerRegistration> DiscoverInAssembly(Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(IsValidHandlerType);

        foreach (var handlerType in handlerTypes)
        {
            var subscribeAttributes = handlerType.GetCustomAttributes<SubscribeAttribute>();
            var messageType = GetMessageType(handlerType);

            if (messageType is null)
                continue;

            foreach (var attribute in subscribeAttributes)
            {
                yield return new HandlerRegistration(
                    handlerType,
                    messageType,
                    attribute.ConnectionName,
                    attribute.Topic,
                    attribute.QualityOfService,
                    attribute);
            }
        }
    }

    private static bool IsValidHandlerType(Type type)
    {
        return type.IsClass 
            && !type.IsAbstract 
            && !type.IsGenericTypeDefinition
            && ImplementsMessageHandler(type)
            && type.GetCustomAttributes<SubscribeAttribute>().Any()
            && !type.IsDefined(typeof(DisableHandlerAttribute), inherit: false);  // 🚫 Ignore disabled handlers
    }

    private static bool ImplementsMessageHandler(Type type)
    {
        return type.GetInterfaces()
            .Any(i => i.IsGenericType && 
                (i.GetGenericTypeDefinition() == MessageHandlerOpenType ||
                 i.GetGenericTypeDefinition() == MessageSubscriptionHandlerOpenType));
    }

    private static Type? GetMessageType(Type handlerType)
    {
        var handlerInterface = handlerType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && 
                (i.GetGenericTypeDefinition() == MessageHandlerOpenType ||
                 i.GetGenericTypeDefinition() == MessageSubscriptionHandlerOpenType));

        return handlerInterface?.GetGenericArguments().FirstOrDefault();
    }
}

