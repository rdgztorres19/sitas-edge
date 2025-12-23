namespace Sitas.Edge.Core.Events.Attributes;

/// <summary>
/// Marks a class as an event handler for a specific event name.
/// The handler will be invoked when EventMediator.EmitAsync() is called with the matching event name.
/// </summary>
/// <example>
/// <code>
/// [Event("GetMachineStatus")]
/// public class GetMachineStatusHandler : IEventHandler&lt;MachineRequest&gt;
/// {
///     public Task HandleAsync(MachineRequest request, TagReadResults tags, IEventContext context, CancellationToken ct)
///     {
///         // Handle the event
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class EventAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the event this handler responds to.
    /// </summary>
    public string EventName { get; }

    /// <summary>
    /// Gets or sets the priority of this handler. Higher priority handlers execute first.
    /// Default is 0.
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Gets or sets whether this handler should be executed asynchronously without waiting.
    /// Default is false (wait for completion).
    /// </summary>
    public bool FireAndForget { get; set; } = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventAttribute"/> class.
    /// </summary>
    /// <param name="eventName">The name of the event this handler responds to.</param>
    public EventAttribute(string eventName)
    {
        EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
    }
}
