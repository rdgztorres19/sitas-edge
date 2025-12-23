namespace Sitas.Edge.Core.Abstractions;

/// <summary>
/// Represents a configured Nexus Service Bus instance with one or more connections.
/// </summary>
public interface ISitasEdge : IAsyncDisposable
{
    /// <summary>
    /// Gets a connection of the specified type.
    /// </summary>
    TConnection GetConnection<TConnection>() where TConnection : class;

    /// <summary>
    /// Gets all configured connections.
    /// </summary>
    IReadOnlyList<object> Connections { get; }

    /// <summary>
    /// Gets the handler activator used for dependency injection.
    /// </summary>
    IHandlerActivator Activator { get; }

    /// <summary>
    /// Connects all configured connections.
    /// </summary>
    Task ConnectAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects all configured connections.
    /// </summary>
    Task DisconnectAllAsync(CancellationToken cancellationToken = default);
}
