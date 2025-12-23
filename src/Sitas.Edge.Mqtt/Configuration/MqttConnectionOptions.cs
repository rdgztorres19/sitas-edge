namespace Sitas.Edge.Mqtt.Configuration;

/// <summary>
/// Configuration options for an MQTT connection.
/// </summary>
public sealed class MqttConnectionOptions
{
    /// <summary>
    /// Gets or sets the connection name for identification.
    /// </summary>
    public string ConnectionName { get; set; } = "default";

    /// <summary>
    /// Gets or sets the broker host address.
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the broker port.
    /// Default is 1883 for non-TLS, 8883 for TLS.
    /// </summary>
    public int Port { get; set; } = 1883;

    /// <summary>
    /// Gets or sets the client identifier.
    /// If not set, a unique ID will be generated.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the username for authentication.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the password for authentication.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets whether TLS/SSL should be used.
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Gets or sets whether to validate the server certificate.
    /// Set to false for self-signed certificates.
    /// </summary>
    public bool ValidateCertificate { get; set; } = true;

    /// <summary>
    /// Gets or sets the clean session flag.
    /// When true, the broker discards any previous session data.
    /// </summary>
    public bool CleanSession { get; set; } = true;

    /// <summary>
    /// Gets or sets the keep-alive interval in seconds.
    /// </summary>
    public int KeepAliveSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the connection timeout in seconds.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether to enable automatic reconnection.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum retry delay in seconds for reconnection.
    /// </summary>
    public int MaxReconnectDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the MQTT protocol version.
    /// </summary>
    public MqttProtocolVersion ProtocolVersion { get; set; } = MqttProtocolVersion.V500;
}

/// <summary>
/// MQTT protocol versions.
/// </summary>
public enum MqttProtocolVersion
{
    /// <summary>
    /// MQTT 3.1.1
    /// </summary>
    V311,

    /// <summary>
    /// MQTT 5.0
    /// </summary>
    V500
}

