using System.Collections.ObjectModel;

namespace LlrpDevice.Abstractions;

/// <summary>Describes the identity exposed by an LLRP device.</summary>
public sealed record LlrpDeviceIdentity
{
    public ulong ReaderId { get; init; } = 1;
    public required string Name { get; init; }
    public uint ManufacturerId { get; init; }
    public uint ModelId { get; init; }
    public string FirmwareVersion { get; init; } = "device";
}

/// <summary>Describes capabilities that the device can truthfully advertise.</summary>
public sealed record LlrpDeviceCapabilities
{
    public ushort MaxNumberOfAntennas { get; init; } = 1;
    public bool CanSetAntennaProperties { get; init; } = true;
    public bool HasUtcClockCapability { get; init; } = true;
    public bool SupportsEpcGlobalClass1Gen2 { get; init; } = true;
    public bool SupportsTagAccess { get; init; } = true;
    public bool SupportsBlockWrite { get; init; } = true;
    public bool SupportsBlockErase { get; init; } = true;
    public bool SupportsStateAwareSingulation { get; init; }
    public bool SupportsReportBuffer { get; init; } = true;
    public bool SupportsEventAndReportHolding { get; init; } = true;
}

/// <summary>Describes one device antenna configuration.</summary>
public sealed record LlrpDeviceAntennaConfiguration
{
    public required ushort AntennaId { get; init; }
    public ushort ReceiverSensitivityIndex { get; init; }
    public ushort TransmitPowerIndex { get; init; }
    public ushort HopTableId { get; init; }
    public ushort ChannelIndex { get; init; }
}

/// <summary>Describes one device GPO state.</summary>
public sealed record LlrpDeviceGpoState
{
    public required ushort PortNumber { get; init; }
    public bool State { get; init; }
}

/// <summary>Represents the protocol-independent reader configuration owned by a device.</summary>
public sealed record LlrpDeviceConfiguration
{
    public IReadOnlyList<LlrpDeviceAntennaConfiguration> Antennas { get; init; } = [];
    public IReadOnlyList<LlrpDeviceGpoState> Gpos { get; init; } = [];
}

/// <summary>Requests a device configuration update.</summary>
public sealed record LlrpDeviceConfigurationUpdate
{
    public bool ResetToFactoryDefault { get; init; }
    public IReadOnlyList<LlrpDeviceAntennaConfiguration> Antennas { get; init; } = [];
    public IReadOnlyList<LlrpDeviceGpoState> Gpos { get; init; } = [];
}

/// <summary>Identifies a memory bank used by EPCglobal Class-1 Gen-2 operations.</summary>
public enum LlrpTagMemoryBank : byte
{
    Reserved = 0,
    ElectronicProductCode = 1,
    Tid = 2,
    User = 3,
}

/// <summary>Defines one bit-level tag selector.</summary>
public sealed record LlrpTagSelector
{
    public LlrpTagMemoryBank MemoryBank { get; init; } = LlrpTagMemoryBank.ElectronicProductCode;
    public ushort BitPointer { get; init; }
    public ushort BitLength { get; init; }
    public ReadOnlyMemory<byte> Mask { get; init; }
    public ReadOnlyMemory<byte> Data { get; init; }
    public bool Match { get; init; } = true;
}

/// <summary>Describes the selection action applied by one inventory filter.</summary>
public enum LlrpInventoryFilterAction
{
    DoNothing,
    Select,
    Unselect,
}

/// <summary>Describes the state target used by a state-aware Gen2 inventory filter.</summary>
public enum LlrpInventoryStateTarget
{
    SelectedFlag,
    Session0,
    Session1,
    Session2,
    Session3,
}

/// <summary>Describes the state-aware action encoded by a Gen2 inventory filter.</summary>
public enum LlrpInventoryStateAction
{
    AssertStateAOrSelectedAndDeassertStateBOrUnselected,
    AssertStateAOrSelectedAndNoOperation,
    NoOperationAndDeassertStateBOrUnselected,
    NegateStateOrSelectedAndNoOperation,
    DeassertStateBOrUnselectedAndAssertStateAOrSelected,
    DeassertStateBOrUnselectedAndNoOperation,
    NoOperationAndAssertStateAOrSelected,
    NoOperationAndNegateStateOrSelected,
}

