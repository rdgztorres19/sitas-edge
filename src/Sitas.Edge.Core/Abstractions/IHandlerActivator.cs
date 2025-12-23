namespace Sitas.Edge.Core.Abstractions;

/// <summary>
/// Activates (creates) handler instances from a dependency injection container.
/// Implement this interface to integrate with any DI container.
/// </summary>
/// <remarks>
/// This is the primary extension point for DI integration.
/// Each DI container should provide its own implementation.
/// </remarks>
public interface IHandlerActivator
{
    /// <summary>
    /// Creates an instance of the specified handler type.
    /// </summary>
    /// <param name="handlerType">The type of handler to create.</param>
    /// <returns>The activated handler instance.</returns>
    object CreateInstance(Type handlerType);

    /// <summary>
    /// Creates an instance of the specified handler type.
    /// </summary>
    /// <typeparam name="THandler">The type of handler to create.</typeparam>
    /// <returns>The activated handler instance.</returns>
    THandler CreateInstance<THandler>() where THandler : class
        => (THandler)CreateInstance(typeof(THandler));

    /// <summary>
    /// Creates a scoped instance of the specified handler type.
    /// The returned scope should be disposed after the handler completes.
    /// </summary>
    /// <param name="handlerType">The type of handler to create.</param>
    /// <returns>A scoped handler that should be disposed after use.</returns>
    IScopedHandler CreateScopedInstance(Type handlerType);
}

/// <summary>
/// Represents a handler instance with an associated scope that should be disposed.
/// </summary>
public interface IScopedHandler : IDisposable
{
    /// <summary>
    /// Gets the handler instance.
    /// </summary>
    object Handler { get; }
}
