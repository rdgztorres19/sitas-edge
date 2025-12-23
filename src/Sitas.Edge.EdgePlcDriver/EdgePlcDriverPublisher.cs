using Microsoft.Extensions.Logging;
using ABLogix = AutomatedSolutions.ASCommStd.AB.Logix;
using Sitas.Edge.Core.Abstractions;
using Sitas.Edge.Core.Enums;

namespace Sitas.Edge.EdgePlcDriver;

/// <summary>
/// Publisher implementation for writing tags to PLCs via Edge PLC Driver.
/// </summary>
/// <remarks>
/// Supports writing:
/// - Primitive values (BOOL, INT, DINT, REAL, etc.)
/// - Arrays
/// - User-Defined Types (UDTs/structures)
/// </remarks>
internal sealed class EdgePlcDriverPublisher : IEdgePlcDriverPublisher
{
    private readonly ILogger _logger;

    // ASComm objects for direct writes
    private ABLogix.Net.Channel? _channel;
    private ABLogix.Device? _device;
    private ABLogix.Group? _writeGroup;

    private Func<string, object, CancellationToken, Task>? _writeTagFunc;

    public EdgePlcDriverPublisher(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets the ASComm IoT channel for direct writes.
    /// </summary>
    internal void SetChannel(ABLogix.Net.Channel? channel)
    {
        _channel = channel;
    }

    /// <summary>
    /// Sets the ASComm IoT device for direct writes.
    /// </summary>
    internal void SetDevice(ABLogix.Device? device)
    {
        _device = device;

        // Create a separate write group for publish operations
        if (_device is not null)
        {
            _writeGroup = new ABLogix.Group(false, 100);
            _device.Groups.Add(_writeGroup);
        }
    }

    /// <summary>
    /// Sets the write function (injected by EdgePlcDriver after initialization).
    /// </summary>
    internal void SetWriteFunction(Func<string, object, CancellationToken, Task> writeFunc)
    {
        _writeTagFunc = writeFunc;
    }

    public Task PublishAsync<TMessage>(
        string topic,
        TMessage message,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        bool retain = false,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        // For IMessagePublisher compatibility, treat topic as tag name
        // QoS and retain are not applicable to PLC tag writes
        return WriteTagAsync(topic, message, cancellationToken);
    }

    public Task PublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        QualityOfService qos = QualityOfService.AtLeastOnce,
        bool retain = false,
        CancellationToken cancellationToken = default)
    {
        // Raw bytes require knowing the tag type
        // Attempt to interpret as string for simple cases
        _logger.LogWarning(
            "Raw byte publish to tag '{TagName}' - consider using WriteTagAsync with typed value instead",
            topic);

        // Convert bytes to string and write
        var stringValue = System.Text.Encoding.UTF8.GetString(payload.Span);
        return WriteTagAsync(topic, stringValue, cancellationToken);
    }

    public async Task WriteTagAsync<T>(string tagName, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagName);

        _logger.LogDebug("✏️ Publisher writing to tag '{TagName}'", tagName);

        try
        {
            // Use injected write function if available (goes through connection)
            if (_writeTagFunc is not null)
            {
                await _writeTagFunc(tagName, value!, cancellationToken).ConfigureAwait(false);
                return;
            }

            // Direct write using ASComm objects
            if (_device is null || _writeGroup is null)
            {
                throw new InvalidOperationException(
                    "Publisher is not initialized. Ensure the connection is established before writing tags.");
            }

            await Task.Run(() =>
            {
                var item = new ABLogix.Item($"write_{tagName}", tagName);
                _writeGroup.Items.Add(item);

                try
                {
                    // Determine how to write based on value type
                    if (value is Array array)
                    {
                        var values = new object[array.Length];
                        array.CopyTo(values, 0);
                        item.Write(values);
                    }
                    else if (IsStructuredType(value))
                    {
                        // UDT/Struct write
                        item.Write(value);
                    }
                    else
                    {
                        // Simple value write
                        item.Write(new object[] { value! });
                    }

                    _logger.LogDebug("✏️ Successfully wrote to tag '{TagName}'", tagName);
                }
                finally
                {
                    _writeGroup.Items.Remove(item);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to write to tag '{TagName}'", tagName);
            throw;
        }
    }

    public async Task WriteTagsAsync(
        IReadOnlyDictionary<string, object> tagValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagValues);

        foreach (var (tagName, value) in tagValues)
        {
            await WriteTagAsync(tagName, value, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes a UDT/structure value to a tag.
    /// </summary>
    /// <typeparam name="T">The UDT class type (must have [StructLayout(LayoutKind.Sequential)]).</typeparam>
    /// <param name="tagName">The tag name in the PLC.</param>
    /// <param name="structValue">The structured value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteStructuredTagAsync<T>(string tagName, T structValue, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(tagName);
        ArgumentNullException.ThrowIfNull(structValue);

        _logger.LogDebug("✏️ Writing UDT to tag '{TagName}'", tagName);

        try
        {
            if (_device is null || _writeGroup is null)
            {
                throw new InvalidOperationException(
                    "Publisher is not initialized. Ensure the connection is established before writing tags.");
            }

            await Task.Run(() =>
            {
                var item = new ABLogix.Item($"write_struct_{tagName}", tagName);
                _writeGroup.Items.Add(item);

                try
                {
                    item.Write(structValue);
                    _logger.LogDebug("✏️ Successfully wrote UDT to tag '{TagName}'", tagName);
                }
                finally
                {
                    _writeGroup.Items.Remove(item);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to write UDT to tag '{TagName}'", tagName);
            throw;
        }
    }

    /// <summary>
    /// Writes an array of values to a tag.
    /// </summary>
    public async Task WriteArrayAsync<T>(string tagName, T[] values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagName);
        ArgumentNullException.ThrowIfNull(values);

        _logger.LogDebug("✏️ Writing array ({Length} elements) to tag '{TagName}'", values.Length, tagName);

        try
        {
            if (_device is null || _writeGroup is null)
            {
                throw new InvalidOperationException(
                    "Publisher is not initialized. Ensure the connection is established before writing tags.");
            }

            await Task.Run(() =>
            {
                var item = new ABLogix.Item($"write_array_{tagName}", tagName)
                {
                    Elements = values.Length
                };
                _writeGroup.Items.Add(item);

                try
                {
                    var objectArray = values.Cast<object>().ToArray();
                    item.Write(objectArray);
                    _logger.LogDebug("✏️ Successfully wrote array to tag '{TagName}'", tagName);
                }
                finally
                {
                    _writeGroup.Items.Remove(item);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to write array to tag '{TagName}'", tagName);
            throw;
        }
    }

    private static bool IsStructuredType<T>(T value)
    {
        if (value is null) return false;

        var type = value.GetType();

        // Check for StructLayout attribute (indicates UDT/struct for ASComm)
        var hasStructLayout = type.GetCustomAttributes(typeof(System.Runtime.InteropServices.StructLayoutAttribute), true).Length > 0;

        // Also consider classes with public fields (ASComm pattern)
        var hasPublicFields = type.IsClass && !type.IsPrimitive && type != typeof(string) &&
                              type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Length > 0;

        return hasStructLayout || hasPublicFields;
    }
}
