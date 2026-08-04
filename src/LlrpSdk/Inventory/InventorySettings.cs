namespace LlrpSdk;

/// <summary>
/// Configures optional attached memory bank read operations during managed inventory.
/// </summary>
public sealed record AttachedDataOptions
{
    /// <summary>Gets a value indicating whether attached memory reading is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the Gen2 memory bank to read (0=Reserved, 1=EPC, 2=TID, 3=User). Default is 2 (TID).</summary>
    public ushort MemoryBank { get; init; } = 2;

    /// <summary>Gets the starting word pointer for reading.</summary>
    public ushort WordPointer { get; init; }

    /// <summary>Gets the number of 16-bit words to read. Default is 6 words.</summary>
    public ushort WordCount { get; init; } = 6;

    /// <summary>Gets the 32-bit hex access password string (8 hex characters); converted internally to the LLRP unsigned 32-bit wire value.</summary>
    public string AccessPassword { get; init; } = "00000000";
}

/// <summary>Defines how a managed ROSpec begins executing after it is enabled.</summary>
public sealed record InventoryStartTrigger
{
    /// <summary>
    /// Gets the trigger kind. The default keeps the ROSpec inactive until the caller explicitly starts it.
    /// </summary>
    public InventoryStartTriggerType Type { get; init; } = InventoryStartTriggerType.None;

    /// <summary>Gets the periodic-trigger offset in milliseconds.</summary>
    public uint OffsetMilliseconds { get; init; }

    /// <summary>Gets the periodic-trigger period in milliseconds. It must be non-zero for a periodic trigger.</summary>
    public uint PeriodMilliseconds { get; init; }

    /// <summary>
    /// Gets the optional UTC time for the first periodic start. When omitted, the reader starts according to
    /// <see cref="OffsetMilliseconds"/>; when supplied, it is compiled to LLRP <c>UTCTimestamp</c>.
    /// </summary>
    public DateTimeOffset? StartAtUtc { get; init; }

    /// <summary>Gets the GPI port used by a GPI trigger. It must be non-zero for a GPI trigger.</summary>
    public ushort GpiPortNumber { get; init; }

    /// <summary>Gets the GPI state which activates a GPI trigger.</summary>
    public bool GpiState { get; init; }

    /// <summary>Gets the GPI trigger timeout in milliseconds; zero means no timeout.</summary>
    public uint TimeoutMilliseconds { get; init; }
}

/// <summary>Defines the supported standard ROSpec start trigger kinds.</summary>
public enum InventoryStartTriggerType
{
    /// <summary>Do not start when the ROSpec is enabled; start it with START_ROSPEC.</summary>
    None,

    Immediate,
    Periodic,
    Gpi,
}

/// <summary>Defines how a managed ROSpec stops executing.</summary>
public sealed record InventoryStopTrigger
{
    /// <summary>Gets the trigger kind. The default has no automatic stop condition.</summary>
    public InventoryStopTriggerType Type { get; init; } = InventoryStopTriggerType.None;

    /// <summary>Gets the duration in milliseconds for a duration trigger.</summary>
    public uint DurationMilliseconds { get; init; }

    /// <summary>Gets the GPI port used by a GPI-with-timeout trigger.</summary>
    public ushort GpiPortNumber { get; init; }

    /// <summary>Gets the GPI state which stops the ROSpec.</summary>
    public bool GpiState { get; init; }

    /// <summary>Gets the GPI trigger timeout in milliseconds; zero means no timeout.</summary>
    public uint TimeoutMilliseconds { get; init; }
}

/// <summary>Defines the supported standard ROSpec stop trigger kinds.</summary>
public enum InventoryStopTriggerType
{
    None,
    Duration,
    GpiWithTimeout,
}

/// <summary>Describes a state-aware C1G2 singulation action for an inventory operation.</summary>
public sealed record InventoryStateAwareSingulation
{
    /// <summary>Gets the inventory state targeted for the configured C1G2 session.</summary>
    public InventoryTarget Target { get; init; } = InventoryTarget.StateA;

    /// <summary>Gets whether tags with the SL flag set or clear participate in the action.</summary>
    public InventorySelectedFlag SelectedFlag { get; init; } = InventorySelectedFlag.Set;
}

/// <summary>Defines the C1G2 inventoried state targeted by state-aware singulation.</summary>
public enum InventoryTarget
{
    StateA,
    StateB,
}

