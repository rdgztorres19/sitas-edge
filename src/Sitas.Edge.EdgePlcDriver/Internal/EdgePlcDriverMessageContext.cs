using Sitas.Edge.EdgePlcDriver.Messages;
using Sitas.Edge.Core.Abstractions;

namespace Sitas.Edge.EdgePlcDriver.Internal;

/// <summary>
/// Edge PLC Driver-specific message context implementation.
/// </summary>
internal sealed class EdgePlcDriverMessageContext : IEdgePlcDriverMessageContext
{
    private readonly IEdgePlcDriver _connection;

    public string Topic { get; }
    public string TagName { get; }
    public string? CorrelationId { get; }
    public DateTimeOffset ReceivedAt { get; }
    public ReadOnlyMemory<byte> RawPayload { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    
    IMessagePublisher IMessageContext.Publisher => Publisher;
    public IEdgePlcDriverPublisher Publisher { get; }

    public EdgePlcDriverMessageContext(
        string tagName,
        ReadOnlyMemory<byte> rawPayload,
        IEdgePlcDriverPublisher publisher,
        IEdgePlcDriver connection,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        TagName = tagName;
        Topic = tagName; // Tag name acts as the "topic" in PLC context
        RawPayload = rawPayload;
        Publisher = publisher;
        _connection = connection;
        CorrelationId = correlationId;
        ReceivedAt = DateTimeOffset.UtcNow;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public Task WriteTagAsync<T>(string tagName, T value, CancellationToken cancellationToken = default)
    {
        return Publisher.WriteTagAsync(tagName, value, cancellationToken);
    }

    public Task<TagValue<T>> ReadTagAsync<T>(string tagName, CancellationToken cancellationToken = default)
    {
        return _connection.ReadTagAsync<T>(tagName, cancellationToken);
    }
}
