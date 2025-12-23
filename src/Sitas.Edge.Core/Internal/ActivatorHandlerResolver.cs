using Sitas.Edge.Core.Abstractions;

namespace Sitas.Edge.Core.Internal;

/// <summary>
/// Default handler resolver that uses Activator.CreateInstance.
/// For use when dependency injection is not configured.
/// </summary>
public sealed class ActivatorHandlerResolver : IHandlerResolver
{
    /// <summary>
    /// Gets the singleton instance of the activator handler resolver.
    /// </summary>
    public static ActivatorHandlerResolver Instance { get; } = new();

    private ActivatorHandlerResolver()
    {
    }

    /// <inheritdoc />
    public object Resolve(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        
        return Activator.CreateInstance(handlerType)
            ?? throw new InvalidOperationException($"Failed to create instance of {handlerType.Name}");
    }

    /// <inheritdoc />
    public THandler Resolve<THandler>() where THandler : class
    {
        return (THandler)Resolve(typeof(THandler));
    }

    /// <inheritdoc />
    public IScopedHandler ResolveScoped(Type handlerType)
    {
        // No scope support for Activator-based resolution
        return new NoScopeHandler(Resolve(handlerType));
    }

    private sealed class NoScopeHandler : IScopedHandler
    {
        public object Handler { get; }

        public NoScopeHandler(object handler) => Handler = handler;

        public void Dispose() { }
    }
}
