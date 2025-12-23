using Sitas.Edge.Core.Abstractions;

namespace Sitas.Edge.Core.Activators;

/// <summary>
/// Adapts an IHandlerActivator to work as an IHandlerResolver.
/// This provides backwards compatibility with existing code.
/// </summary>
public sealed class HandlerActivatorAdapter : IHandlerResolver
{
    private readonly IHandlerActivator _activator;

    /// <summary>
    /// Creates a new adapter wrapping the specified activator.
    /// </summary>
    /// <param name="activator">The activator to wrap.</param>
    public HandlerActivatorAdapter(IHandlerActivator activator)
    {
        _activator = activator ?? throw new ArgumentNullException(nameof(activator));
    }

    /// <inheritdoc />
    public object Resolve(Type handlerType) => _activator.CreateInstance(handlerType);

    /// <inheritdoc />
    public THandler Resolve<THandler>() where THandler : class 
        => _activator.CreateInstance<THandler>();

    /// <inheritdoc />
    public IScopedHandler ResolveScoped(Type handlerType) 
        => _activator.CreateScopedInstance(handlerType);
}
