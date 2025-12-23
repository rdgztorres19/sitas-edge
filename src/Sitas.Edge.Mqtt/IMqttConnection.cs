using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Enums;

namespace Sitas.Edge.Mqtt;

/// <summary>
/// Represents an active MQTT connection with protocol-specific features.
/// </summary>
public interface IMqttConnection : IServiceBusConnection
{
    /// <summary>
    /// Gets the MQTT-specific publisher for enhanced publishing options.
    /// </summary>
    new IMqttPublisher Publisher { get; }

    /// <summary>
    /// Manually subscribes to a topic with a raw message handler.
    /// Use this for dynamic subscriptions not covered by attribute-based discovery.
    /// </summary>
    /// <param name="topic">The topic pattern to subscribe to.</param>
    /// <param name="handler">The handler to invoke when messages arrive.</param>
    /// <param name="qos">The quality of service level.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A disposable subscription that can be used to unsubscribe.</returns>
    Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        Func<ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task> handler,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually subscribes to a topic with a raw message handler that receives the topic.
    /// Useful for wildcard subscriptions where you need to know which topic matched.
    /// </summary>
    /// <param name="topic">The topic pattern to subscribe to.</param>
    /// <param name="handler">The handler to invoke when messages arrive (receives topic, payload, context, cancellationToken).</param>
    /// <param name="qos">The quality of service level.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A disposable subscription that can be used to unsubscribe.</returns>
    Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        Func<string, ReadOnlyMemory<byte>, IMessageContext, CancellationToken, Task> handler,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually subscribes to a topic with a typed message handler.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="topic">The topic pattern to subscribe to.</param>
    /// <param name="handler">The handler to invoke when messages arrive.</param>
    /// <param name="qos">The quality of service level.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A disposable subscription that can be used to unsubscribe.</returns>
    Task<IAsyncDisposable> SubscribeAsync<TMessage>(
        string topic,
        Func<TMessage, IMessageContext, CancellationToken, Task> handler,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        CancellationToken cancellationToken = default)
        where TMessage : class;
}

/// <summary>
/// MQTT-specific publisher with additional publishing options.
/// </summary>
public interface IMqttPublisher : IMessagePublisher
{
    /// <summary>
    /// Publishes a message with MQTT-specific options.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="topic">The topic to publish to.</param>
    /// <param name="message">The message to publish.</param>
    /// <param name="options">MQTT-specific publish options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the publish operation.</returns>
    Task PublishAsync<TMessage>(
        string topic,
        TMessage message,
        MqttPublishOptions options,
        CancellationToken cancellationToken = default)
        where TMessage : class;
}

/// <summary>
/// MQTT-specific publish options.
/// </summary>
public sealed class MqttPublishOptions
{
    /// <summary>
    /// Gets or sets the quality of service level.
    /// </summary>
    public QualityOfService QualityOfService { get; set; } = QualityOfService.AtLeastOnce;

    /// <summary>
    /// Gets or sets whether the message should be retained by the broker.
    /// </summary>
    public bool Retain { get; set; }

    /// <summary>
    /// Gets or sets the message expiry interval in seconds.
    /// Only applicable for MQTT 5.0.
    /// </summary>
    public uint? MessageExpiryIntervalSeconds { get; set; }

    /// <summary>
    /// Gets or sets the content type of the payload.
    /// Only applicable for MQTT 5.0.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the correlation data for request-response patterns.
    /// Only applicable for MQTT 5.0.
    /// </summary>
    public byte[]? CorrelationData { get; set; }

    /// <summary>
    /// Gets or sets the response topic for request-response patterns.
    /// Only applicable for MQTT 5.0.
    /// </summary>
    public string? ResponseTopic { get; set; }

    /// <summary>
    /// Gets or sets user properties.
    /// Only applicable for MQTT 5.0.
    /// </summary>
    public Dictionary<string, string>? UserProperties { get; set; }
}

