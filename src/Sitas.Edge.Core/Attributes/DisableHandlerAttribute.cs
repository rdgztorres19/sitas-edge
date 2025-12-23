namespace Sitas.Edge.Core.Attributes;

/// <summary>
/// When applied to a message handler, prevents it from being discovered and registered
/// during the automatic handler discovery process.
/// </summary>
/// <remarks>
/// This attribute is useful for temporarily disabling handlers during development
/// or for conditionally excluding handlers without removing them from the codebase.
/// </remarks>
/// <example>
/// <code>
/// [DisableHandler]
/// [MqttSubscribe("mqtt", "my/topic")]
/// public class MyTemporarilyDisabledHandler : IMessageSubscriptionHandler&lt;MyMessage&gt;
/// {
///     // Handler implementation
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DisableHandlerAttribute : Attribute
{
}
