using System.Collections.ObjectModel;
using System.Net;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using Microsoft.Extensions.Logging;

namespace LlrpVirtualReader;

/// <summary>Identifies the lifecycle state of one virtual reader host.</summary>
public enum VirtualReaderLifecycleState
{
    Created,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
}

/// <summary>Describes what the host does when a client exceeds its connection limit.</summary>
public enum VirtualReaderConnectionLimitPolicy
{
    RejectAdditional,
    ReplaceExisting,
}

/// <summary>Controls how an unregistered vendor parameter is handled by the standard dispatcher.</summary>
public enum VirtualReaderUnknownVendorParameterBehavior
{
    PreserveAndIgnore,
    Reject,
}

/// <summary>Describes one deterministic tag exposed by a virtual reader.</summary>
public sealed record VirtualTag
{
    /// <summary>Gets the EPC value.</summary>
    public required ReadOnlyMemory<byte> ElectronicProductCode { get; init; }

    /// <summary>Gets optional TID bytes.</summary>
    public ReadOnlyMemory<byte> Tid { get; init; }

    /// <summary>Gets the simulated peak RSSI in dBm.</summary>
    public short PeakRssi { get; init; } = -42;

    /// <summary>Gets the simulated antenna identifier.</summary>
    public ushort AntennaId { get; init; } = 1;

    /// <summary>Gets the simulated channel index.</summary>
    public ushort ChannelIndex { get; init; } = 1;

    /// <summary>Gets the initial User-memory words.</summary>
    public IReadOnlyList<ushort> UserMemory { get; init; } = [0, 0, 0, 0];
}

/// <summary>Provides deterministic tag inventory and C1G2 memory operations.</summary>
public interface IVirtualTagSource
{
    /// <summary>Returns a stable snapshot of tags currently visible to the virtual reader.</summary>
    public IReadOnlyList<VirtualTag> GetTags();

    /// <summary>Reads words from one tag memory bank.</summary>
    public bool TryReadWords(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        int wordPointer,
        int wordCount,
        out IReadOnlyList<ushort> words);

    /// <summary>Writes words to one tag memory bank.</summary>
    public bool TryWriteWords(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        int wordPointer,
        IReadOnlyList<ushort> words);

    /// <summary>Gets a byte representation used for C1G2 tag-selection matching.</summary>
    public bool TryGetMemoryBytes(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        out ReadOnlyMemory<byte> bytes);
}