/// <summary>Describes one standard C1G2 inventory Select filter.</summary>
public sealed record LlrpInventoryFilter
{
    public required LlrpTagSelector Selector { get; init; }
    public LlrpInventoryFilterAction MatchAction { get; init; } = LlrpInventoryFilterAction.Select;
    public LlrpInventoryFilterAction NonMatchAction { get; init; } = LlrpInventoryFilterAction.Unselect;
    public LlrpInventoryStateTarget? StateTarget { get; init; }
    public LlrpInventoryStateAction? StateAction { get; init; }
}

/// <summary>Describes RF control values requested by one inventory command.</summary>
public sealed record LlrpInventoryRfControl
{
    public ushort ModeIndex { get; init; }
    public ushort Tari { get; init; }
}

/// <summary>Describes one antenna-specific RF configuration attached to an inventory AISpec.</summary>
public sealed record LlrpInventoryAntennaConfiguration
{
    public required ushort AntennaId { get; init; }
    public ushort? ReceiverSensitivityIndex { get; init; }
    public ushort? TransmitPowerIndex { get; init; }
    public ushort? HopTableId { get; init; }
    public ushort? ChannelIndex { get; init; }
}

/// <summary>Describes C1G2 singulation values requested by one inventory command.</summary>
public sealed record LlrpInventorySingulationControl
{
    public byte Session { get; init; }
    public ushort TagPopulation { get; init; }
    public uint TagTransitTime { get; init; }
    public bool StateAware { get; init; }
    public LlrpInventoryStateAwareSingulation? StateAwareSingulation { get; init; }
}

/// <summary>Describes the inventoried state selected by C1G2 state-aware singulation.</summary>
public enum LlrpInventorySingulationTarget
{
    StateA,
    StateB,
}

/// <summary>Describes the Selected (SL) flag selected by C1G2 state-aware singulation.</summary>
public enum LlrpInventorySelectedFlag
{
    Set,
    Clear,
}

/// <summary>Describes the state-aware singulation action requested by an inventory command.</summary>
public sealed record LlrpInventoryStateAwareSingulation
{
    public LlrpInventorySingulationTarget Target { get; init; } = LlrpInventorySingulationTarget.StateA;
    public LlrpInventorySelectedFlag SelectedFlag { get; init; } = LlrpInventorySelectedFlag.Set;
}

/// <summary>Identifies one C1G2 operation.</summary>
public enum LlrpTagAccessOperationKind
{
    Read,
    Write,
    BlockWrite,
    Lock,
    Kill,
    BlockErase,
}

/// <summary>Represents C1G2 lock privilege values without a protocol-version dependency.</summary>
public enum LlrpTagLockPrivilege
{
    ReadWrite,
    PermaUnlock,
    Unlock,
    PermaLock,
}

/// <summary>Describes one lock payload.</summary>
public sealed record LlrpTagLockRequest(
    LlrpTagLockPrivilege Privilege,
    LlrpTagMemoryBank MemoryBank);

/// <summary>Describes one C1G2 tag access operation.</summary>
public sealed record LlrpTagAccessOperation
{
    public required ushort OperationId { get; init; }
    public required LlrpTagAccessOperationKind Kind { get; init; }
    public uint AccessPassword { get; init; }
    public uint KillPassword { get; init; }
    public LlrpTagMemoryBank MemoryBank { get; init; } = LlrpTagMemoryBank.User;
    public ushort WordPointer { get; init; }
    public ushort WordCount { get; init; }
    public IReadOnlyList<ushort> WriteData { get; init; } = [];
    public IReadOnlyList<LlrpTagLockRequest> LockRequests { get; init; } = [];
}

/// <summary>Describes an access request compiled from a wire AccessSpec.</summary>
public sealed record LlrpTagAccessRequest
{
    public required uint AccessSpecId { get; init; }
    public required uint RoSpecId { get; init; }
    public required LlrpTagSelector Selector { get; init; }
    public IReadOnlyList<LlrpTagAccessOperation> Operations { get; init; } = [];
}

