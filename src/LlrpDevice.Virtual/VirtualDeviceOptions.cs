using LlrpDevice.Abstractions;

namespace LlrpDevice.Virtual;

public enum VirtualRfScenario
{
    Static,
    MovingTags,
    Noisy,
}

public sealed record VirtualRfSimulationOptions
{
    public VirtualRfScenario Scenario { get; init; } = VirtualRfScenario.Static;
    public int RandomSeed { get; init; } = 2026;
    public double DetectionProbability { get; init; } = 1.0;
    public int PresenceCycleRounds { get; init; } = 3;
    public int RssiJitterDb { get; init; }
    public int MaxTagsPerRound { get; init; }
}

public sealed record VirtualTagDefinition
{
    public required ReadOnlyMemory<byte> ElectronicProductCode { get; init; }
    public ReadOnlyMemory<byte> Tid { get; init; }
    public short PeakRssi { get; init; } = -42;
    public ushort AntennaId { get; init; } = 1;
    public ushort ChannelIndex { get; init; } = 1;
    public IReadOnlyList<ushort> UserMemory { get; init; } = [0, 0, 0, 0];
    public uint AccessPassword { get; init; }
    public uint KillPassword { get; init; }
}

public sealed record VirtualDeviceOptions
{
    public LlrpDeviceIdentity Identity { get; init; } = new()
    {
        ReaderId = 1,
        Name = "Virtual Reader",
        FirmwareVersion = "virtual-device",
    };

    public LlrpDeviceCapabilities Capabilities { get; init; } = new()
    {
        MaxNumberOfAntennas = 4,
        CanSetAntennaProperties = true,
        HasUtcClockCapability = true,
        SupportsStateAwareSingulation = true,
        SupportsReportBuffer = true,
        SupportsEventAndReportHolding = true,
    };

    public LlrpDeviceConfiguration Configuration { get; init; } = new()
    {
        Antennas =
        [
            new LlrpDeviceAntennaConfiguration
            {
                AntennaId = 1,
                ReceiverSensitivityIndex = 0,
                TransmitPowerIndex = 0,
                HopTableId = 0,
                ChannelIndex = 1,
            },
        ],
        Gpos = [new LlrpDeviceGpoState { PortNumber = 1, State = false }],
    };

    public IReadOnlyList<VirtualTagDefinition> Tags { get; init; } =
    [
        new VirtualTagDefinition
        {
            ElectronicProductCode = new byte[] { 0xE2, 0x80, 0x11, 0x71, 0x00, 0x00, 0x02, 0x0D, 0x05, 0x6E, 0x9B, 0xEE },
            Tid = new byte[] { 0xE2, 0x00, 0x34, 0x12, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF },
        },
    ];

    public VirtualRfSimulationOptions RfSimulation { get; init; } = new();

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Identity);
        ArgumentNullException.ThrowIfNull(Capabilities);
        ArgumentNullException.ThrowIfNull(Configuration);
        ArgumentNullException.ThrowIfNull(Tags);
        ArgumentNullException.ThrowIfNull(RfSimulation);
        if (string.IsNullOrWhiteSpace(Identity.Name))
        {
            throw new ArgumentException("A virtual device name is required.", nameof(Identity));
        }

        if (Capabilities.MaxNumberOfAntennas == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Capabilities));
        }

        if (Tags.Count == 0)
        {
            throw new ArgumentException("At least one virtual tag is required.", nameof(Tags));
        }

        if (double.IsNaN(RfSimulation.DetectionProbability) ||
            double.IsInfinity(RfSimulation.DetectionProbability) ||
            RfSimulation.DetectionProbability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(RfSimulation.DetectionProbability));
        }

        if (RfSimulation.PresenceCycleRounds <= 0 || RfSimulation.RssiJitterDb < 0 ||
            RfSimulation.MaxTagsPerRound < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RfSimulation));
        }

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (VirtualTagDefinition tag in Tags)
        {
            ArgumentNullException.ThrowIfNull(tag);
            if (tag.ElectronicProductCode.IsEmpty || tag.ElectronicProductCode.Length % 2 != 0)
            {
                throw new ArgumentException("A virtual tag EPC must contain an even number of octets.", nameof(Tags));
            }

            if (!identities.Add(Convert.ToHexString(tag.ElectronicProductCode.Span)))
            {
                throw new ArgumentException("Virtual tag EPC values must be unique.", nameof(Tags));
            }
        }
    }
}