/// <summary>Stores a fixed set of deterministic virtual tags in memory.</summary>
public sealed class FixedVirtualTagSource : IVirtualTagSource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, VirtualTagState> _tags;

    /// <summary>Creates a source from one or more tag definitions.</summary>
    public FixedVirtualTagSource(IEnumerable<VirtualTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        _tags = new Dictionary<string, VirtualTagState>(StringComparer.OrdinalIgnoreCase);
        foreach (VirtualTag tag in tags)
        {
            ArgumentNullException.ThrowIfNull(tag);
            if (tag.ElectronicProductCode.IsEmpty || tag.ElectronicProductCode.Length % 2 != 0)
            {
                throw new ArgumentException("A virtual tag EPC must contain an even number of octets.", nameof(tags));
            }

            string key = Convert.ToHexString(tag.ElectronicProductCode.Span);
            if (!_tags.TryAdd(key, new VirtualTagState(tag)))
            {
                throw new ArgumentException($"The virtual tag EPC {key} is duplicated.", nameof(tags));
            }
        }

        if (_tags.Count == 0)
        {
            throw new ArgumentException("At least one virtual tag is required.", nameof(tags));
        }
    }

    /// <summary>Creates the default deterministic single-tag source.</summary>
    public static FixedVirtualTagSource CreateDefault() => new(
    [
        new VirtualTag
        {
            ElectronicProductCode = Convert.FromHexString("E28011710000020D056E9BEE"),
            Tid = Convert.FromHexString("E20034120123456789ABCDEF"),
        },
    ]);

    /// <inheritdoc />
    public IReadOnlyList<VirtualTag> GetTags()
    {
        lock (_gate)
        {
            return _tags.Values.Select(static tag => tag.ToSnapshot()).ToArray();
        }
    }

    /// <inheritdoc />
    public bool TryReadWords(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        int wordPointer,
        int wordCount,
        out IReadOnlyList<ushort> words)
    {
        lock (_gate)
        {
            if (!TryGetState(electronicProductCode, out VirtualTagState tag) ||
                !tag.TryReadWords(memoryBank, wordPointer, wordCount, out words))
            {
                words = [];
                return false;
            }

            return true;
        }
    }

    /// <inheritdoc />
    public bool TryWriteWords(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        int wordPointer,
        IReadOnlyList<ushort> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        lock (_gate)
        {
            return TryGetState(electronicProductCode, out VirtualTagState tag) &&
                tag.TryWriteWords(memoryBank, wordPointer, words);
        }
    }

    /// <inheritdoc />
    public bool TryGetMemoryBytes(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        out ReadOnlyMemory<byte> bytes)
    {
        lock (_gate)
        {
            if (!TryGetState(electronicProductCode, out VirtualTagState tag))
            {
                bytes = ReadOnlyMemory<byte>.Empty;
                return false;
            }

            bytes = tag.GetMemoryBytes(memoryBank);
            return true;
        }
    }

    private bool TryGetState(ReadOnlySpan<byte> electronicProductCode, out VirtualTagState tag)
    {
        if (_tags.TryGetValue(Convert.ToHexString(electronicProductCode), out VirtualTagState? found) &&
            found is not null)
        {
            tag = found;
            return true;
        }

        tag = null!;
        return false;
    }

    private sealed class VirtualTagState
    {
        private readonly VirtualTag _definition;
        private readonly ushort[] _userMemory;

        public VirtualTagState(VirtualTag definition)
        {
            _definition = definition;
            _userMemory = definition.UserMemory.ToArray();
        }

        public VirtualTag ToSnapshot() => _definition with { UserMemory = _userMemory.ToArray() };

        public bool TryReadWords(byte memoryBank, int wordPointer, int wordCount, out IReadOnlyList<ushort> words)
        {
            ushort[] memory = GetWords(memoryBank);
            if (wordPointer < 0 || wordCount < 0 || wordPointer > memory.Length - wordCount)
            {
                words = [];
                return false;
            }

            words = memory.Skip(wordPointer).Take(wordCount).ToArray();
            return true;
        }

        public bool TryWriteWords(byte memoryBank, int wordPointer, IReadOnlyList<ushort> words)
        {
            if (memoryBank != 3 || wordPointer < 0 || wordPointer > _userMemory.Length - words.Count)
            {
                return false;
            }

            for (int index = 0; index < words.Count; index++)
            {
                _userMemory[wordPointer + index] = words[index];
            }

            return true;
        }

        public ReadOnlyMemory<byte> GetMemoryBytes(byte memoryBank) => memoryBank switch
        {
            0 => new byte[16],
            1 => new byte[] { 0, 0, 0, 0 }.Concat(_definition.ElectronicProductCode.ToArray()).ToArray(),
            2 => _definition.Tid,
            3 => WordsToBytes(_userMemory),
            _ => ReadOnlyMemory<byte>.Empty,
        };

        private ushort[] GetWords(byte memoryBank) => memoryBank switch
        {
            0 => new ushort[8],
            1 => [0, 0, .. BytesToWords(_definition.ElectronicProductCode.Span)],
            2 => BytesToWords(_definition.Tid.Span),
            3 => _userMemory,
            _ => [],
        };
    }

    private static ushort[] BytesToWords(ReadOnlySpan<byte> bytes)
    {
        int wordCount = bytes.Length / 2;
        var words = new ushort[wordCount];
        for (int index = 0; index < wordCount; index++)
        {
            words[index] = (ushort)((bytes[index * 2] << 8) | bytes[(index * 2) + 1]);
        }

        return words;
    }

    private static byte[] WordsToBytes(ReadOnlySpan<ushort> words)
    {
        var bytes = new byte[words.Length * 2];
        for (int index = 0; index < words.Length; index++)
        {
            bytes[index * 2] = (byte)(words[index] >> 8);
            bytes[(index * 2) + 1] = (byte)words[index];
        }

        return bytes;
    }
}

