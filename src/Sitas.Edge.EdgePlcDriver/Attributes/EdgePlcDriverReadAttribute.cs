using Sitas.Edge.Core.Events.Attributes;

namespace Sitas.Edge.EdgePlcDriver.Attributes;

/// <summary>
/// Specifies a PLC tag to read when an event is triggered.
/// The tag is read ONCE when the event is emitted, not continuously polled.
/// </summary>
/// <remarks>
/// Use this attribute on event handlers (classes with [Event] attribute) to automatically
/// read PLC tags before the handler is invoked. The values are available in TagReadResults.
/// </remarks>
/// <example>
/// <code>
/// [Event("GetMachineStatus")]
/// [EdgePlcDriverRead("plc1", "Machine_Status")]
/// [EdgePlcDriverRead("plc1", "Temperature", typeof(float))]
/// public class GetMachineStatusHandler : IEventHandler&lt;MachineRequest&gt;
/// {
///     public Task HandleAsync(MachineRequest request, TagReadResults tags, CancellationToken ct)
///     {
///         var status = tags.Get&lt;MachineStatusUdt&gt;("Machine_Status");
///         var temp = tags.Get&lt;float&gt;("Temperature");
///         // ...
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class EdgePlcDriverReadAttribute : TagReadAttribute
{
    /// <summary>
    /// Gets or sets the number of array elements to read.
    /// Only applicable for array tags. Default is 1.
    /// </summary>
    public int Elements { get; set; } = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="EdgePlcDriverReadAttribute"/> class.
    /// </summary>
    /// <param name="connectionName">The name of the PLC driver (e.g., "plc1").</param>
    /// <param name="tagName">The name of the PLC tag to read.</param>
    public EdgePlcDriverReadAttribute(string connectionName, string tagName)
        : base(connectionName, tagName)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EdgePlcDriverReadAttribute"/> class
    /// with an explicit value type.
    /// </summary>
    /// <param name="connectionName">The name of the PLC driver (e.g., "plc1").</param>
    /// <param name="tagName">The name of the PLC tag to read.</param>
    /// <param name="valueType">The expected type of the tag value.</param>
    public EdgePlcDriverReadAttribute(string connectionName, string tagName, Type valueType)
        : base(connectionName, tagName)
    {
        ValueType = valueType;
    }
}
