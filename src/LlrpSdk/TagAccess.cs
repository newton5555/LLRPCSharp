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

/// <summary>Represents one standard C1G2 operation result projected from a tag report.</summary>
public sealed record TagAccessOperationResult(
    ushort OpSpecId,
    bool Success,
    IReadOnlyList<ushort> ReadData,
    ushort? WordsWritten,
    string? Error);

/// <summary>Represents one tag and the result of the requested access operation.</summary>
public sealed record TagAccessResult(
    TagReport Tag,
    TagAccessOperationResult Operation);
