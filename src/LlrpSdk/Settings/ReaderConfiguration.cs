using Keepalive = LlrpNet.Protocol.Messages.V1_0_1.KEEPALIVE;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using System;
using System.Collections.Generic;

namespace LlrpSdk;

/// <summary>
/// Represents the high-level, version-independent configuration of an LLRP Reader.
/// </summary>
public sealed record ReaderConfiguration
{
    /// <summary>Gets whether the reader holds events and reports while the client reconnects.</summary>
    public bool HoldEventsAndReportsUponReconnect { get; init; }

    /// <summary>Gets typed vendor configuration values projected by active settings contributors.</summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    /// <summary>
    /// Gets the keepalive configuration.
    /// </summary>
    public KeepaliveConfiguration Keepalive { get; init; } = new();

    /// <summary>
    /// Gets the list of individual antenna configuration settings.
    /// </summary>
    public IReadOnlyList<AntennaConfigurationSettings> Antennas { get; init; } = Array.Empty<AntennaConfigurationSettings>();

    /// <summary>
    /// Gets the GPO write data configurations.
    /// </summary>
    public IReadOnlyList<GpoConfiguration> Gpos { get; init; } = Array.Empty<GpoConfiguration>();

    /// <summary>
    /// Gets the GPI port current states. This is typically read-only status.
    /// </summary>
    public IReadOnlyList<GpiStatus> Gpis { get; init; } = Array.Empty<GpiStatus>();

    /// <summary>
    /// Gets the reader event notification configuration settings.
    /// </summary>
    public EventNotificationConfiguration Events { get; init; } = new();
}

/// <summary>
/// Describes keepalive configuration parameters.
/// </summary>
public sealed record KeepaliveConfiguration
{
    /// <summary>
    /// Gets the keepalive trigger type.
    /// </summary>
    public KeepaliveTriggerType TriggerType { get; init; } = KeepaliveTriggerType.None;

    /// <summary>
    /// Gets the periodic keepalive trigger value (interval) in milliseconds.
    /// </summary>
    public uint IntervalMs { get; init; }
}

/// <summary>
/// Specifies the keepalive trigger types.
/// </summary>
public enum KeepaliveTriggerType
{
    /// <summary>No keepalives are sent.</summary>
    None = 0,

    /// <summary>Keepalives are sent periodically.</summary>
    Periodic = 1
}

/// <summary>
/// Describes configuration settings for a single reader antenna.
/// </summary>
public sealed record AntennaConfigurationSettings
{
    /// <summary>
    /// Gets the antenna identifier.
    /// </summary>
    public ushort AntennaId { get; init; }

    /// <summary>
    /// Gets whether the antenna is connected. (Read-only status retrieved from reader properties).
    /// </summary>
    public bool? IsConnected { get; init; }

    /// <summary>
    /// Gets the gain of the antenna in dB. (Read-only status retrieved from reader properties).
    /// </summary>
    public short? Gain { get; init; }

    /// <summary>
    /// Gets the index of the transmit power level table to use.
    /// </summary>
    public ushort? TransmitPowerIndex { get; init; }

    /// <summary>
    /// Gets the index of the receiver sensitivity table to use.
    /// </summary>
    public ushort? ReceiverSensitivityIndex { get; init; }

    /// <summary>
    /// Gets the channel index to use.
    /// </summary>
    public ushort? ChannelIndex { get; init; }
}

/// <summary>
/// Describes configuration for one GPO port.
/// </summary>
public sealed record GpoConfiguration
{
    /// <summary>
    /// Gets the GPO port number.
    /// </summary>
    public ushort GpoPortNumber { get; init; }

    /// <summary>
    /// Gets the state data of the GPO port (High = true, Low = false).
    /// </summary>
    public bool GpoData { get; init; }
}

/// <summary>
/// Describes the current status of one GPI port.
/// </summary>
public sealed record GpiStatus
{
    /// <summary>
    /// Gets the GPI port number.
    /// </summary>
    public ushort GpiPortNumber { get; init; }

    /// <summary>
    /// Gets whether the GPI port is configured.
    /// </summary>
    public bool Configured { get; init; }

    /// <summary>
    /// Gets the current state of the GPI port.
    /// </summary>
    public GpiState State { get; init; }
}

/// <summary>
/// Specifies the state of a GPI port.
/// </summary>
public enum GpiState
{
    /// <summary>Port is in Low state.</summary>
    Low = 0,

    /// <summary>Port is in High state.</summary>
    High = 1,

    /// <summary>Port state is unknown.</summary>
    Unknown = 2
}

/// <summary>
/// Describes reader event notification configurations.
/// </summary>
public sealed record EventNotificationConfiguration
{
    /// <summary>Whether channel hopping event notification is enabled.</summary>
    public bool HoppingEventEnabled { get; init; }

    /// <summary>Whether GPI event notification is enabled.</summary>
    public bool GpiEventEnabled { get; init; }

    /// <summary>Whether ROSpec event notification is enabled.</summary>
    public bool RoSpecEventEnabled { get; init; }

    /// <summary>Whether report buffer warning event notification is enabled.</summary>
    public bool ReportBufferWarningEnabled { get; init; }

    /// <summary>Whether reader exception event notification is enabled.</summary>
    public bool ReaderExceptionEventEnabled { get; init; }

    /// <summary>Whether RF survey event notification is enabled.</summary>
    public bool RfSurveyEventEnabled { get; init; }

    /// <summary>Whether AISpec event notification is enabled.</summary>
    public bool AiSpecEventEnabled { get; init; }

    /// <summary>Whether antenna event notification is enabled.</summary>
    public bool AntennaEventEnabled { get; init; }

    /// <summary>Whether connection attempt event notification is enabled.</summary>
    public bool ConnectionAttemptEventEnabled { get; init; }

    /// <summary>Whether connection close event notification is enabled.</summary>
    public bool ConnectionCloseEventEnabled { get; init; }
}
