namespace Sitas.Edge.Core.Enums;

/// <summary>
/// Defines the quality of service levels for message delivery.
/// Aligned with MQTT QoS levels but abstracted for multi-protocol support.
/// </summary>
public enum QualityOfService
{
    /// <summary>
    /// At most once delivery. Message may be lost. Fire and forget.
    /// MQTT QoS 0 equivalent.
    /// </summary>
    AtMostOnce = 0,

    /// <summary>
    /// At least once delivery. Message is guaranteed to arrive but may be duplicated.
    /// MQTT QoS 1 equivalent.
    /// </summary>
    AtLeastOnce = 1,

    /// <summary>
    /// Exactly once delivery. Message is guaranteed to arrive exactly once.
    /// MQTT QoS 2 equivalent.
    /// </summary>
    ExactlyOnce = 2
}

