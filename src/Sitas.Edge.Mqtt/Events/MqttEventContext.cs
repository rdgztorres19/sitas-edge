using Sitas.Edge.Core.Events;
using Sitas.Edge.Core.Enums;

namespace Sitas.Edge.Mqtt.Events;

/// <summary>
/// MQTT-specific event context implementation.
/// Provides the ability to publish messages to MQTT topics during event handling.
/// </summary>
public class MqttEventContext : IMqttEventContext
{
    private readonly Dictionary<string, object?> _metadata = new();
    private readonly IMqttPublisher _publisher;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttEventContext"/> class.
    /// </summary>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="publisher">The MQTT publisher to use for publishing messages.</param>
    public MqttEventContext(string eventName, IMqttPublisher publisher)
    {
        EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public string EventName { get; }

    /// <inheritdoc/>
    public DateTimeOffset Timestamp { get; }

    /// <inheritdoc/>
    public string? CorrelationId { get; init; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?> Metadata => _metadata;

    /// <inheritdoc/>
    public void SetMetadata(string key, object? value)
    {
        _metadata[key] = value;
    }

    /// <inheritdoc/>
    public Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken = default)
        where T : class
    {
        return _publisher.PublishAsync(topic, message, QualityOfService.AtLeastOnce, false, cancellationToken);
    }
}
