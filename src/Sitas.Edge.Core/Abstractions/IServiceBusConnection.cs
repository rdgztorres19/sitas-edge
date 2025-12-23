using Sitas.Edge.Core.Enums;

namespace Sitas.Edge.Core.Abstractions;

/// <summary>
/// Represents an active connection to a service bus.
/// Manages the connection lifecycle and provides publishing capabilities.
/// </summary>
public interface IServiceBusConnection : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the logical name of this connection (e.g., "mqtt", "plc1").
    /// This matches the name used in subscription attributes and event tag reads.
    /// </summary>
    string ConnectionName { get; }

    /// <summary>
    /// Gets the unique identifier for this connection.
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// Gets the current state of the connection.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Gets the publisher for sending messages through this connection.
    /// </summary>
    IMessagePublisher Publisher { get; }

    /// <summary>
    /// Gets a value indicating whether the connection is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Occurs when the connection state changes.
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Establishes the connection to the service bus and starts listening for messages.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous connect operation.</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the service bus gracefully.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous disconnect operation.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Event arguments for connection state changes.
/// </summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous connection state.
    /// </summary>
    public ConnectionState PreviousState { get; }

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public ConnectionState CurrentState { get; }

    /// <summary>
    /// Gets the exception that caused the state change, if any.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionStateChangedEventArgs"/> class.
    /// </summary>
    public ConnectionStateChangedEventArgs(ConnectionState previousState, ConnectionState currentState, Exception? exception = null)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        Exception = exception;
    }
}