/// <summary>Defines the SL flag state selected by state-aware singulation.</summary>
public enum InventorySelectedFlag
{
    Set,
    Clear,
    /// <summary>
    /// Ignores the SL flag. This is represented by <c>S_All</c> in LLRP 1.1 and is not available in LLRP 1.0.1.
    /// </summary>
    All,
}

/// <summary>One ordered Gen2 Select rule used before inventory.</summary>
public sealed record InventorySelectFilter
{
    /// <summary>Gen2 memory bank (0=Reserved, 1=EPC, 2=TID, 3=User).</summary>
    public ushort MemoryBank { get; init; } = 1;
    /// <summary>Bit pointer in the selected memory bank.</summary>
    public ushort BitPointer { get; init; } = 32;
    /// <summary>Mask bits, encoded most-significant bit first.</summary>
    public ReadOnlyMemory<byte> Mask { get; init; }
    /// <summary>
    /// Gets the number of meaningful mask bits. A value of zero uses all bits in <see cref="Mask"/>.
    /// This preserves standard LLRP filters whose mask is not byte aligned.
    /// </summary>
    public ushort BitLength { get; init; }
    public InventorySelectAction MatchAction { get; init; } = InventorySelectAction.Select;
    public InventorySelectAction NonMatchAction { get; init; } = InventorySelectAction.Unselect;
    /// <summary>
    /// Gets an optional state-aware Select action. When supplied it replaces the state-unaware action pair.
    /// </summary>
    public InventoryStateAwareFilterAction? StateAwareAction { get; init; }
}

/// <summary>Describes the state-aware action for one Gen2 Select rule.</summary>
public sealed record InventoryStateAwareFilterAction
{
    public InventoryFilterTarget Target { get; init; } = InventoryFilterTarget.SelectedFlag;
    public InventoryFilterAction Action { get; init; } = InventoryFilterAction.AssertSelectedOrStateAAndDeassertSelectedOrStateB;
}

/// <summary>Defines the Gen2 tag population targeted by a state-aware Select rule.</summary>
public enum InventoryFilterTarget
{
    SelectedFlag,
    Session0,
    Session1,
    Session2,
    Session3,
}

/// <summary>Defines the result of a state-aware Select rule.</summary>
public enum InventoryFilterAction
{
    AssertSelectedOrStateAAndDeassertSelectedOrStateB,
    AssertSelectedOrStateAAndNoOperation,
    NoOperationAndDeassertSelectedOrStateB,
    NegateSelectedOrStateAndNoOperation,
    DeassertSelectedOrStateBAndAssertSelectedOrStateA,
    DeassertSelectedOrStateBAndNoOperation,
    NoOperationAndAssertSelectedOrStateA,
    NoOperationAndNegateSelectedOrState,
}

/// <summary>State-unaware Select action applied for a matching or non-matching tag.</summary>
public enum InventorySelectAction
{
    DoNothing,
    Select,
    Unselect,
}

/// <summary>Controls the standard fields and trigger used for managed inventory reports.</summary>
public sealed record InventoryReportSettings
{
    public InventoryReportTrigger Trigger { get; init; } = InventoryReportTrigger.UponNTagsOrEndOfAiSpec;
    public bool IncludeRoSpecId { get; init; } = true;
    public bool IncludeSpecIndex { get; init; } = true;
    public bool IncludeInventoryParameterSpecId { get; init; } = true;
    public bool IncludeAntennaId { get; init; } = true;
    public bool IncludeChannelIndex { get; init; } = true;
    public bool IncludePeakRssi { get; init; } = true;
    public bool IncludeFirstSeenTimestamp { get; init; } = true;
    public bool IncludeLastSeenTimestamp { get; init; } = true;
    public bool IncludeTagSeenCount { get; init; } = true;
    public bool IncludeAccessSpecId { get; init; } = true;
    public bool IncludeCrc { get; init; }
    public bool IncludePcBits { get; init; }
}

public enum InventoryReportTrigger
{
    None,
    UponNTagsOrEndOfAiSpec,
    UponNTagsOrEndOfRoSpec,
}

/// <summary>
/// Standard LLRP antenna-specific RF values used by an inventory AISpec.
/// </summary>
/// <remarks>
/// These values are part of managed inventory intent, rather than reader-global configuration. Null omits the
/// corresponding standard parameter; when an RF transmitter is specified, its three wire fields are supplied together.
/// </remarks>
public sealed record InventoryAntennaConfiguration
{
    /// <summary>Gets the LLRP antenna identifier. Zero applies this configuration to all selected antennas.</summary>
    public ushort AntennaId { get; init; }
    public ushort? ReceiverSensitivityIndex { get; init; }
    public ushort? TransmitPowerIndex { get; init; }
    public ushort? HopTableId { get; init; }
    public ushort? ChannelIndex { get; init; }
}

