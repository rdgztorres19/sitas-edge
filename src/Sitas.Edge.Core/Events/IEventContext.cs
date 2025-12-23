namespace Sitas.Edge.Core.Events;

/// <summary>
/// Base context for event handlers. Protocol-specific contexts extend this interface
/// to provide additional capabilities (e.g., IEdgePlcDriverEventContext for PLC read/write).
/// </summary>
public interface IEventContext
{
    /// <summary>
    /// Gets the name of the event being handled.
    /// </summary>
    string EventName { get; }

    /// <summary>
    /// Gets the timestamp when the event was emitted.
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the correlation ID for tracking the event across systems.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    /// Gets additional metadata associated with the event.
    /// </summary>
    IReadOnlyDictionary<string, object?> Metadata { get; }

    /// <summary>
    /// Sets a metadata value.
    /// </summary>
    void SetMetadata(string key, object? value);
}

/// <summary>
/// Event context with MQTT-specific capabilities.
/// </summary>
public interface IMqttEventContext : IEventContext
{
    /// <summary>
    /// Publishes a message to an MQTT topic.
    /// </summary>
    /// <typeparam name="T">The type of message to publish.</typeparam>
    /// <param name="topic">The MQTT topic.</param>
    /// <param name="message">The message payload.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken = default)
        where T : class;
}

/// <summary>
/// Event context with Edge PLC Driver capabilities.
/// Provides read and write access to PLC tags during event handling.
/// </summary>
public interface IEdgePlcDriverEventContext : IEventContext
{
    /// <summary>
    /// Gets the connection name for this context.
    /// </summary>
    string ConnectionName { get; }

    /// <summary>
    /// Reads a tag value from the PLC.
    /// </summary>
    /// <typeparam name="T">The expected type of the tag value.</typeparam>
    /// <param name="tagName">The name of the tag to read.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The tag value with quality and timestamp information.</returns>
    Task<TagReadValue<T>> ReadTagAsync<T>(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a value to a PLC tag.
    /// </summary>
    /// <typeparam name="T">The type of value to write.</typeparam>
    /// <param name="tagName">The name of the tag to write.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task WriteTagAsync<T>(string tagName, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads multiple tags from the PLC.
    /// </summary>
    /// <param name="tagNames">The names of the tags to read.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A dictionary of tag names to their values.</returns>
    Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(
        IEnumerable<string> tagNames,
        CancellationToken cancellationToken = default);
}
