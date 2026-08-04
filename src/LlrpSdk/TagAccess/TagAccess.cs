namespace LlrpSdk;

/// <summary>Identifies one Gen2 tag memory bank.</summary>
public enum TagMemoryBank : byte
{
    Reserved = 0,
    ElectronicProductCode = 1,
    Tid = 2,
    User = 3,
}

/// <summary>Describes the standard C1G2 target used to select tags for an access operation.</summary>
/// <remarks>
/// The mask and data are expressed as packed, most-significant-bit-first bytes. Their meaningful bit count is
/// <see cref="BitLength"/>; trailing bits in the final byte are ignored.
/// </remarks>
public sealed record TagSelection
{
    /// <summary>Gets the memory bank that contains the target pattern.</summary>
    public TagMemoryBank MemoryBank { get; init; } = TagMemoryBank.ElectronicProductCode;

    /// <summary>Gets the starting bit address in <see cref="MemoryBank"/>.</summary>
    public ushort BitPointer { get; init; } = 32;

    /// <summary>Gets the number of significant bits in <see cref="Mask"/> and <see cref="Data"/>.</summary>
    /// <remarks>Zero means "use every packed bit" (LLRP masks are bit vectors whose array length is the bit count).</remarks>
    public ushort BitLength { get; init; }

    /// <summary>Gets the packed mask bits.</summary>
    public ReadOnlyMemory<byte> Mask { get; init; }

    /// <summary>Gets the packed target-data bits.</summary>
    public ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>Gets whether a matching tag must equal the selected data.</summary>
    public bool Match { get; init; } = true;
}

/// <summary>Base type for one version-independent, standard C1G2 tag access operation.</summary>
public abstract record TagAccessRequest
{
    /// <summary>Gets the tag selection used by the generated AccessSpec.</summary>
    public required TagSelection Selection { get; init; }

    /// <summary>Gets the target antenna, or zero to use all antennas in the associated ROSpec.</summary>
    public ushort AntennaId { get; init; }

    /// <summary>Gets the C1G2 access password.</summary>
    public uint AccessPassword { get; init; }
}

/// <summary>Requests one standard C1G2 memory read.</summary>
public sealed record ReadTagRequest : TagAccessRequest
{
    /// <summary>Gets the memory bank to read.</summary>
    public TagMemoryBank MemoryBank { get; init; } = TagMemoryBank.User;

    /// <summary>Gets the starting word address.</summary>
    public ushort WordPointer { get; init; }

    /// <summary>Gets the number of 16-bit words to read.</summary>
    public ushort WordCount { get; init; }
}

/// <summary>Requests one standard C1G2 memory write.</summary>
public sealed record WriteTagRequest : TagAccessRequest
{
    /// <summary>Gets the memory bank to write.</summary>
    public TagMemoryBank MemoryBank { get; init; } = TagMemoryBank.User;

    /// <summary>Gets the starting word address.</summary>
    public ushort WordPointer { get; init; }

    /// <summary>Gets the 16-bit words to write.</summary>
    public required IReadOnlyList<ushort> WriteData { get; init; }
}

/// <summary>Defines C1G2 lock privilege modes.</summary>
public enum TagLockMode : byte
{
    Accessible = 0,
    AlwaysAccessible = 1,
    SecuredWrite = 2,
    AlwaysNotWritable = 3,
    NoChange = 4,
}

/// <summary>Requests one standard C1G2 lock operation.</summary>
public sealed record LockTagRequest : TagAccessRequest
{
    /// <summary>Gets the privilege mode for the Kill password.</summary>
    public TagLockMode KillPasswordLockMode { get; init; } = TagLockMode.NoChange;

    /// <summary>Gets the privilege mode for the Access password.</summary>
    public TagLockMode AccessPasswordLockMode { get; init; } = TagLockMode.NoChange;

    /// <summary>Gets the privilege mode for the EPC memory bank.</summary>
    public TagLockMode EpcMemoryLockMode { get; init; } = TagLockMode.NoChange;

    /// <summary>Gets the privilege mode for the TID memory bank.</summary>
    public TagLockMode TidMemoryLockMode { get; init; } = TagLockMode.NoChange;

    /// <summary>Gets the privilege mode for the User memory bank.</summary>
    public TagLockMode UserMemoryLockMode { get; init; } = TagLockMode.NoChange;
}

/// <summary>Requests one standard C1G2 kill operation.</summary>
public sealed record KillTagRequest : TagAccessRequest
{
    /// <summary>Gets the 32-bit C1G2 Kill password required to kill the tag.</summary>
    public required uint KillPassword { get; init; }
}

/// <summary>Requests one standard C1G2 block erase operation.</summary>
public sealed record BlockEraseTagRequest : TagAccessRequest
{
    /// <summary>Gets the memory bank to erase.</summary>
    public TagMemoryBank MemoryBank { get; init; } = TagMemoryBank.User;

    /// <summary>Gets the starting word address.</summary>
    public ushort WordPointer { get; init; }

    /// <summary>Gets the number of 16-bit words to erase.</summary>
    public ushort WordCount { get; init; }
}

/// <summary>
/// Requests multiple standard C1G2 operations to be executed in one AccessSpec against the same tag selection.
/// </summary>
/// <remarks>
/// Every operation must use the same <see cref="TagAccessRequest.Selection"/> and antenna. Individual
/// operations may use their own access passwords and memory parameters.
/// </remarks>
public sealed record TagAccessSequenceRequest
{
    /// <summary>Gets the operations to compile into one AccessSpec. At least one operation is required.</summary>
    public required IReadOnlyList<TagAccessRequest> Operations { get; init; }
}

/// <summary>Represents one standard C1G2 operation result projected from a tag report.</summary>
public sealed record TagAccessOperationResult(
    ushort OpSpecID,
    bool Success,
    IReadOnlyList<ushort> ReadData,
    ushort? WordsWritten,
    string? Error);

/// <summary>Represents one tag and the result of the requested access operation.</summary>
public sealed record TagAccessResult(
    TagReport Tag,
    TagAccessOperationResult Operation);

/// <summary>Represents one tag and all result entries from a completed AccessSpec operation sequence.</summary>
public sealed record TagAccessSequenceResult(
    TagReport Tag,
    IReadOnlyList<TagAccessOperationResult> Operations);
