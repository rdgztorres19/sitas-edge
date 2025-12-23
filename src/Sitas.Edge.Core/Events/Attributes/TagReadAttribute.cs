namespace Sitas.Edge.Core.Events.Attributes;

/// <summary>
/// Base attribute for specifying tags to read when an event is triggered.
/// Protocol-specific attributes should inherit from this class.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public abstract class TagReadAttribute : Attribute
{
    /// <summary>
    /// Gets the connection name for reading this tag.
    /// </summary>
    public string ConnectionName { get; }

    /// <summary>
    /// Gets the name of the tag to read.
    /// </summary>
    public string TagName { get; }

    /// <summary>
    /// Gets or sets the expected type of the tag value.
    /// If not specified, the type will be inferred.
    /// </summary>
    public Type? ValueType { get; set; }

    /// <summary>
    /// Gets or sets an alias for this tag in the TagReadResults.
    /// If not specified, the TagName is used.
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>
    /// Gets or sets whether to continue processing if this tag read fails.
    /// Default is true (continue on failure).
    /// </summary>
    public bool ContinueOnFailure { get; set; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="TagReadAttribute"/> class.
    /// </summary>
    /// <param name="connectionName">The connection name for reading this tag.</param>
    /// <param name="tagName">The name of the tag to read.</param>
    protected TagReadAttribute(string connectionName, string tagName)
    {
        ConnectionName = connectionName ?? throw new ArgumentNullException(nameof(connectionName));
        TagName = tagName ?? throw new ArgumentNullException(nameof(tagName));
    }

    /// <summary>
    /// Gets the key to use in TagReadResults (Alias if specified, otherwise TagName).
    /// </summary>
    public string ResultKey => Alias ?? TagName;
}
