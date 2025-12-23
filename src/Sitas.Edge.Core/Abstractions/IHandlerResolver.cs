namespace Sitas.Edge.Core.Abstractions;

/// <summary>
/// Resolves handler instances from a dependency injection container or factory.
/// </summary>
public interface IHandlerResolver
{
    /// <summary>
    /// Resolves a handler instance of the specified type.
    /// </summary>
    /// <param name="handlerType">The type of handler to resolve.</param>
    /// <returns>The resolved handler instance.</returns>
    object Resolve(Type handlerType);

    /// <summary>
    /// Resolves a handler instance of the specified type.
    /// </summary>
    /// <typeparam name="THandler">The type of handler to resolve.</typeparam>
    /// <returns>The resolved handler instance.</returns>
    THandler Resolve<THandler>() where THandler : class;

    /// <summary>
    /// Resolves a scoped handler instance. The scope should be disposed after use.
    /// </summary>
    /// <param name="handlerType">The type of handler to resolve.</param>
    /// <returns>A scoped handler that should be disposed after use.</returns>
    IScopedHandler ResolveScoped(Type handlerType);
}

