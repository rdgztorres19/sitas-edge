namespace Sitas.Edge.EdgePlcDriver.Messages;

/// <summary>
/// Represents a PLC tag value with metadata.
/// </summary>
/// <typeparam name="T">The type of the tag value.</typeparam>
public sealed class TagValue<T>
{
    /// <summary>
    /// Gets or sets the tag name.
    /// </summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current value of the tag.
    /// </summary>
    public T Value { get; set; } = default!;

    /// <summary>
    /// Gets or sets the previous value of the tag (if available).
    /// </summary>
    public T? PreviousValue { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the value was read from the PLC.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the quality of the tag value.
    /// </summary>
    public TagQuality Quality { get; set; } = TagQuality.Good;

    /// <summary>
    /// Gets a value indicating whether this is the first read (no previous value).
    /// </summary>
    public bool IsInitialRead => PreviousValue is null;

    /// <summary>
    /// Gets a value indicating whether the value has changed from the previous read.
    /// </summary>
    public bool HasChanged => !EqualityComparer<T>.Default.Equals(Value, PreviousValue);
}

/// <summary>
/// Non-generic base class for tag values.
/// </summary>
public abstract class TagValueBase
{
    /// <summary>
    /// Gets or sets the tag name.
    /// </summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the value was read from the PLC.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the quality of the tag value.
    /// </summary>
    public TagQuality Quality { get; set; } = TagQuality.Good;

    /// <summary>
    /// Gets the value as an object.
    /// </summary>
    public abstract object? RawValue { get; }
}

/// <summary>
/// Quality indicator for tag values.
/// </summary>
public enum TagQuality
{
    /// <summary>
    /// Value is good and reliable.
    /// </summary>
    Good = 0,

    /// <summary>
    /// Value quality is uncertain.
    /// </summary>
    Uncertain = 1,

    /// <summary>
    /// Value is bad or unavailable.
    /// </summary>
    Bad = 2,

    /// <summary>
    /// Communication error occurred.
    /// </summary>
    CommError = 3,

    /// <summary>
    /// Tag was not found in the PLC.
    /// </summary>
    NotFound = 4
}
