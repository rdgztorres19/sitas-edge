using Sitas.Edge.Core.Abstractions;

namespace Sitas.Edge.Core.Internal;

/// <summary>
/// Default implementation of message context.
/// </summary>
public sealed class DefaultMessageContext : IMessageContext
{
    /// <inheritdoc />
    public string Topic { get; }
    
    /// <inheritdoc />
    public string? CorrelationId { get; }
    
    /// <inheritdoc />
    public DateTimeOffset ReceivedAt { get; }
    
    /// <inheritdoc />
    public ReadOnlyMemory<byte> RawPayload { get; }
    
    /// <inheritdoc />
    public IMessagePublisher Publisher { get; }
    
    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultMessageContext"/> class.
    /// </summary>
    /// <param name="topic">The topic the message was received on.</param>
    /// <param name="rawPayload">The raw message payload bytes.</param>
    /// <param name="publisher">The publisher for sending responses.</param>
    /// <param name="correlationId">Optional correlation ID for message tracking.</param>
    /// <param name="metadata">Optional additional metadata.</param>
    public DefaultMessageContext(
        string topic,
        ReadOnlyMemory<byte> rawPayload,
        IMessagePublisher publisher,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        RawPayload = rawPayload;
        Publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        CorrelationId = correlationId;
        ReceivedAt = DateTimeOffset.UtcNow;
        Metadata = metadata ?? new Dictionary<string, string>();
    }
}

/// <summary>
/// Default implementation of typed message context.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
public sealed class DefaultMessageContext<TMessage> : IMessageContext<TMessage>
    where TMessage : class
{
    private readonly IMessageContext _inner;

    /// <inheritdoc />
    public TMessage Message { get; }
    
    /// <inheritdoc />
    public string Topic => _inner.Topic;
    
    /// <inheritdoc />
    public string? CorrelationId => _inner.CorrelationId;
    
    /// <inheritdoc />
    public DateTimeOffset ReceivedAt => _inner.ReceivedAt;
    
    /// <inheritdoc />
    public ReadOnlyMemory<byte> RawPayload => _inner.RawPayload;
    
    /// <inheritdoc />
    public IMessagePublisher Publisher => _inner.Publisher;
    
    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Metadata => _inner.Metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultMessageContext{TMessage}"/> class.
    /// </summary>
    /// <param name="inner">The base message context.</param>
    /// <param name="message">The deserialized message.</param>
    public DefaultMessageContext(IMessageContext inner, TMessage message)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }
}
