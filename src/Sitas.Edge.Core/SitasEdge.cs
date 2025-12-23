using Sitas.Edge.Core.Abstractions;

namespace Sitas.Edge.Core;

/// <summary>
/// Default implementation of ISitasEdge that manages multiple connections.
/// </summary>
internal sealed class SitasEdge : ISitasEdge
{
    private readonly List<object> _connections;

    public IReadOnlyList<object> Connections => _connections.AsReadOnly();
    public IHandlerActivator Activator { get; }

    internal SitasEdge(List<object> connections, IHandlerActivator activator)
    {
        _connections = connections;
        Activator = activator;
    }

    public TConnection GetConnection<TConnection>() where TConnection : class
    {
        var connection = _connections.OfType<TConnection>().FirstOrDefault();
        
        if (connection is null)
        {
            throw new InvalidOperationException(
                $"No connection of type '{typeof(TConnection).Name}' is configured. " +
                $"Available connections: {string.Join(", ", _connections.Select(c => c.GetType().Name))}");
        }

        return connection;
    }

    public async Task ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var connection in _connections)
        {
            if (connection is IServiceBusConnection conn)
            {
                await conn.ConnectAsync(cancellationToken);
            }
        }
    }

    public async Task DisconnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var connection in _connections)
        {
            if (connection is IServiceBusConnection conn)
            {
                await conn.DisconnectAsync(cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            if (connection is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (connection is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
