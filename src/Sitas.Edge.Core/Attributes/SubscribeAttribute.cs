using Sitas.Edge.Core.Enums;

namespace Sitas.Edge.Core.Attributes;

/// <summary>
/// Base attribute for marking message handlers with subscription information.
/// Protocol-specific attributes should inherit from this class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public abstract class SubscribeAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the connection this subscription belongs to.
    /// Allows handlers to target specific connections when multiple are configured.
    /// </summary>
    public string ConnectionName { get; }

    /// <summary>
    /// Gets the topic pattern to subscribe to.
    /// Supports wildcards depending on the protocol (e.g., MQTT uses + and #).
    /// </summary>
    public string Topic { get; }

    /// <summary>
    /// Gets the quality of service level for the subscription.
    /// </summary>
    public QualityOfService QualityOfService { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscribeAttribute"/> class.
    /// </summary>
    /// <param name="connectionName">The connection name this subscription targets.</param>
    /// <param name="topic">The topic pattern to subscribe to.</param>
    /// <param name="qos">The quality of service level.</param>
    protected SubscribeAttribute(string connectionName, string topic, QualityOfService qos = QualityOfService.AtLeastOnce)
    {
        ConnectionName = connectionName ?? throw new ArgumentNullException(nameof(connectionName));
        Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        QualityOfService = qos;
    }
}

