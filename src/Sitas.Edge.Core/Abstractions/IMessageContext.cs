using Sitas.Edge.Core.Enums;

namespace Sitas.Edge.Core.Abstractions;

/// <summary>
/// Provides context information about a received message, including the ability to publish responses.
/// </summary>
public interface IMessageContext
{
    /// <summary>
    /// Gets the topic or channel from which the message was received.
    /// </summary>
    string Topic { get; }

    /// <summary>
    /// Gets the correlation identifier for message tracking.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    /// Gets the timestamp when the message was received.
    /// </summary>
    DateTimeOffset ReceivedAt { get; }

    /// <summary>
    /// Gets the raw payload bytes.
    /// </summary>
    ReadOnlyMemory<byte> RawPayload { get; }

    /// <summary>
    /// Gets the publisher interface for sending response messages.
    /// </summary>
    IMessagePublisher Publisher { get; }

    /// <summary>
    /// Gets additional metadata associated with the message.
    /// </summary>
    IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// Provides strongly-typed context for received messages.
/// </summary>
/// <typeparam name="TMessage">The type of the deserialized message.</typeparam>
public interface IMessageContext<out TMessage> : IMessageContext
    where TMessage : class
{
    /// <summary>
    /// Gets the deserialized message payload.
    /// </summary>
    TMessage Message { get; }
}