/// <summary>Configures deterministic report cadence for one active ROSpec.</summary>
public sealed record VirtualReaderReportOptions
{
    /// <summary>Gets the interval between report messages.</summary>
    public TimeSpan ReportInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets the maximum number of report messages per ROSpec; zero means unlimited.</summary>
    public int ReportCount { get; init; }

    /// <summary>Gets whether a report is repeated until the ROSpec stops.</summary>
    public bool Repeat { get; init; } = true;
}

/// <summary>Describes the virtual reader's standard capabilities.</summary>
public sealed record VirtualReaderCapabilities
{
    public ushort MaxNumberOfAntennas { get; init; } = 4;
    public bool CanSetAntennaProperties { get; init; } = true;
    public bool HasUtcClockCapability { get; init; } = true;
    public uint ManufacturerId { get; init; }
    public uint ModelId { get; init; }
    public string FirmwareVersion { get; init; } = "virtual-reader";
}

/// <summary>Describes one initial reader-level antenna configuration.</summary>
public sealed record VirtualReaderAntennaConfiguration
{
    public ushort AntennaId { get; init; }
    public ushort ReceiverSensitivityIndex { get; init; }
    public ushort TransmitPowerIndex { get; init; }
    public ushort HopTableId { get; init; }
    public ushort ChannelIndex { get; init; }
}

/// <summary>Describes one initial virtual GPO state.</summary>
public sealed record VirtualReaderGpoState
{
    public ushort PortNumber { get; init; }
    public bool State { get; init; }
}

/// <summary>Configures one virtual reader host's device behavior.</summary>
public sealed record VirtualReaderOptions
{
    /// <summary>Gets the displayed reader name.</summary>
    public string ReaderName { get; init; } = "Virtual Reader";

    /// <summary>Gets the reader identifier exposed by the identity parameter when supported.</summary>
    public ulong ReaderId { get; init; } = 1;

    /// <summary>Gets the explicitly advertised protocol version.</summary>
    public LlrpProtocolVersion ProtocolVersion { get; init; } = LlrpProtocolVersion.Version101;

    /// <summary>Gets the maximum number of simultaneous clients.</summary>
    public int MaximumClientConnections { get; init; } = 1;

    /// <summary>Gets the deterministic policy for an additional client.</summary>
    public VirtualReaderConnectionLimitPolicy ConnectionLimitPolicy { get; init; } =
        VirtualReaderConnectionLimitPolicy.RejectAdditional;

    /// <summary>Gets the maximum time allowed between complete incoming frames.</summary>
    public TimeSpan IdleTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>Gets the maximum time allowed to assemble one frame after its first octet.</summary>
    public TimeSpan FrameAssemblyTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the maximum complete frame length accepted by a client connection.</summary>
    public uint MaximumFrameLength { get; init; } = 1_048_576;

    /// <summary>Gets whether accepted sockets enable TCP keepalive.</summary>
    public bool UseTcpKeepAlive { get; init; } = true;

    /// <summary>Gets the device-level LLRP KEEPALIVE interval; null disables active keepalives.</summary>
    public TimeSpan? KeepAliveInterval { get; init; }

    /// <summary>Gets standard capabilities exposed by GET_READER_CAPABILITIES.</summary>
    public VirtualReaderCapabilities Capabilities { get; init; } = new();

    /// <summary>Gets the initial antenna configuration returned by GET_READER_CONFIG.</summary>
    public IReadOnlyList<VirtualReaderAntennaConfiguration> AntennaConfigurations { get; init; } = [];

