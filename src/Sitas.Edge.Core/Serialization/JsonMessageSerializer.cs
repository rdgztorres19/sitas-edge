using System.Text;
using System.Text.Json;
using Sitas.Edge.Core.Abstractions;

namespace Sitas.Edge.Core.Serialization;

/// <summary>
/// Default JSON-based message serializer using System.Text.Json.
/// </summary>
public sealed class JsonMessageSerializer : IMessageSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Gets the default serializer instance with sensible defaults.
    /// </summary>
    public static JsonMessageSerializer Default { get; } = new(CreateDefaultOptions());

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonMessageSerializer"/> class.
    /// </summary>
    public JsonMessageSerializer() : this(CreateDefaultOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonMessageSerializer"/> class with custom options.
    /// </summary>
    /// <param name="options">The JSON serializer options.</param>
    public JsonMessageSerializer(JsonSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.SerializeToUtf8Bytes(message, _options);
    }

    /// <inheritdoc />
    public TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> data) where TMessage : class
    {
        return JsonSerializer.Deserialize<TMessage>(data.Span, _options)
            ?? throw new InvalidOperationException($"Failed to deserialize message to type {typeof(TMessage).Name}");
    }

    /// <inheritdoc />
    public object Deserialize(ReadOnlyMemory<byte> data, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.Deserialize(data.Span, type, _options)
            ?? throw new InvalidOperationException($"Failed to deserialize message to type {type.Name}");
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }
}

