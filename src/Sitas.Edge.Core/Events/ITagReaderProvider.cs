namespace Sitas.Edge.Core.Events;

/// <summary>
/// Provider for tag readers used by the event mediator.
/// Implementations register tag readers for different connections (e.g., PLC connections).
/// </summary>
public interface ITagReaderProvider
{
    /// <summary>
    /// Gets a tag reader for the specified connection name.
    /// </summary>
    /// <param name="connectionName">The name of the connection.</param>
    /// <returns>The tag reader, or null if not found.</returns>
    ITagReader? GetReader(string connectionName);

    /// <summary>
    /// Creates an event context for the specified connection.
    /// </summary>
    /// <param name="connectionName">The name of the connection.</param>
    /// <param name="eventName">The name of the event.</param>
    /// <returns>A protocol-specific event context.</returns>
    IEventContext? CreateContext(string connectionName, string eventName);

    /// <summary>
    /// Registers a tag reader for a connection.
    /// </summary>
    /// <param name="connectionName">The name of the connection.</param>
    /// <param name="reader">The tag reader to register.</param>
    void RegisterReader(string connectionName, ITagReader reader);

    /// <summary>
    /// Registers a context factory for a connection.
    /// </summary>
    /// <param name="connectionName">The name of the connection.</param>
    /// <param name="contextFactory">Factory function to create contexts.</param>
    void RegisterContextFactory(string connectionName, Func<string, IEventContext> contextFactory);
}

/// <summary>
/// Interface for reading tags from a data source (e.g., PLC).
/// </summary>
public interface ITagReader
{
    /// <summary>
    /// Reads a tag value asynchronously.
    /// </summary>
    /// <param name="tagName">The name of the tag to read.</param>
    /// <param name="valueType">The expected type of the value, or null for auto-detect.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The tag value with metadata.</returns>
    Task<TagReadValue<object?>> ReadTagAsync(string tagName, Type? valueType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of <see cref="ITagReaderProvider"/>.
/// </summary>
public class TagReaderProvider : ITagReaderProvider
{
    private readonly Dictionary<string, ITagReader> _readers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<string, IEventContext>> _contextFactories = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public ITagReader? GetReader(string connectionName)
    {
        return _readers.TryGetValue(connectionName, out var reader) ? reader : null;
    }

    /// <inheritdoc/>
    public IEventContext? CreateContext(string connectionName, string eventName)
    {
        if (_contextFactories.TryGetValue(connectionName, out var factory))
        {
            return factory(eventName);
        }
        return null;
    }

    /// <inheritdoc/>
    public void RegisterReader(string connectionName, ITagReader reader)
    {
        _readers[connectionName] = reader;
    }

    /// <inheritdoc/>
    public void RegisterContextFactory(string connectionName, Func<string, IEventContext> contextFactory)
    {
        _contextFactories[connectionName] = contextFactory;
    }
}