/// <summary>
/// Describes the version-independent intent for one managed inventory operation.
/// </summary>
/// <remarks>
/// The SDK compiles these settings into the protocol version selected for the reader. This type deliberately
/// contains no LLRP message or parameter types.
/// </remarks>
public sealed record InventorySettings
{
    /// <summary>Gets vendor-specific inventory options keyed by a contributor-owned stable name.</summary>
    /// <remarks>The core SDK never interprets these values; only an active vendor extension consumes its own key.</remarks>
    public IReadOnlyDictionary<string, object?> Extensions { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    /// <summary>Gets ordered standard Gen2 Select filters for this inventory operation.</summary>
    public IReadOnlyList<InventorySelectFilter> Filters { get; init; } = Array.Empty<InventorySelectFilter>();

    /// <summary>
    /// Gets the reader antenna identifiers to use. The default value <c>0</c> selects all reader antennas.
    /// </summary>
    public IReadOnlyList<ushort> AntennaIds { get; init; } = [0];

    /// <summary>
    /// Gets optional standard LLRP RF parameters for individual inventory antennas.
    /// </summary>
    /// <remarks>
    /// Empty means no antenna-specific RFReceiver/RFTransmitter parameters are emitted. A vendor profile may resolve
    /// capability-dependent recommendations here; vendor custom parameters belong in <see cref="Extensions"/>.
    /// </remarks>
    public IReadOnlyList<InventoryAntennaConfiguration> AntennaConfigurations { get; init; } = Array.Empty<InventoryAntennaConfiguration>();

    /// <summary>
    /// Gets the priority assigned to the SDK-managed ROSpec.
    /// </summary>
    public byte Priority { get; init; }

    /// <summary>
    /// Gets the inventory parameter specification identifier inside the managed ROSpec.
    /// </summary>
    public ushort InventoryParameterSpecId { get; init; } = 1;

    /// <summary>
    /// Gets the number of observed tags that trigger one report. The default reports each observed tag.
    /// A value of zero is valid only with <see cref="InventoryReportTrigger.UponNTagsOrEndOfRoSpec"/>,
    /// where it suppresses the N-tag condition and reports the buffered data when the ROSpec ends.
    /// </summary>
    public ushort ReportEveryNTags { get; init; } = 1;

    /// <summary>Gets the standard selector and trigger used for inventory reports.</summary>
    public InventoryReportSettings Report { get; init; } = new();

    /// <summary>
    /// Gets the C1G2 Session (0, 1, 2, or 3) for singulation. Default is 0.
    /// </summary>
    public byte Session { get; init; }

    /// <summary>
    /// Gets the estimated tag population for singulation slot count calculation. Default is 32.
    /// </summary>
    public ushort TagPopulationEstimate { get; init; } = 32;

    /// <summary>
    /// Gets the C1G2 ModeIndex (RF mode) to request, or <c>0</c> for default mode.
    /// </summary>
    public ushort ModeIndex { get; init; }

    /// <summary>
    /// Gets the Tari value in nsec, or <c>0</c> for default Tari.
    /// </summary>
    public ushort Tari { get; init; }

    /// <summary>
    /// Gets the attached data options for reading extra tag memory during inventory.
    /// </summary>
    public AttachedDataOptions AttachedData { get; init; } = new();

    /// <summary>Gets the ROSpec trigger that begins inventory after the ROSpec is enabled.</summary>
    public InventoryStartTrigger StartTrigger { get; init; } = new();

    /// <summary>Gets the ROSpec trigger that automatically stops inventory.</summary>
    public InventoryStopTrigger StopTrigger { get; init; } = new();

    /// <summary>
    /// Gets the optional state-aware C1G2 singulation action. The connected reader must advertise
    /// <see cref="ReaderCapabilities.CanDoTagInventoryStateAwareSingulation"/> when this is set.
    /// </summary>
    public InventoryStateAwareSingulation? StateAwareSingulation { get; init; }

    /// <summary>Creates inventory settings through the optional fluent configuration helper.</summary>
    public static InventorySettings Create(Action<InventorySettingsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new InventorySettingsBuilder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>Returns an edited copy while preserving fields not changed by the helper.</summary>
    public InventorySettings Edit(Action<InventorySettingsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new InventorySettingsBuilder(this);
        configure(builder);
        return builder.Build();
    }
}
