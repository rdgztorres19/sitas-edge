namespace Sitas.Edge.EdgePlcDriver.Configuration;

/// <summary>
/// Configuration options for an Edge PLC Driver connection.
/// </summary>
/// <remarks>
/// Supports Allen-Bradley PLC families:
/// - ControlLogix
/// - CompactLogix  
/// - GuardPLC
/// - SoftLogix
/// - Micro800
/// </remarks>
public sealed class EdgePlcDriverOptions
{
    /// <summary>
    /// Gets or sets the logical name of this driver.
    /// Used to match handlers decorated with [EdgePlcDriverSubscribe] to this driver.
    /// </summary>
    public string ConnectionName { get; set; } = "default";

    /// <summary>
    /// Gets or sets the IP address of the PLC or ENET/ENBT module.
    /// </summary>
    /// <example>192.168.1.10</example>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the slot number of the CPU in the backplane.
    /// Default is 0 (typically the leftmost slot).
    /// </summary>
    /// <remarks>
    /// For CompactLogix, this is typically 0.
    /// For ControlLogix, this depends on your chassis configuration.
    /// </remarks>
    public int CpuSlot { get; set; } = 0;

    /// <summary>
    /// Gets or sets the backplane number.
    /// Default is 1 (standard for most configurations).
    /// </summary>
    /// <remarks>
    /// Port 1 = Backplane
    /// Port 2 = Ethernet module
    /// </remarks>
    public int Backplane { get; set; } = 1;

    /// <summary>
    /// Gets or sets the PLC model type.
    /// Default is ControlLogix which works for most Logix PLCs.
    /// </summary>
    public PlcModel Model { get; set; } = PlcModel.ControlLogix;

    /// <summary>
    /// Gets or sets the default polling interval in milliseconds for active tag groups.
    /// Can be overridden per-subscription via attributes.
    /// </summary>
    /// <remarks>
    /// Recommended values:
    /// - 50-100ms for fast-changing process data
    /// - 500-1000ms for slow-changing status data
    /// - 1000-5000ms for configuration/diagnostics
    /// </remarks>
    public int DefaultPollingIntervalMs { get; set; } = 100;

    /// <summary>
    /// Gets or sets the connection timeout in seconds.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Gets or sets the transaction timeout in seconds.
    /// Applied to read/write operations.
    /// </summary>
    public int TransactionTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether to automatically reconnect on connection loss.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum delay between reconnection attempts in seconds.
    /// Uses exponential backoff starting from 1 second.
    /// </summary>
    public int MaxReconnectDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether to enable simulation mode.
    /// When true, no actual PLC communication occurs.
    /// </summary>
    public bool SimulateMode { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to notify on remote disconnect.
    /// When true, the connection will fire events if the PLC closes the connection.
    /// </summary>
    public bool NotifyOnRemoteDisconnect { get; set; } = true;

    /// <summary>
    /// Gets the route path in ASComm IoT format: "IP,Backplane,Slot"
    /// </summary>
    /// <remarks>
    /// Example: "192.168.1.10,1,0" means:
    /// - Connect to 192.168.1.10
    /// - Through backplane 1 (port 1)
    /// - To CPU in slot 0
    /// </remarks>
    public string RoutePath => $"{IpAddress},{Backplane},{CpuSlot}";
}

/// <summary>
/// Allen-Bradley PLC model types supported by Edge PLC Driver (via ASComm IoT library).
/// </summary>
public enum PlcModel
{
    /// <summary>
    /// ControlLogix and CompactLogix PLCs (default).
    /// </summary>
    ControlLogix = 0,

    /// <summary>
    /// Micro800 series PLCs (Micro820, Micro830, Micro850, Micro870, Micro880).
    /// </summary>
    Micro800 = 1,

    /// <summary>
    /// SoftLogix emulator.
    /// </summary>
    SoftLogix = 2,

    /// <summary>
    /// GuardPLC safety controller.
    /// </summary>
    GuardPLC = 3
}
