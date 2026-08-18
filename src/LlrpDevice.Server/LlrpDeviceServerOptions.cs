using System.Net;
using LlrpDevice.Abstractions;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace LlrpDevice.Server;

public enum LlrpDeviceServerLifecycleState
{
    Created,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
}

public enum LlrpDeviceConnectionLimitPolicy
{
    RejectAdditional,
    ReplaceExisting,
}

public enum LlrpUnknownVendorParameterBehavior
{
    PreserveAndIgnore,
    Reject,
}

public sealed record LlrpDeviceReportOptions
{
    public TimeSpan ReportInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public int ReportCount { get; init; }
    public bool Repeat { get; init; } = true;
}

public sealed record LlrpDeviceServerOptions
{
    public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;
    public int Port { get; init; }
    public LlrpProtocolVersion ProtocolVersion { get; init; } = LlrpProtocolVersion.Version101;
    public int MaximumClientConnections { get; init; } = 1;
    public LlrpDeviceConnectionLimitPolicy ConnectionLimitPolicy { get; init; } =
        LlrpDeviceConnectionLimitPolicy.RejectAdditional;
    public TimeSpan IdleTimeout { get; init; } = Timeout.InfiniteTimeSpan;
    public TimeSpan FrameAssemblyTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public uint MaximumFrameLength { get; init; } = 1_048_576;
    public bool UseTcpKeepAlive { get; init; } = true;
    public TimeSpan? KeepAliveInterval { get; init; }
    public int ReportBufferCapacity { get; init; } = 1024;
    public LlrpDeviceReportOptions Reports { get; init; } = new();
    public LlrpUnknownVendorParameterBehavior UnknownVendorParameterBehavior { get; init; } =
        LlrpUnknownVendorParameterBehavior.PreserveAndIgnore;
    public bool UseStrictStandardInventoryProfile { get; init; }
    /// <summary>
    /// Allows <c>DISABLE_ROSPEC</c> to implicitly stop an Active ROSpec before moving it to Disabled.
    /// This is a compatibility mode for readers that accept the shortcut; it is disabled by default so
    /// protocol tests continue to catch clients that omit <c>STOP_ROSPEC</c>.
    /// </summary>
    public bool AllowImplicitStopOnDisable { get; init; }
    public IReadOnlySet<ushort> DropResponseForMessageTypes { get; init; } = new HashSet<ushort>();
    public IReadOnlyDictionary<ushort, LlrpDeviceServerErrorResponse> ErrorResponseForMessageTypes { get; init; } =
        new Dictionary<ushort, LlrpDeviceServerErrorResponse>();
    public IReadOnlySet<ushort> CloseConnectionAfterRequestMessageTypes { get; init; } = new HashSet<ushort>();
    public IReadOnlySet<ushort> TruncateResponseForMessageTypes { get; init; } = new HashSet<ushort>();
    public ILoggerFactory? LoggerFactory { get; init; }
    public ILlrpFrameObserver? FrameObserver { get; init; }
    public IReadOnlyList<ILlrpDeviceProtocolModule> ProtocolModules { get; init; } = [];

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(ListenAddress);
        ArgumentNullException.ThrowIfNull(Reports);
        ArgumentNullException.ThrowIfNull(DropResponseForMessageTypes);
        ArgumentNullException.ThrowIfNull(ErrorResponseForMessageTypes);
        ArgumentNullException.ThrowIfNull(CloseConnectionAfterRequestMessageTypes);
        ArgumentNullException.ThrowIfNull(TruncateResponseForMessageTypes);
        ArgumentNullException.ThrowIfNull(ProtocolModules);
        if (Port is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(Port));
        }

        if (!Enum.IsDefined(ProtocolVersion))
        {
            throw new ArgumentOutOfRangeException(nameof(ProtocolVersion));
        }

        if (MaximumClientConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumClientConnections));
        }

        if (Reports.ReportCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Reports.ReportCount));
        }

        if (ReportBufferCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ReportBufferCapacity));
        }

        ValidatePositiveTimeout(Reports.ReportInterval, nameof(Reports.ReportInterval));
        ValidatePositiveTimeout(FrameAssemblyTimeout, nameof(FrameAssemblyTimeout));
        if (IdleTimeout != Timeout.InfiniteTimeSpan)
        {
            ValidatePositiveTimeout(IdleTimeout, nameof(IdleTimeout));
        }

        if (KeepAliveInterval is TimeSpan keepalive)
        {
            ValidatePositiveTimeout(keepalive, nameof(KeepAliveInterval));
        }

        if (MaximumFrameLength < LlrpMessageHeader.EncodedLength)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumFrameLength));
        }

        foreach (ILlrpDeviceProtocolModule module in ProtocolModules)
        {
            ArgumentNullException.ThrowIfNull(module);
        }
    }

    private static void ValidatePositiveTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMilliseconds(uint.MaxValue - 1d))
        {
            throw new ArgumentOutOfRangeException(name, value, "The timeout must be positive and finite.");
        }
    }
}

public sealed record LlrpDeviceServerErrorResponse
{
    public LlrpDeviceServerErrorResponse(ushort statusCode, string description)
    {
        StatusCode = statusCode;
        Description = description ?? string.Empty;
    }

    public ushort StatusCode { get; }
    public string Description { get; }
}
