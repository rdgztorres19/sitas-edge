using System.Linq;
using System.Reflection;
using Sitas.Edge.Core.Abstractions;

namespace Sitas.Edge.Core.Activators;

/// <summary>
/// Handler activator that uses a delegate function.
/// Provides flexibility for any DI container integration.
/// </summary>
/// <remarks>
/// This activator automatically creates handler instances even if they are not
/// explicitly registered in the DI container. It resolves constructor dependencies
/// using the provided factory function.
/// </remarks>
/// <example>
/// // Autofac - handlers are created automatically, no registration needed
/// var activator = new FuncActivator(type => container.Resolve(type));
/// 
/// // SimpleInjector
/// var activator = new FuncActivator(type => container.GetInstance(type));
/// 
/// // Ninject
/// var activator = new FuncActivator(type => kernel.Get(type));
/// </example>
public sealed class FuncActivator : IHandlerActivator
{
    private readonly Func<Type, object> _factory;
    private ISitasEdge? _conduitInstance;

    /// <summary>
    /// Creates a new activator using the specified factory delegate.
    /// </summary>
    /// <param name="factory">A function that resolves service instances by type.</param>
    public FuncActivator(Func<Type, object> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Sets the Conduit instance for auto-injection.
    /// Called internally by SitasEdgeBuilder after creating the Conduit.
    /// </summary>
    internal void SetConduitInstance(ISitasEdge conduit)
    {
        _conduitInstance = conduit;
    }

    /// <inheritdoc />
    public object CreateInstance(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);

        // Auto-inject ISitasEdge without needing to register it in DI
        if (handlerType == typeof(ISitasEdge))
        {
            if (_conduitInstance is null)
            {
                Console.WriteLine($"❌ FuncActivator: ISitasEdge requested but Conduit is not initialized yet.");
                throw new InvalidOperationException("ISitasEdge requested but Conduit is not initialized yet.");
            }
            return _conduitInstance;
        }

        // 1. First try to resolve from container directly
        try
        {
            var handler = _factory(handlerType);
            if (handler is not null)
                return handler;
        }
        catch
        {
            // Type is not registered, create it manually
        }

        // 2. Create instance manually, resolving constructor dependencies
        try
        {
            return CreateInstanceWithDependencies(handlerType);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FuncActivator: Failed to create instance of {handlerType.Name}");
            Console.WriteLine($"   Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
            throw;
        }
    }

    /// <summary>
    /// Creates an instance of the handler type, resolving all constructor dependencies.
    /// </summary>
    private object CreateInstanceWithDependencies(Type handlerType)
    {
        // Find constructor with most parameters (greedy)
        var constructors = handlerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        
        if (constructors.Length == 0)
        {
            Console.WriteLine($"❌ FuncActivator: Handler '{handlerType.Name}' has no public constructors.");
            throw new InvalidOperationException(
                $"Handler '{handlerType.Name}' has no public constructors.");
        }

        var constructor = constructors
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var parameters = constructor.GetParameters();
        var args = new object[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var paramType = parameters[i].ParameterType;
            
            // Intercept ISitasEdge here as well (for constructor dependencies)
            if (paramType == typeof(ISitasEdge))
            {
                if (_conduitInstance is null)
                {
                    Console.WriteLine($"❌ FuncActivator: Handler '{handlerType.Name}' requires ISitasEdge, but Conduit is not initialized yet.");
                    throw new InvalidOperationException(
                        $"Handler '{handlerType.Name}' requires ISitasEdge, but Conduit is not initialized yet.");
                }
                args[i] = _conduitInstance;
                continue;
            }
            
            // Auto-inject connections (IServiceBusConnection, IMqttConnection, IEdgePlcDriver, etc.)
            // Recognize interfaces that implement IServiceBusConnection
            var isServiceBusConnectionType = paramType == typeof(IServiceBusConnection) || 
                                            (paramType.IsInterface && typeof(IServiceBusConnection).IsAssignableFrom(paramType));
            
            if (isServiceBusConnectionType)
            {
                // First try to get from Conduit
                if (_conduitInstance != null)
                {
                    try
                    {
                        // For IServiceBusConnection (base interface), search in all connections
                        if (paramType == typeof(IServiceBusConnection))
                        {
                            var connections = _conduitInstance.GetType().GetProperty("Connections")?.GetValue(_conduitInstance) as System.Collections.IEnumerable;
                            if (connections != null)
                            {
                                foreach (var conn in connections)
                                {
                                    if (conn != null && paramType.IsAssignableFrom(conn.GetType()))
                                    {
                                        args[i] = conn;
                                        continue; // Continue with next parameter
                                    }
                                }
                            }
                        }
                        else
                        {
                            // For specific connection interfaces (IMqttConnection, IEdgePlcDriver, etc.), use GetConnection<T>()
                            var getConnectionMethod = typeof(ISitasEdge).GetMethod("GetConnection")!.MakeGenericMethod(paramType);
                            var connection = getConnectionMethod.Invoke(_conduitInstance, null);
                            if (connection != null)
                            {
                                args[i] = connection;
                                continue; // Continue with next parameter
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not InvalidOperationException)
                    {
                        // Failed to get from Conduit, will try DI container
                    }
                }
                
                // If not found in Conduit, try to search in DI container (useful for NullEdgePlcDriver)
                try
                {
                    var connection = _factory(paramType);
                    if (connection != null)
                    {
                        args[i] = connection;
                        continue; // Continue with next parameter
                    }
                }
                catch
                {
                    // Not in DI container, continue to throw error
                }
                
                Console.WriteLine($"  ❌ Parameter {i + 1}: {paramType.Name} not found in Conduit or DI container");
                throw new InvalidOperationException(
                    $"Handler '{handlerType.Name}' requires {paramType.Name}, but no connection of that type is configured. " +
                    $"Ensure you've added the corresponding connection (e.g., AddMqttConnection, AddEdgePlcDriver) or registered it in your DI container.");
            }
            
            // Auto-inject publishers (IMessagePublisher, IMqttPublisher, IEdgePlcDriverPublisher)
            // Publishers are obtained from connections: IMqttConnection.Publisher, IEdgePlcDriver.Publisher, etc.
            // Note: IMqttPublisher and IEdgePlcDriverPublisher implement IMessagePublisher
            if (paramType.Name.StartsWith("I") && paramType.Name.Contains("Publisher") && paramType.IsInterface)
            {
                bool publisherResolved = false;
                
                // First try to get from Conduit connections
                if (_conduitInstance != null)
                {
                    try
                    {
                        var connections = _conduitInstance.GetType().GetProperty("Connections")?.GetValue(_conduitInstance) as System.Collections.IEnumerable;
                        if (connections != null)
                        {
                            // If requesting IMessagePublisher (base interface), we can get it from any connection
                            var isBasePublisher = paramType == typeof(IMessagePublisher);
                            
                            foreach (var conn in connections)
                            {
                                if (conn == null) continue;
                                
                                var connType = conn.GetType();
                                
                                // If it's IMessagePublisher, search directly in IServiceBusConnection.Publisher
                                if (isBasePublisher)
                                {
                                    // Search for Publisher property of IServiceBusConnection
                                    var serviceBusInterface = typeof(IServiceBusConnection);
                                    var publisherProp = serviceBusInterface.GetProperty("Publisher", BindingFlags.Public | BindingFlags.Instance);
                                    if (publisherProp != null && serviceBusInterface.IsAssignableFrom(connType))
                                    {
                                        var publisher = publisherProp.GetValue(conn);
                                        if (publisher != null)
                                        {
                                            args[i] = publisher;
                                            publisherResolved = true;
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    // For specific publishers (IMqttPublisher, IEdgePlcDriverPublisher),
                                    // search in all interfaces that the connection implements
                                    // This is necessary because IMqttConnection has "new IMqttPublisher Publisher"
                                    // which hides IServiceBusConnection.Publisher
                                    var interfaces = connType.GetInterfaces();
                                    
                                    foreach (var iface in interfaces)
                                    {
                                        var publisherProp = iface.GetProperty("Publisher", BindingFlags.Public | BindingFlags.Instance);
                                        if (publisherProp != null)
                                        {
                                            // Get the publisher and verify if it implements the requested interface
                                            // This is more reliable than checking only the return type
                                            var publisher = publisherProp.GetValue(conn);
                                            if (publisher != null)
                                            {
                                                // Verify that the returned object actually implements the requested interface
                                                // paramType.IsAssignableFrom(publisher.GetType()) means:
                                                // "Can I assign publisher to a variable of type paramType?"
                                                if (paramType.IsAssignableFrom(publisher.GetType()))
                                                {
                                                    args[i] = publisher;
                                                    publisherResolved = true;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    
                                    if (publisherResolved) break;
                                    
                                    // If not found in interfaces, search in concrete type as fallback
                                    var concreteProp = connType.GetProperty("Publisher", BindingFlags.Public | BindingFlags.Instance);
                                    if (concreteProp != null)
                                    {
                                        var publisher = concreteProp.GetValue(conn);
                                        if (publisher != null && paramType.IsAssignableFrom(publisher.GetType()))
                                        {
                                            args[i] = publisher;
                                            publisherResolved = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not InvalidOperationException)
                    {
                        // Error searching in Conduit connections, will try DI container
                    }
                }
                
                // If not found in connections, try to search in DI container
                // (useful if publisher was manually registered in DI container)
                if (!publisherResolved)
                {
                    try
                    {
                        var publisher = _factory(paramType);
                        if (publisher != null)
                        {
                            args[i] = publisher;
                            publisherResolved = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        // If DI container tries to create the interface directly, it will fail
                        // because interfaces cannot be instantiated. This is expected.
                    }
                }
                
                // If resolved, continue with next parameter
                if (publisherResolved)
                {
                    continue;
                }
                
                // If we reach here, publisher was not found in any connection or DI container
                Console.WriteLine($"  ❌ Parameter {i + 1}: {paramType.Name} not found in any connection or DI container");
                Console.WriteLine($"     Available connections: {(_conduitInstance != null ? string.Join(", ", ((System.Collections.IEnumerable)_conduitInstance.Connections).Cast<object>().Select(c => c?.GetType().Name ?? "null")) : "none")}");
                throw new InvalidOperationException(
                    $"Handler '{handlerType.Name}' requires {paramType.Name}, but no compatible connection with that publisher type is configured. " +
                    $"Ensure you've added a connection that provides this publisher (e.g., IMqttConnection for IMqttPublisher, IEdgePlcDriver for IEdgePlcDriverPublisher).");
            }
            
            // If it's ILogger<> and not registered, use NullLogger automatically
            if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>))
            {
                try
                {
                    args[i] = _factory(paramType);
                }
                catch
                {
                    // Not registered, create NullLogger
                    var nullLoggerType = typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>)
                        .MakeGenericType(paramType.GetGenericArguments()[0]);
                    args[i] = Activator.CreateInstance(nullLoggerType)!;
                }
                continue;
            }
            
            try
            {
                args[i] = _factory(paramType);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Parameter {i + 1}: Failed to resolve {paramType.Name} from DI container: {ex.Message}");
                throw new InvalidOperationException(
                    $"Cannot resolve dependency '{paramType.Name}' for handler '{handlerType.Name}'. " +
                    $"Ensure the dependency is registered in your DI container.", ex);
            }

            if (args[i] is null)
            {
                Console.WriteLine($"  ❌ Parameter {i + 1}: {paramType.Name} resolved to null");
                throw new InvalidOperationException(
                    $"Dependency '{paramType.Name}' for handler '{handlerType.Name}' resolved to null. " +
                    $"Ensure the dependency is properly registered.");
            }
        }

        try
        {
            var instance = constructor.Invoke(args);
            return instance;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ CreateInstanceWithDependencies: Constructor invocation failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"     Inner Exception: {ex.InnerException.Message}");
            }
            throw;
        }
    }

    /// <inheritdoc />
    public THandler CreateInstance<THandler>() where THandler : class
        => (THandler)CreateInstance(typeof(THandler));

    /// <inheritdoc />
    public IScopedHandler CreateScopedInstance(Type handlerType)
    {
        // FuncActivator doesn't support scopes, create without scope
        return new NoScopeHandler(CreateInstance(handlerType));
    }

    private sealed class NoScopeHandler : IScopedHandler
    {
        public object Handler { get; }

        public NoScopeHandler(object handler) => Handler = handler;

        public void Dispose() { } // No scope to dispose
    }
}
