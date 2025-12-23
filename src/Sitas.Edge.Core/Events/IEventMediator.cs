namespace Sitas.Edge.Core.Events;

/// <summary>
/// Mediator for emitting events to registered handlers.
/// Events are processed on-demand (not polling-based) and can trigger
/// tag reads defined via attributes on the handler class.
/// </summary>
public interface IEventMediator
{
    /// <summary>
    /// Emits an event to all handlers registered for the specified event name.
    /// </summary>
    /// <typeparam name="TEvent">The type of event data.</typeparam>
    /// <param name="eventName">The name of the event to emit.</param>
    /// <param name="eventData">The event data to pass to handlers.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EmitAsync<TEvent>(string eventName, TEvent eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits an event without typed data.
    /// </summary>
    /// <param name="eventName">The name of the event to emit.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task EmitAsync(string eventName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits an event and returns a result from the handler.
    /// </summary>
    /// <typeparam name="TEvent">The type of event data.</typeparam>
    /// <typeparam name="TResult">The expected result type.</typeparam>
    /// <param name="eventName">The name of the event to emit.</param>
    /// <param name="eventData">The event data to pass to handlers.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result from the handler.</returns>
    Task<TResult?> EmitAsync<TEvent, TResult>(string eventName, TEvent eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits an event to all handlers and collects all results.
    /// Useful when multiple handlers can respond to the same event.
    /// </summary>
    /// <typeparam name="TEvent">The type of event data.</typeparam>
    /// <typeparam name="TResult">The expected result type.</typeparam>
    /// <param name="eventName">The name of the event to emit.</param>
    /// <param name="eventData">The event data to pass to handlers.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>All results from handlers.</returns>
    Task<IReadOnlyList<TResult>> EmitToAllAsync<TEvent, TResult>(string eventName, TEvent eventData, CancellationToken cancellationToken = default);
}
