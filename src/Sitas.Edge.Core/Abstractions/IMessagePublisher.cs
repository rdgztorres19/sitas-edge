using Sitas.Edge.Core.Enums;

namespace Sitas.Edge.Core.Abstractions;

/// <summary>
/// Provides functionality to publish messages to a service bus.
/// Can be injected into handlers or used standalone for publishing outside of handlers.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a message to the specified topic.
    /// </summary>
    /// <typeparam name="TMessage">The type of message to publish.</typeparam>
    /// <param name="topic">The topic or channel to publish to.</param>
    /// <param name="message">The message to publish.</param>
    /// <param name="qos">The quality of service level for delivery.</param>
    /// <param name="retain">Whether the message should be retained by the broker.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishAsync<TMessage>(
        string topic,
        TMessage message,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        bool retain = false,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Publishes raw bytes to the specified topic.
    /// </summary>
    /// <param name="topic">The topic or channel to publish to.</param>
    /// <param name="payload">The raw message payload.</param>
    /// <param name="qos">The quality of service level for delivery.</param>
    /// <param name="retain">Whether the message should be retained by the broker.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        bool retain = false,
        CancellationToken cancellationToken = default);
}