    /// <summary>Gets the initial GPO state returned by GET_READER_CONFIG.</summary>
    public IReadOnlyList<VirtualReaderGpoState> GpoStates { get; init; } =
        [new VirtualReaderGpoState { PortNumber = 1, State = false }];

    /// <summary>Gets the deterministic tag source.</summary>
    public IVirtualTagSource TagSource { get; init; } = FixedVirtualTagSource.CreateDefault();

    /// <summary>Gets report cadence and repetition settings.</summary>
    public VirtualReaderReportOptions Reports { get; init; } = new();

    /// <summary>Gets the handling policy for unknown vendor parameters.</summary>
    public VirtualReaderUnknownVendorParameterBehavior UnknownVendorParameterBehavior { get; init; } =
        VirtualReaderUnknownVendorParameterBehavior.PreserveAndIgnore;

    /// <summary>Gets whether the strict standard inventory profile is enforced.</summary>
    public bool UseStrictStandardInventoryProfile { get; init; }

    /// <summary>Gets or initializes the legacy single-tag EPC value.</summary>
    public ReadOnlyMemory<byte> ElectronicProductCode
    {
        get => LegacyElectronicProductCode ?? ReadOnlyMemory<byte>.Empty;
        init => LegacyElectronicProductCode = value.IsEmpty ? null : value.ToArray();
    }

    /// <summary>Gets or initializes the legacy single-tag User-memory words.</summary>
    public IReadOnlyList<ushort> UserMemory
    {
        get => LegacyUserMemory ?? [];
        init => LegacyUserMemory = value?.ToArray();
    }

    /// <summary>Gets request types for which the host intentionally withholds a response.</summary>
    public IReadOnlySet<ushort> DropResponseForMessageTypes { get; init; } = new HashSet<ushort>();

    /// <summary>Gets request types for which the host returns an injected LLRP error.</summary>
    public IReadOnlyDictionary<ushort, VirtualReaderErrorResponse> ErrorResponseForMessageTypes { get; init; } =
        new Dictionary<ushort, VirtualReaderErrorResponse>();

    /// <summary>Gets request types after which the current TCP connection closes once.</summary>
    public IReadOnlySet<ushort> CloseConnectionAfterRequestMessageTypes { get; init; } = new HashSet<ushort>();

    /// <summary>Gets request types whose response is truncated before the connection closes.</summary>
    public IReadOnlySet<ushort> TruncateResponseForMessageTypes { get; init; } = new HashSet<ushort>();

