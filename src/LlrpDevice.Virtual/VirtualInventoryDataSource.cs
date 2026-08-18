namespace LlrpDevice.Virtual;

/// <summary>
/// Supplies the tag population observed by a virtual inventory execution.
/// The source is deliberately separate from reader capabilities and endpoint
/// settings so a future UI, file source, or live simulator can be composed
/// without changing the device-side LLRP service.
/// </summary>
public interface IVirtualInventoryDataSource
{
    /// <summary>Gets the stable source identifier used for diagnostics.</summary>
    public string Id { get; }

    /// <summary>Gets the initial tag definitions supplied to a virtual device.</summary>
    public IReadOnlyList<VirtualTagDefinition> Tags { get; }
}

/// <summary>Immutable in-memory inventory data source for virtual devices.</summary>
public sealed class InMemoryVirtualInventoryDataSource : IVirtualInventoryDataSource
{
    public InMemoryVirtualInventoryDataSource(
        string id,
        IEnumerable<VirtualTagDefinition> tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(tags);

        Id = id;
        Tags = tags.ToArray();
        ValidateTags(Tags);
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public IReadOnlyList<VirtualTagDefinition> Tags { get; }

    private static void ValidateTags(IReadOnlyList<VirtualTagDefinition> tags)
    {
        if (tags.Count == 0)
        {
            throw new ArgumentException("At least one virtual inventory tag is required.", nameof(tags));
        }

        var epcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (VirtualTagDefinition tag in tags)
        {
            ArgumentNullException.ThrowIfNull(tag);
            if (tag.ElectronicProductCode.IsEmpty || tag.ElectronicProductCode.Length % 2 != 0)
            {
                throw new ArgumentException(
                    "A virtual inventory tag EPC must contain an even number of octets.",
                    nameof(tags));
            }

            if (!epcs.Add(Convert.ToHexString(tag.ElectronicProductCode.Span)))
            {
                throw new ArgumentException("Virtual inventory tag EPC values must be unique.", nameof(tags));
            }
        }
    }
}

/// <summary>Built-in inventory data sources supplied by the virtual-device SDK.</summary>
public static class VirtualInventoryDataSources
{
    /// <summary>Identifier of the deterministic default tag source.</summary>
    public const string DefaultId = "default";

    /// <summary>Deterministic multi-tag population used when no source is supplied.</summary>
    public static InMemoryVirtualInventoryDataSource Default { get; } =
        new(
            DefaultId,
            [
                new VirtualTagDefinition
                {
                    ElectronicProductCode = new byte[]
                    {
                        0xE2, 0x80, 0x11, 0x71, 0x00, 0x00,
                        0x02, 0x0D, 0x05, 0x6E, 0x9B, 0xEE,
                    },
                    Tid = new byte[]
                    {
                        0xE2, 0x00, 0x34, 0x12, 0x01, 0x23,
                        0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
                    },
                    PeakRssi = -42,
                    AntennaId = 1,
                    ChannelIndex = 1,
                },
                new VirtualTagDefinition
                {
                    ElectronicProductCode = new byte[]
                    {
                        0xE2, 0x80, 0x11, 0x71, 0x00, 0x00,
                        0x02, 0x0D, 0x05, 0x6E, 0x9B, 0xEF,
                    },
                    Tid = new byte[]
                    {
                        0xE2, 0x00, 0x34, 0x12, 0x01, 0x23,
                        0x45, 0x67, 0x89, 0xAB, 0xCD, 0xE0,
                    },
                    PeakRssi = -48,
                    AntennaId = 1,
                    ChannelIndex = 2,
                },
                new VirtualTagDefinition
                {
                    ElectronicProductCode = new byte[]
                    {
                        0xE2, 0x80, 0x11, 0x71, 0x00, 0x00,
                        0x02, 0x0D, 0x05, 0x6E, 0x9B, 0xF0,
                    },
                    Tid = new byte[]
                    {
                        0xE2, 0x00, 0x34, 0x12, 0x01, 0x23,
                        0x45, 0x67, 0x89, 0xAB, 0xCD, 0xE1,
                    },
                    PeakRssi = -51,
                    AntennaId = 2,
                    ChannelIndex = 1,
                },
                new VirtualTagDefinition
                {
                    ElectronicProductCode = new byte[]
                    {
                        0xE2, 0x80, 0x11, 0x71, 0x00, 0x00,
                        0x02, 0x0D, 0x05, 0x6E, 0x9B, 0xF1,
                    },
                    Tid = new byte[]
                    {
                        0xE2, 0x00, 0x34, 0x12, 0x01, 0x23,
                        0x45, 0x67, 0x89, 0xAB, 0xCD, 0xE2,
                    },
                    PeakRssi = -54,
                    AntennaId = 2,
                    ChannelIndex = 2,
                },
                new VirtualTagDefinition
                {
                    ElectronicProductCode = new byte[]
                    {
                        0xE2, 0x80, 0x11, 0x71, 0x00, 0x00,
                        0x02, 0x0D, 0x05, 0x6E, 0x9B, 0xF2,
                    },
                    Tid = new byte[]
                    {
                        0xE2, 0x00, 0x34, 0x12, 0x01, 0x23,
                        0x45, 0x67, 0x89, 0xAB, 0xCD, 0xE3,
                    },
                    PeakRssi = -46,
                    AntennaId = 3,
                    ChannelIndex = 1,
                },
                new VirtualTagDefinition
                {
                    ElectronicProductCode = new byte[]
                    {
                        0xE2, 0x80, 0x11, 0x71, 0x00, 0x00,
                        0x02, 0x0D, 0x05, 0x6E, 0x9B, 0xF3,
                    },
                    Tid = new byte[]
                    {
                        0xE2, 0x00, 0x34, 0x12, 0x01, 0x23,
                        0x45, 0x67, 0x89, 0xAB, 0xCD, 0xE4,
                    },
                    PeakRssi = -58,
                    AntennaId = 4,
                    ChannelIndex = 1,
                },
            ]);
}
