namespace Sitas.Edge.Core.Abstractions;

/// <summary>
/// Defines a handler for processing subscription-based messages (polling or push-based).
/// Handlers are discovered automatically via reflection when decorated with subscription attributes
/// like [AsCommSubscribe] or [MqttSubscribe].
/// </summary>
/// <typeparam name="TMessage">The type of message this handler processes.</typeparam>
/// <remarks>
/// For on-demand events (non-subscription), use the Event Mediator pattern
/// (Conduit.Core.Events.IEventMediator + an IEventHandler&lt;TEvent&gt; implementation).
/// </remarks>
public interface IMessageSubscriptionHandler<in TMessage>
    where TMessage : class
{
    /// <summary>
    /// Handles an incoming message with full context including the ability to publish responses.
    /// </summary>
    /// <param name="message">The deserialized message payload.</param>
    /// <param name="context">The message context containing metadata and publisher.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(TMessage message, IMessageContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Alias for <see cref="IMessageSubscriptionHandler{TMessage}"/> for backward compatibility.
/// Use IMessageSubscriptionHandler for new code.
/// </summary>
/// <typeparam name="TMessage">The type of message this handler processes.</typeparam>
[Obsolete("Use IMessageSubscriptionHandler<TMessage> for clarity. This alias is kept for backward compatibility.")]
public interface IMessageHandler<in TMessage> : IMessageSubscriptionHandler<TMessage>
    where TMessage : class
{
}

/// <summary>
/// Marker interface for handlers that don't require a specific message type.
/// Receives raw message data for custom deserialization scenarios.
/// </summary>
public interface IRawMessageHandler
{
    /// <summary>
    /// Handles raw message bytes.
    /// </summary>
    /// <param name="payload">The raw message payload.</param>
    /// <param name="context">The message context.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(ReadOnlyMemory<byte> payload, IMessageContext context, CancellationToken cancellationToken = default);
}

