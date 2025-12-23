using System.Runtime.InteropServices;
using System.Text;

namespace Sitas.Edge.EdgePlcDriver.DataTypes;

/// <summary>
/// Represents a Logix STRING type for PLC communication.
/// Equivalent to the native STRING type in Allen-Bradley PLCs.
/// </summary>
/// <remarks>
/// The LOGIX_STRING structure consists of:
/// - 4 bytes (Int32) for string length
/// - 82 bytes for string data (default capacity)
/// - 2 bytes padding (total 88 bytes)
/// 
/// This class must use [StructLayout(LayoutKind.Sequential)] and public fields
/// to work correctly with ASComm's GetStructuredValues/Write methods.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public class LogixString
{
    /// <summary>
    /// The actual length of the string (not the capacity).
    /// </summary>
    public Int32 stringLength;

    /// <summary>
    /// The string data buffer. Default capacity is 82 bytes.
    /// Change the array dimension for custom string types in RSLogix.
    /// </summary>
    public byte[] stringData = new byte[82];

    /// <summary>
    /// Gets the maximum capacity of this string in characters.
    /// </summary>
    public int Capacity => stringData.Length;

    /// <summary>
    /// Gets or sets the string value.
    /// </summary>
    public string Value
    {
        get => ToString();
        set => SetString(value);
    }

    /// <summary>
    /// Creates a new empty LogixString with default capacity (82 bytes).
    /// </summary>
    public LogixString() { }

    /// <summary>
    /// Creates a new LogixString with a specified capacity.
    /// </summary>
    /// <param name="capacity">The maximum string length in bytes.</param>
    public LogixString(int capacity)
    {
        stringData = new byte[capacity];
    }

    /// <summary>
    /// Creates a new LogixString initialized with a value.
    /// </summary>
    /// <param name="value">The initial string value.</param>
    public LogixString(string value)
    {
        SetString(value);
    }

    /// <summary>
    /// Returns the string value of this LOGIX_STRING.
    /// </summary>
    public override string ToString()
    {
        if (stringLength <= 0 || stringData == null || stringData.Length == 0)
            return string.Empty;

        var length = Math.Min(stringLength, stringData.Length);
        return Encoding.ASCII.GetString(stringData, 0, length);
    }

    /// <summary>
    /// Sets the string value.
    /// </summary>
    /// <param name="value">The string value to set.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the string length exceeds the buffer capacity.
    /// </exception>
    public void SetString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length > stringData.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"String length ({value.Length}) exceeds capacity ({stringData.Length} bytes)");
        }

        // Clear existing data
        Array.Clear(stringData, 0, stringData.Length);

        // Copy new data
        var bytes = Encoding.ASCII.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, stringData, 0, bytes.Length);
        stringLength = bytes.Length;
    }

    /// <summary>
    /// Implicit conversion from LogixString to string.
    /// </summary>
    public static implicit operator string(LogixString logixString)
    {
        return logixString?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Implicit conversion from string to LogixString.
    /// </summary>
    public static implicit operator LogixString(string value)
    {
        return new LogixString(value);
    }
}

/// <summary>
/// Represents a custom-length Logix STRING type.
/// Use this for STRING types with non-standard capacity defined in RSLogix.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class LogixString<TCapacity> where TCapacity : ILogixStringCapacity, new()
{
    private static readonly int _capacity = new TCapacity().Capacity;

    /// <summary>
    /// The actual length of the string.
    /// </summary>
    public Int32 stringLength;

    /// <summary>
    /// The string data buffer.
    /// </summary>
    public byte[] stringData;

    /// <summary>
    /// Creates a new LogixString with the capacity defined by TCapacity.
    /// </summary>
    public LogixString()
    {
        stringData = new byte[_capacity];
    }

    /// <summary>
    /// Gets or sets the string value.
    /// </summary>
    public string Value
    {
        get => ToString();
        set => SetString(value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (stringLength <= 0 || stringData == null || stringData.Length == 0)
            return string.Empty;

        var length = Math.Min(stringLength, stringData.Length);
        return Encoding.ASCII.GetString(stringData, 0, length);
    }

    /// <summary>
    /// Sets the string value.
    /// </summary>
    public void SetString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length > stringData.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"String length ({value.Length}) exceeds capacity ({stringData.Length} bytes)");
        }

        Array.Clear(stringData, 0, stringData.Length);
        var bytes = Encoding.ASCII.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, stringData, 0, bytes.Length);
        stringLength = bytes.Length;
    }
}

/// <summary>
/// Interface for defining custom string capacities.
/// </summary>
public interface ILogixStringCapacity
{
    /// <summary>
    /// Gets the capacity in bytes.
    /// </summary>
    int Capacity { get; }
}

/// <summary>
/// Standard 82-byte string capacity.
/// </summary>
public struct StringCapacity82 : ILogixStringCapacity 
{ 
    /// <summary>
    /// Gets the capacity in bytes (82).
    /// </summary>
    public int Capacity => 82; 
}

/// <summary>
/// 256-byte string capacity.
/// </summary>
public struct StringCapacity256 : ILogixStringCapacity 
{ 
    /// <summary>
    /// Gets the capacity in bytes (256).
    /// </summary>
    public int Capacity => 256; 
}

/// <summary>
/// 512-byte string capacity.
/// </summary>
public struct StringCapacity512 : ILogixStringCapacity 
{ 
    /// <summary>
    /// Gets the capacity in bytes (512).
    /// </summary>
    public int Capacity => 512; 
}
