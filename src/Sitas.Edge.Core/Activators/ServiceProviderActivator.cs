using Microsoft.Extensions.DependencyInjection;
using Sitas.Edge.Core.Abstractions;

namespace Sitas.Edge.Core.Activators;

/// <summary>
/// Handler activator that uses any IServiceProvider implementation.
/// Works with Microsoft.Extensions.DependencyInjection, Autofac, SimpleInjector, 
/// Ninject, Lamar, DryIoc, or any container that provides IServiceProvider.
/// </summary>
public sealed class ServiceProviderActivator : IHandlerActivator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory? _scopeFactory;

    /// <summary>
    /// Creates a new activator using the specified service provider.
    /// </summary>
    /// <param name="serviceProvider">The service provider to use for resolving handlers.</param>
    public ServiceProviderActivator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _scopeFactory = serviceProvider.GetService<IServiceScopeFactory>();
    }

    /// <inheritdoc />
    public object CreateInstance(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        
        var handler = _serviceProvider.GetService(handlerType);
        
        if (handler is null)
        {
            throw new InvalidOperationException(
                $"Handler '{handlerType.Name}' is not registered. " +
                $"Register it in your DI container before building the connection.");
        }

        return handler;
    }

    /// <inheritdoc />
    public THandler CreateInstance<THandler>() where THandler : class
        => (THandler)CreateInstance(typeof(THandler));

    /// <inheritdoc />
    public IScopedHandler CreateScopedInstance(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        
        if (_scopeFactory is null)
        {
            // No scope factory available, create without scope
            return new ScopedHandler(CreateInstance(handlerType), null);
        }

        var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetService(handlerType);
        
        if (handler is null)
        {
            scope.Dispose();
            throw new InvalidOperationException(
                $"Handler '{handlerType.Name}' is not registered. " +
                $"Register it in your DI container before building the connection.");
        }

        return new ScopedHandler(handler, scope);
    }

    private sealed class ScopedHandler : IScopedHandler
    {
        private readonly IServiceScope? _scope;

        public object Handler { get; }

        public ScopedHandler(object handler, IServiceScope? scope)
        {
            Handler = handler;
            _scope = scope;
        }

        public void Dispose() => _scope?.Dispose();
    }
}