/// <summary>Identifies the result of one device-side tag access operation.</summary>
public enum LlrpTagAccessResultCode
{
    Success,
    NoResponseFromTag,
    MemoryOverrun,
    NonSpecificTagError,
    IncorrectPassword,
    TagKilled,
    Locked,
    UnsupportedOperation,
    Failed,
}

/// <summary>Contains one operation result returned by a device.</summary>
public sealed record LlrpTagAccessOperationResult
{
    public required ushort OperationId { get; init; }
    public required LlrpTagAccessResultCode Result { get; init; }
    public IReadOnlyList<ushort> ReadData { get; init; } = [];
    public ushort WordsWritten { get; init; }
    public string? Error { get; init; }
}

/// <summary>Contains the selected tag and all operation results.</summary>
public sealed record LlrpTagAccessResult
{
    public required TagObservation Tag { get; init; }
    public IReadOnlyList<LlrpTagAccessOperationResult> Operations { get; init; } = [];
}

/// <summary>Describes a version-neutral inventory plan compiled by the Server.</summary>
public sealed record LlrpInventoryPlan
{
    public required uint RoSpecId { get; init; }
    public IReadOnlyList<ushort> AntennaIds { get; init; } = [];
    public IReadOnlyList<LlrpInventoryAntennaConfiguration> AntennaConfigurations { get; init; } = [];
    public ushort? InventoryParameterSpecId { get; init; }
    public int? MaxTagsPerRound { get; init; }
    public IReadOnlyList<LlrpInventoryFilter> Filters { get; init; } = [];
    public LlrpInventoryRfControl? RfControl { get; init; }
    public LlrpInventorySingulationControl? Singulation { get; init; }
}

/// <summary>Identifies one inventory observation round.</summary>
public sealed record LlrpInventoryRound(
    uint RoSpecId,
    int Sequence,
    IReadOnlyList<ushort> AntennaIds)
{
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Represents one tag observed during inventory.</summary>
public sealed record TagObservation
{
    public required ReadOnlyMemory<byte> ElectronicProductCode { get; init; }
    public ReadOnlyMemory<byte> Tid { get; init; }
    public short PeakRssi { get; init; } = -42;
    public ushort AntennaId { get; init; } = 1;
    public ushort ChannelIndex { get; init; } = 1;
    public DateTimeOffset FirstSeenUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenUtc { get; init; }
    public uint SeenCount { get; init; } = 1;
    public ushort? PcBits { get; init; }
    public ushort? Crc { get; init; }
}

/// <summary>Contains the observations returned for one inventory round.</summary>
public sealed record InventoryObservationBatch
{
    public IReadOnlyList<TagObservation> Tags { get; init; } = [];
}

/// <summary>Describes a structured device error.</summary>
public sealed record LlrpDeviceError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public Exception? Exception { get; init; }
}

/// <summary>Reports a device-side event to the generic Server.</summary>
public sealed record LlrpDeviceEvent
{
    public required string Name { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? Detail { get; init; }
    public LlrpDeviceError? Error { get; init; }
    public ushort? GpiPortNumber { get; init; }
    public bool? GpiState { get; init; }
    public ushort? AntennaId { get; init; }
    public bool? AntennaConnected { get; init; }
    public uint? RoSpecId { get; init; }
    public uint? AccessSpecId { get; init; }
    public ushort? OpSpecId { get; init; }
    public ushort? SpecIndex { get; init; }
    public ushort? InventoryParameterSpecId { get; init; }
    public byte? ReportBufferPercentage { get; init; }
}

/// <summary>Provides a running inventory operation.</summary>
public interface IInventoryExecution : IAsyncDisposable
{
    public LlrpInventoryPlan Plan { get; }

    public ValueTask<InventoryObservationBatch> ObserveAsync(
        LlrpInventoryRound round,
        CancellationToken cancellationToken = default);

    public ValueTask StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Describes a device result that does not return a protocol payload.</summary>
public sealed record LlrpDeviceOperationResult
{
    public bool Succeeded { get; init; }
    public LlrpDeviceError? Error { get; init; }

    public static LlrpDeviceOperationResult Success() => new() { Succeeded = true };

    public static LlrpDeviceOperationResult Failure(string code, string message, Exception? exception = null) => new()
    {
        Error = new LlrpDeviceError { Code = code, Message = message, Exception = exception },
    };
}
