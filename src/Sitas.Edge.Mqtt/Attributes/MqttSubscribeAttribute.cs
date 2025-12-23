using Sitas.Edge.Core.Attributes;
using Sitas.Edge.Core.Enums;

namespace Sitas.Edge.Mqtt.Attributes;

/// <summary>
/// Marks a message handler class to subscribe to an MQTT topic.
/// Multiple attributes can be applied to subscribe to multiple topics.
/// </summary>
/// <remarks>
/// MQTT Topic Wildcards:
/// - Single-level: + (matches exactly one topic level)
/// - Multi-level: # (matches any number of levels, must be last character)
/// 
/// Examples:
/// - "machines/+/heartbeat" matches "machines/123/heartbeat"
/// - "sensors/#" matches "sensors/temp/room1" and "sensors/humidity"
/// </remarks>
/// <example>
/// <code>
/// [MqttSubscribe("default", "machines/+/heartbeat", QualityOfService.AtMostOnce)]
/// public class HeartbeatHandler : IMessageHandler&lt;Heartbeat&gt;
/// {
///     public Task HandleAsync(Heartbeat message, IMessageContext context, CancellationToken ct)
///     {
///         // Handle the heartbeat message
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MqttSubscribeAttribute : SubscribeAttribute
{
    /// <summary>
    /// Gets or sets whether the subscription should receive messages that were published
    /// before the subscription was made (retained messages).
    /// </summary>
    public bool NoLocal { get; set; }

    /// <summary>
    /// Gets or sets whether retained messages should be sent when the subscription is made.
    /// </summary>
    public bool RetainAsPublished { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttSubscribeAttribute"/> class.
    /// </summary>
    /// <param name="connectionName">
    /// The name of the MQTT connection to use. Must match a configured connection name.
    /// </param>
    /// <param name="topic">
    /// The MQTT topic pattern to subscribe to. Supports + and # wildcards.
    /// </param>
    /// <param name="qos">
    /// The quality of service level for the subscription.
    /// Defaults to AtLeastOnce (QoS 1).
    /// </param>
    public MqttSubscribeAttribute(
        string connectionName,
        string topic,
        QualityOfService qos = QualityOfService.AtLeastOnce)
        : base(connectionName, topic, qos)
    {
    }
}

