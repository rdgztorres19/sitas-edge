namespace Sitas.Edge.Core.Events;

/// <summary>
/// Container for tag values read based on [AsCommRead] or similar attributes
/// on the event handler class. These values are read once when the event is emitted.
/// </summary>
public class TagReadResults
{
    private readonly Dictionary<string, TagReadValue<object?>> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a tag value by name, cast to the expected type.
    /// </summary>
    /// <typeparam name="T">The expected type of the tag value.</typeparam>
    /// <param name="tagName">The name of the tag.</param>
    /// <returns>The tag value, or default if not found or cannot be cast.</returns>
    public T? Get<T>(string tagName)
    {
        if (_values.TryGetValue(tagName, out var tagValue) && tagValue.Value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// Gets a tag value with full metadata (quality, timestamp, etc.).
    /// </summary>
    /// <typeparam name="T">The expected type of the tag value.</typeparam>
    /// <param name="tagName">The name of the tag.</param>
    /// <returns>The tag value with metadata, or null if not found.</returns>
    public TagReadValue<T>? GetTagValue<T>(string tagName)
    {
        if (_values.TryGetValue(tagName, out var tagValue))
        {
            return new TagReadValue<T>
            {
                TagName = tagValue.TagName,
                Value = tagValue.Value is T typedValue ? typedValue : default!,
                Quality = tagValue.Quality,
                Timestamp = tagValue.Timestamp
            };
        }
        return null;
    }

    /// <summary>
    /// Gets a tag value, throwing if not found.
    /// </summary>
    /// <typeparam name="T">The expected type of the tag value.</typeparam>
    /// <param name="tagName">The name of the tag.</param>
    /// <returns>The tag value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the tag is not found.</exception>
    public T GetRequired<T>(string tagName)
    {
        if (!_values.TryGetValue(tagName, out var tagValue))
        {
            throw new KeyNotFoundException($"Tag '{tagName}' was not found in the read results. " +
                $"Ensure [AsCommRead(\"{tagName}\")] attribute is present on the handler class.");
        }

        if (tagValue.Value is T typedValue)
        {
            return typedValue;
        }

        throw new InvalidCastException(
            $"Tag '{tagName}' value cannot be cast to {typeof(T).Name}. " +
            $"Actual type: {tagValue.Value?.GetType().Name ?? "null"}");
    }

    /// <summary>
    /// Checks if a tag value exists in the results.
    /// </summary>
    /// <param name="tagName">The name of the tag.</param>
    /// <returns>True if the tag exists, false otherwise.</returns>
    public bool Contains(string tagName) => _values.ContainsKey(tagName);

    /// <summary>
    /// Gets all tag names in the results.
    /// </summary>
    public IEnumerable<string> TagNames => _values.Keys;

    /// <summary>
    /// Gets the count of tag values.
    /// </summary>
    public int Count => _values.Count;

    /// <summary>
    /// Checks if there are any tag values in good quality.
    /// </summary>
    public bool AllGoodQuality => _values.Values.All(v => v.Quality == TagQuality.Good);

    /// <summary>
    /// Gets tags that have bad quality.
    /// </summary>
    public IEnumerable<string> BadQualityTags => _values
        .Where(kv => kv.Value.Quality != TagQuality.Good)
        .Select(kv => kv.Key);

    /// <summary>
    /// Adds a tag value to the results. Used internally by the event mediator.
    /// </summary>
    internal void Add(string tagName, TagReadValue<object?> value)
    {
        _values[tagName] = value;
    }

    /// <summary>
    /// Creates an empty TagReadResults instance.
    /// </summary>
    public static TagReadResults Empty { get; } = new();
}

/// <summary>
/// Represents a single tag value read from a data source.
/// </summary>
/// <typeparam name="T">The type of the tag value.</typeparam>
public class TagReadValue<T>
{
    /// <summary>
    /// Gets or sets the name of the tag.
    /// </summary>
    public required string TagName { get; init; }

    /// <summary>
    /// Gets or sets the value read from the tag.
    /// </summary>
    public T Value { get; init; } = default!;

    /// <summary>
    /// Gets or sets the quality of the read.
    /// </summary>
    public TagQuality Quality { get; init; } = TagQuality.Good;

    /// <summary>
    /// Gets or sets the timestamp when the value was read.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Indicates whether the read was successful (good quality).
    /// </summary>
    public bool IsGood => Quality == TagQuality.Good;
}

/// <summary>
/// Represents the quality of a tag read operation.
/// </summary>
public enum TagQuality
{
    /// <summary>
    /// The value was read successfully and is valid.
    /// </summary>
    Good = 0,

    /// <summary>
    /// The value quality is uncertain.
    /// </summary>
    Uncertain = 1,

    /// <summary>
    /// The read failed or the value is invalid.
    /// </summary>
    Bad = 2,

    /// <summary>
    /// A communication error occurred.
    /// </summary>
    CommError = 3
}
