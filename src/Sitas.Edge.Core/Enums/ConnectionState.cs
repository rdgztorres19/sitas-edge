namespace Sitas.Edge.Core.Enums;

/// <summary>
/// Represents the current state of a service bus connection.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// The connection has been created but not yet connected.
    /// </summary>
    Disconnected,

    /// <summary>
    /// The connection is currently establishing.
    /// </summary>
    Connecting,

    /// <summary>
    /// The connection is established and ready.
    /// </summary>
    Connected,

    /// <summary>
    /// The connection is in the process of disconnecting.
    /// </summary>
    Disconnecting,

    /// <summary>
    /// The connection encountered an error.
    /// </summary>
    Faulted,

    /// <summary>
    /// The connection is attempting to reconnect.
    /// </summary>
    Reconnecting
}