    private byte[]? LegacyElectronicProductCode { get; init; }
    private IReadOnlyList<ushort>? LegacyUserMemory { get; init; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ReaderName))
        {
            throw new ArgumentException("A virtual reader name is required.", nameof(ReaderName));
        }

        if (MaximumClientConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumClientConnections));
        }

        if (!Enum.IsDefined(ProtocolVersion))
        {
            throw new ArgumentOutOfRangeException(nameof(ProtocolVersion));
        }

        ArgumentNullException.ThrowIfNull(Capabilities);
        ArgumentNullException.ThrowIfNull(TagSource);
        ArgumentNullException.ThrowIfNull(Reports);
        ArgumentNullException.ThrowIfNull(AntennaConfigurations);
        ArgumentNullException.ThrowIfNull(GpoStates);
        ArgumentNullException.ThrowIfNull(DropResponseForMessageTypes);
        ArgumentNullException.ThrowIfNull(ErrorResponseForMessageTypes);
        ArgumentNullException.ThrowIfNull(CloseConnectionAfterRequestMessageTypes);
        ArgumentNullException.ThrowIfNull(TruncateResponseForMessageTypes);
        if (Reports.ReportCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Reports.ReportCount));
        }

        if (MaximumFrameLength < LlrpMessageHeader.EncodedLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumFrameLength),
                MaximumFrameLength,
                $"The maximum frame length must be at least {LlrpMessageHeader.EncodedLength} octets.");
        }

        ValidatePositiveTimeout(Reports.ReportInterval, nameof(Reports.ReportInterval));
        ValidatePositiveTimeout(FrameAssemblyTimeout, nameof(FrameAssemblyTimeout));
        if (IdleTimeout != Timeout.InfiniteTimeSpan)
        {
            ValidatePositiveTimeout(IdleTimeout, nameof(IdleTimeout));
        }

        if (KeepAliveInterval is TimeSpan keepAliveInterval)
        {
            ValidatePositiveTimeout(keepAliveInterval, nameof(KeepAliveInterval));
        }

        foreach (VirtualReaderAntennaConfiguration antenna in AntennaConfigurations)
        {
            ArgumentNullException.ThrowIfNull(antenna);
            if (antenna.AntennaId == 0)
            {
                throw new ArgumentException("Antenna identifiers must be non-zero.", nameof(AntennaConfigurations));
            }
        }

        foreach (VirtualReaderGpoState gpo in GpoStates)
        {
            ArgumentNullException.ThrowIfNull(gpo);
        }
    }

    internal IVirtualTagSource NormalizeLegacyTagSource()
    {
        if (LegacyElectronicProductCode is null && LegacyUserMemory is null)
        {
            return TagSource;
        }

        VirtualTag original = TagSource.GetTags().FirstOrDefault() ?? new VirtualTag
        {
            ElectronicProductCode = Convert.FromHexString("E28011710000020D056E9BEE"),
        };
        return new FixedVirtualTagSource(
        [
            original with
            {
                ElectronicProductCode = LegacyElectronicProductCode ?? original.ElectronicProductCode,
                UserMemory = LegacyUserMemory ?? original.UserMemory,
            },
        ]);
    }

    private static void ValidatePositiveTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMilliseconds(uint.MaxValue - 1d))
        {
            throw new ArgumentOutOfRangeException(parameterName, timeout, "The timeout must be positive and finite.");
        }
    }
}

/// <summary>Configures the exact endpoint and host-level diagnostics for one virtual reader.</summary>
public sealed record VirtualReaderHostOptions
{
    /// <summary>Gets the local address on which the host listens.</summary>
    public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;

    /// <summary>Gets the TCP port. Port zero is reserved for in-process tests.</summary>
    public int Port { get; init; }

    /// <summary>Gets the deterministic device behavior options.</summary>
    public VirtualReaderOptions ReaderOptions { get; init; } = new();

    /// <summary>Gets the optional logger factory used by the host and accepted transports.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>Gets the optional bounded frame observer used by all accepted connections.</summary>
    public ILlrpFrameObserver? FrameObserver { get; init; }

    /// <summary>Gets optional protocol modules that contribute codecs and message handlers.</summary>
    public IReadOnlyList<IVirtualReaderProtocolModule> ProtocolModules { get; init; } = [];

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(ListenAddress);
        ArgumentNullException.ThrowIfNull(ReaderOptions);
        ArgumentNullException.ThrowIfNull(ProtocolModules);
        if (Port is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "The virtual reader port must be between 0 and 65535.");
        }

        ReaderOptions.Validate();
        foreach (IVirtualReaderProtocolModule module in ProtocolModules)
        {
            ArgumentNullException.ThrowIfNull(module);
        }
    }
}

/// <summary>Describes one deterministic status code fault injection.</summary>
public sealed record VirtualReaderErrorResponse
{
    /// <summary>Creates a fault using the standard 1.0.1 status enum for source compatibility.</summary>
    public VirtualReaderErrorResponse(StatusCode statusCode, string description)
        : this(checked((ushort)statusCode), description)
    {
    }

    /// <summary>Creates a fault from a wire status code.</summary>
    public VirtualReaderErrorResponse(ushort statusCode, string description)
    {
        StatusCode = statusCode;
        Description = description ?? string.Empty;
    }

    /// <summary>Gets the numeric LLRP status code.</summary>
    public ushort StatusCode { get; }

    /// <summary>Gets the human-readable error description.</summary>
    public string Description { get; }
}
