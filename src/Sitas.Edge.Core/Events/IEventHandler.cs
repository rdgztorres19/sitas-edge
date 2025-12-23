namespace Sitas.Edge.Core.Events;

/// <summary>
/// Defines a handler for processing events triggered by the EventMediator.
/// Unlike IMessageSubscriptionHandler (polling-based), event handlers are invoked
/// on-demand when a user calls EventMediator.EmitAsync().
/// </summary>
/// <typeparam name="TEvent">The type of event data this handler processes.</typeparam>
public interface IEventHandler<in TEvent>
{
    /// <summary>
    /// Handles an event with tag values read from attributes.
    /// Protocol-specific capabilities (publish/read/write) should be obtained via
    /// dependency injection (e.g., inject ISitasEdge and get the desired connection),
    /// rather than being passed as a context parameter.
    /// </summary>
    /// <param name="eventData">The event data sent by the user via EmitAsync.</param>
    /// <param name="tagValues">Values read from tags specified in [AsCommRead] or similar attributes.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(
        TEvent eventData,
        TagReadResults tagValues,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a handler for events that returns a result.
/// </summary>
/// <typeparam name="TEvent">The type of event data this handler processes.</typeparam>
/// <typeparam name="TResult">The type of result returned by this handler.</typeparam>
public interface IEventHandler<in TEvent, TResult>
{
    /// <summary>
    /// Handles an event and returns a result.
    /// </summary>
    /// <param name="eventData">The event data sent by the user via EmitAsync.</param>
    /// <param name="tagValues">Values read from tags specified in [AsCommRead] or similar attributes.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of handling the event.</returns>
    Task<TResult> HandleAsync(
        TEvent eventData,
        TagReadResults tagValues,
        CancellationToken cancellationToken = default);
}
