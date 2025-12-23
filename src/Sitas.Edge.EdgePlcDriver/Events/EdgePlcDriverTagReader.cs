using Sitas.Edge.Core.Events;

namespace Sitas.Edge.EdgePlcDriver.Events;

/// <summary>
/// Tag reader implementation for Edge PLC Driver connections.
/// Used by the EventMediator to read tags specified in [EdgePlcDriverRead] attributes.
/// </summary>
public class EdgePlcDriverTagReader : ITagReader
{
    private readonly IEdgePlcDriver _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="EdgePlcDriverTagReader"/> class.
    /// </summary>
    /// <param name="connection">The Edge PLC Driver connection to use for reading.</param>
    public EdgePlcDriverTagReader(IEdgePlcDriver connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <inheritdoc/>
    public async Task<TagReadValue<object?>> ReadTagAsync(
        string tagName,
        Type? valueType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use reflection to call the generic ReadTagAsync method
            var method = typeof(IEdgePlcDriver).GetMethod(nameof(IEdgePlcDriver.ReadTagAsync));
            if (method is null)
            {
                throw new InvalidOperationException("ReadTagAsync method not found on IEdgePlcDriver");
            }

            var effectiveType = valueType ?? typeof(object);
            var genericMethod = method.MakeGenericMethod(effectiveType);

            var task = (Task?)genericMethod.Invoke(_connection, new object[] { tagName, cancellationToken });
            if (task is null)
            {
                return CreateErrorResult(tagName, "ReadTagAsync returned null");
            }

            await task;

            // Get the result from the task
            var resultProperty = task.GetType().GetProperty("Result");
            var result = resultProperty?.GetValue(task);

            if (result is null)
            {
                return CreateErrorResult(tagName, "Result was null");
            }

            // Extract properties from TagValue<T>
            var valueProperty = result.GetType().GetProperty("Value");
            var qualityProperty = result.GetType().GetProperty("Quality");
            var timestampProperty = result.GetType().GetProperty("Timestamp");

            var value = valueProperty?.GetValue(result);
            var quality = qualityProperty?.GetValue(result);
            var timestamp = timestampProperty?.GetValue(result);

            return new TagReadValue<object?>
            {
                TagName = tagName,
                Value = value,
                Quality = MapQuality(quality),
                Timestamp = timestamp is DateTimeOffset dto ? dto : DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult(tagName, ex.Message);
        }
    }

    private static TagReadValue<object?> CreateErrorResult(string tagName, string error)
    {
        return new TagReadValue<object?>
        {
            TagName = tagName,
            Value = null,
            Quality = TagQuality.CommError,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static TagQuality MapQuality(object? quality)
    {
        if (quality is Messages.TagQuality tq)
        {
            return tq switch
            {
                Messages.TagQuality.Good => TagQuality.Good,
                Messages.TagQuality.Uncertain => TagQuality.Uncertain,
                Messages.TagQuality.Bad => TagQuality.Bad,
                Messages.TagQuality.CommError => TagQuality.CommError,
                _ => TagQuality.Bad
            };
        }
        return TagQuality.Bad;
    }
}
