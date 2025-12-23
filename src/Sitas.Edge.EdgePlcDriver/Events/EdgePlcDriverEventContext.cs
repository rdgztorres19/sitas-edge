using Sitas.Edge.Core.Events;

namespace Sitas.Edge.EdgePlcDriver.Events;

/// <summary>
/// Event context implementation for Edge PLC Driver connections.
/// Provides read and write access to PLC tags during event handling.
/// </summary>
public class EdgePlcDriverEventContext : IEdgePlcDriverEventContext
{
    private readonly IEdgePlcDriver _connection;
    private readonly Dictionary<string, object?> _metadata = new();

    /// <inheritdoc/>
    public string EventName { get; }

    /// <inheritdoc/>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public string? CorrelationId { get; init; }

    /// <inheritdoc/>
    public string ConnectionName { get; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?> Metadata => _metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="EdgePlcDriverEventContext"/> class.
    /// </summary>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="connection">The Edge PLC Driver connection.</param>
    public EdgePlcDriverEventContext(string eventName, IEdgePlcDriver connection)
    {
        EventName = eventName;
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ConnectionName = GetConnectionName(connection);
    }

    /// <inheritdoc/>
    public void SetMetadata(string key, object? value)
    {
        _metadata[key] = value;
    }

    /// <inheritdoc/>
    public async Task<TagReadValue<T>> ReadTagAsync<T>(string tagName, CancellationToken cancellationToken = default)
    {
        var result = await _connection.ReadTagAsync<T>(tagName, cancellationToken);
        
        return new TagReadValue<T>
        {
            TagName = tagName,
            Value = result.Value,
            Quality = MapQuality(result.Quality),
            Timestamp = result.Timestamp
        };
    }

    /// <inheritdoc/>
    public Task WriteTagAsync<T>(string tagName, T value, CancellationToken cancellationToken = default)
    {
        return _connection.WriteTagAsync(tagName, value, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(
        IEnumerable<string> tagNames,
        CancellationToken cancellationToken = default)
    {
        return await _connection.ReadTagsAsync(tagNames, cancellationToken);
    }

    private static TagQuality MapQuality(Messages.TagQuality quality)
    {
        return quality switch
        {
            Messages.TagQuality.Good => TagQuality.Good,
            Messages.TagQuality.Uncertain => TagQuality.Uncertain,
            Messages.TagQuality.Bad => TagQuality.Bad,
            Messages.TagQuality.CommError => TagQuality.CommError,
            _ => TagQuality.Bad
        };
    }

    private static string GetConnectionName(IEdgePlcDriver connection)
    {
        // Try to get connection name from the connection
        var connectionIdProperty = connection.GetType().GetProperty("ConnectionId");
        if (connectionIdProperty?.GetValue(connection) is string connectionId)
        {
            // Extract connection name from ConnectionId (format: "name-guid")
            var dashIndex = connectionId.LastIndexOf('-');
            if (dashIndex > 0)
            {
                return connectionId[..dashIndex];
            }
            return connectionId;
        }
        return "default";
    }
}
