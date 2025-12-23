using Sitas.Edge.Core.Attributes;
using Sitas.Edge.Core.Enums;

namespace Sitas.Edge.Core.Discovery;

/// <summary>
/// Represents a discovered handler registration with its subscription metadata.
/// </summary>
public sealed class HandlerRegistration
{
    /// <summary>
    /// Gets the handler type that was discovered.
    /// </summary>
    public Type HandlerType { get; }

    /// <summary>
    /// Gets the message type the handler processes.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    /// Gets the connection name this handler targets.
    /// </summary>
    public string ConnectionName { get; }

    /// <summary>
    /// Gets the topic pattern the handler subscribes to.
    /// </summary>
    public string Topic { get; }

    /// <summary>
    /// Gets the quality of service level for the subscription.
    /// </summary>
    public QualityOfService QualityOfService { get; }

    /// <summary>
    /// Gets the subscribe attribute that defined this registration.
    /// </summary>
    public SubscribeAttribute Attribute { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerRegistration"/> class.
    /// </summary>
    public HandlerRegistration(
        Type handlerType,
        Type messageType,
        string connectionName,
        string topic,
        QualityOfService qos,
        SubscribeAttribute attribute)
    {
        HandlerType = handlerType ?? throw new ArgumentNullException(nameof(handlerType));
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        ConnectionName = connectionName ?? throw new ArgumentNullException(nameof(connectionName));
        Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        QualityOfService = qos;
        Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
    }
}

