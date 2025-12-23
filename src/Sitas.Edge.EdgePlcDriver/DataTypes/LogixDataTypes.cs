using System.Runtime.InteropServices;

namespace Sitas.Edge.EdgePlcDriver.DataTypes;

/// <summary>
/// Provides utilities for working with Logix data types.
/// </summary>
public static class LogixDataTypes
{
    /// <summary>
    /// Maps .NET types to their Logix PLC equivalents.
    /// </summary>
    /// <remarks>
    /// Type mapping table:
    /// - BOOL → Boolean (4 bytes in UDT context)
    /// - SINT → SByte (1 byte)
    /// - INT → Int16 (2 bytes)
    /// - DINT → Int32 (4 bytes)
    /// - LINT → Int64 (8 bytes)
    /// - USINT → Byte (1 byte)
    /// - UINT → UInt16 (2 bytes)
    /// - UDINT → UInt32 (4 bytes)
    /// - ULINT → UInt64 (8 bytes)
    /// - REAL → Single/float (4 bytes)
    /// - LREAL → Double (8 bytes)
    /// - STRING → LogixString (88 bytes default)
    /// </remarks>
    public static Type GetDotNetType(LogixTagType tagType)
    {
        return tagType switch
        {
            LogixTagType.BOOL => typeof(bool),
            LogixTagType.SINT => typeof(sbyte),
            LogixTagType.INT => typeof(short),
            LogixTagType.DINT => typeof(int),
            LogixTagType.LINT => typeof(long),
            LogixTagType.USINT => typeof(byte),
            LogixTagType.UINT => typeof(ushort),
            LogixTagType.UDINT => typeof(uint),
            LogixTagType.ULINT => typeof(ulong),
            LogixTagType.REAL => typeof(float),
            LogixTagType.LREAL => typeof(double),
            LogixTagType.STRING => typeof(LogixString),
            _ => typeof(object)
        };
    }

    /// <summary>
    /// Gets the Logix tag type from a .NET type.
    /// </summary>
    public static LogixTagType GetLogixType(Type dotNetType)
    {
        if (dotNetType == typeof(bool)) return LogixTagType.BOOL;
        if (dotNetType == typeof(sbyte)) return LogixTagType.SINT;
        if (dotNetType == typeof(short)) return LogixTagType.INT;
        if (dotNetType == typeof(int)) return LogixTagType.DINT;
        if (dotNetType == typeof(long)) return LogixTagType.LINT;
        if (dotNetType == typeof(byte)) return LogixTagType.USINT;
        if (dotNetType == typeof(ushort)) return LogixTagType.UINT;
        if (dotNetType == typeof(uint)) return LogixTagType.UDINT;
        if (dotNetType == typeof(ulong)) return LogixTagType.ULINT;
        if (dotNetType == typeof(float)) return LogixTagType.REAL;
        if (dotNetType == typeof(double)) return LogixTagType.LREAL;
        if (dotNetType == typeof(string) || dotNetType == typeof(LogixString)) return LogixTagType.STRING;

        // For UDTs and complex types
        return LogixTagType.UDT;
    }

    /// <summary>
    /// Gets the size in bytes of a Logix data type.
    /// </summary>
    public static int GetTypeSize(LogixTagType tagType)
    {
        return tagType switch
        {
            LogixTagType.BOOL => 4, // BOOL is 4 bytes in UDT context
            LogixTagType.SINT => 1,
            LogixTagType.INT => 2,
            LogixTagType.DINT => 4,
            LogixTagType.LINT => 8,
            LogixTagType.USINT => 1,
            LogixTagType.UINT => 2,
            LogixTagType.UDINT => 4,
            LogixTagType.ULINT => 8,
            LogixTagType.REAL => 4,
            LogixTagType.LREAL => 8,
            LogixTagType.STRING => 88, // Default string size: 4 (length) + 82 (data) + 2 (padding)
            _ => 0
        };
    }
}

/// <summary>
/// Logix PLC data types.
/// </summary>
public enum LogixTagType
{
    /// <summary>Unknown or auto-detect type.</summary>
    Unknown = 0,

    /// <summary>Boolean (BOOL) - 1 bit, but 4 bytes in UDT.</summary>
    BOOL,

