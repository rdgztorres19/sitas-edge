namespace Sitas.Edge.Core.Abstractions;

/// <summary>
/// Provides message serialization and deserialization capabilities.
/// Implement this interface to customize how messages are converted to/from bytes.
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Serializes a message to bytes.
    /// </summary>
    /// <typeparam name="TMessage">The type of message to serialize.</typeparam>
    /// <param name="message">The message to serialize.</param>
    /// <returns>The serialized bytes.</returns>
    ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message) where TMessage : class;

    /// <summary>
    /// Deserializes bytes to a message.
    /// </summary>
    /// <typeparam name="TMessage">The type of message to deserialize to.</typeparam>
    /// <param name="data">The bytes to deserialize.</param>
    /// <returns>The deserialized message.</returns>
    TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> data) where TMessage : class;

    /// <summary>
    /// Deserializes bytes to a message of the specified type.
    /// </summary>
    /// <param name="data">The bytes to deserialize.</param>
    /// <param name="type">The target type.</param>
    /// <returns>The deserialized message.</returns>
    object Deserialize(ReadOnlyMemory<byte> data, Type type);
}