    /// <summary>Signed 8-bit integer (SINT).</summary>
    SINT,

    /// <summary>Signed 16-bit integer (INT).</summary>
    INT,

    /// <summary>Signed 32-bit integer (DINT).</summary>
    DINT,

    /// <summary>Signed 64-bit integer (LINT).</summary>
    LINT,

    /// <summary>Unsigned 8-bit integer (USINT).</summary>
    USINT,

    /// <summary>Unsigned 16-bit integer (UINT).</summary>
    UINT,

    /// <summary>Unsigned 32-bit integer (UDINT).</summary>
    UDINT,

    /// <summary>Unsigned 64-bit integer (ULINT).</summary>
    ULINT,

    /// <summary>32-bit floating point (REAL).</summary>
    REAL,

    /// <summary>64-bit floating point (LREAL).</summary>
    LREAL,

    /// <summary>String type.</summary>
    STRING,

    /// <summary>User-Defined Type (structure).</summary>
    UDT,

    /// <summary>Pre-Defined Type (built-in structure like TIMER, COUNTER, PID).</summary>
    PDT
}

/// <summary>
/// Base class for User-Defined Types (UDTs) with proper structure layout.
/// Inherit from this class and apply [StructLayout(LayoutKind.Sequential)] attribute.
/// </summary>
/// <remarks>
/// Guidelines for creating UDT classes:
/// 1. The class MUST be decorated with [StructLayout(LayoutKind.Sequential)]
/// 2. Use PUBLIC FIELDS (not properties) for UDT members
/// 3. Fields must be in the EXACT ORDER as defined in the RSLogix/Studio 5000 UDT
/// 4. Use the correct .NET types that map to Logix types (see LogixDataTypes)
/// 5. Initialize array fields and nested UDT fields in the declaration
/// 
/// Example:
/// <code>
/// [StructLayout(LayoutKind.Sequential)]
/// public class MyUDT
/// {
///     public Boolean enabled;           // BOOL
///     public Int32 count;               // DINT
///     public Single temperature;        // REAL
///     public Int16[] hourlyData = new Int16[24];  // INT[24]
///     public LogixString description = new();     // STRING
/// }
/// </code>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public abstract class LogixUdtBase
{
    // Marker class for UDTs
}

/// <summary>
/// TIMER predefined type structure.
/// Equivalent to the TIMER type in Allen-Bradley PLCs.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class LogixTimer
{
    /// <summary>Preset value in milliseconds.</summary>
    public Int32 PRE;

    /// <summary>Accumulated value in milliseconds.</summary>
    public Int32 ACC;

    /// <summary>Timer enable bit.</summary>
    public Boolean EN;

    /// <summary>Timer timing bit.</summary>
    public Boolean TT;

    /// <summary>Timer done bit.</summary>
    public Boolean DN;
}

/// <summary>
/// COUNTER predefined type structure.
/// Equivalent to the COUNTER type in Allen-Bradley PLCs.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class LogixCounter
{
    /// <summary>Preset value.</summary>
    public Int32 PRE;

    /// <summary>Accumulated value.</summary>
    public Int32 ACC;

    /// <summary>Count up enable bit.</summary>
    public Boolean CU;

    /// <summary>Count down enable bit.</summary>
    public Boolean CD;

    /// <summary>Done bit.</summary>
    public Boolean DN;

    /// <summary>Overflow bit.</summary>
    public Boolean OV;

    /// <summary>Underflow bit.</summary>
    public Boolean UN;
}

/// <summary>
/// CONTROL predefined type structure.
/// Used for array instructions (FSC, SQO, SQI, etc.).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class LogixControl
{
    /// <summary>Length of the array.</summary>
    public Int32 LEN;

    /// <summary>Current position.</summary>
    public Int32 POS;

    /// <summary>Enable bit.</summary>
    public Boolean EN;

    /// <summary>Done bit.</summary>
    public Boolean DN;

    /// <summary>Error bit.</summary>
    public Boolean ER;

    /// <summary>Unload bit.</summary>
    public Boolean UL;

    /// <summary>Inhibit bit.</summary>
    public Boolean IN;

    /// <summary>Found bit.</summary>
    public Boolean FD;
}
