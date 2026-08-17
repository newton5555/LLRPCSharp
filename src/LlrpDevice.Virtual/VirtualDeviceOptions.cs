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
    /// <summary>
    /// Creates the deterministic 1.0.1 RF profile captured from the Zebra reader at
    /// 192.168.40.88 (manufacturer 161, model 96008, firmware 3.32.37.0).
    /// These values are a built-in virtual-device profile; they are not a live claim about attached hardware.
    /// </summary>
    public static LlrpDeviceRegulatoryCapabilities CreateDefaultRegulatoryCapabilities() => new()
    {
        CountryCode = 156,
        CommunicationsStandard = LlrpCommunicationsStandard.Unspecified,
        TransmitPowerLevels = Enumerable
            .Range(0, 193)
            .Select(static index => new LlrpDeviceTransmitPowerLevel(
                checked((ushort)index),
                checked((short)(1_000 + (index * 10)))))
            .ToArray(),
        Hopping = true,
        FrequencyHopTables =
        [
            new LlrpDeviceFrequencyHopTable(
                1,
                [
                    923_375, 923_125, 921_375, 922_875,
                    921_875, 922_375, 924_125, 922_625,
                    921_625, 922_125, 923_875, 920_875,
                    924_375, 921_125, 923_625, 920_625,
                ]),
        ],
        C1G2RfModes =
        [
            CreateRfMode(20, LlrpC1G2MValue.M4, 64_000, 2_000, 12_500, 23_000, 2_100),
            CreateRfMode(1, LlrpC1G2MValue.Fm0, 640_000, 1_500, 6_250, 6_250, 0),
            CreateRfMode(2, LlrpC1G2MValue.Fm0, 640_000, 2_000, 6_250, 6_250, 0),
            CreateRfMode(3, LlrpC1G2MValue.M2, 120_000, 1_500, 25_000, 25_000, 0),
            CreateRfMode(4, LlrpC1G2MValue.M2, 120_000, 1_500, 12_500, 23_000, 2_100),
            CreateRfMode(5, LlrpC1G2MValue.M2, 120_000, 2_000, 25_000, 25_000, 0),
            CreateRfMode(6, LlrpC1G2MValue.M2, 120_000, 2_000, 12_500, 23_000, 2_100),
            CreateRfMode(7, LlrpC1G2MValue.M2, 128_000, 1_500, 25_000, 25_000, 0),
            CreateRfMode(8, LlrpC1G2MValue.M2, 128_000, 1_500, 12_500, 23_000, 2_100),
            CreateRfMode(9, LlrpC1G2MValue.M2, 128_000, 2_000, 25_000, 25_000, 0),
            CreateRfMode(10, LlrpC1G2MValue.M2, 128_000, 2_000, 12_500, 23_000, 2_100),
            CreateRfMode(11, LlrpC1G2MValue.M2, 160_000, 1_500, 12_500, 18_800, 2_100),
            CreateRfMode(12, LlrpC1G2MValue.M2, 160_000, 2_000, 12_500, 18_800, 2_100),
            CreateRfMode(13, LlrpC1G2MValue.M4, 60_000, 1_500, 25_000, 25_000, 0),
            CreateRfMode(14, LlrpC1G2MValue.M4, 60_000, 1_500, 12_500, 23_000, 2_100),
            CreateRfMode(15, LlrpC1G2MValue.M4, 60_000, 2_000, 25_000, 25_000, 0),
            CreateRfMode(16, LlrpC1G2MValue.M4, 60_000, 2_000, 12_500, 23_000, 2_100),
            CreateRfMode(17, LlrpC1G2MValue.M4, 64_000, 1_500, 25_000, 25_000, 0),
            CreateRfMode(18, LlrpC1G2MValue.M4, 64_000, 1_500, 12_500, 23_000, 2_100),
            CreateRfMode(19, LlrpC1G2MValue.M4, 64_000, 2_000, 25_000, 25_000, 0),
            // The physical reader returns Mode 20 twice; retain wire order for a faithful profile.
            CreateRfMode(20, LlrpC1G2MValue.M4, 64_000, 2_000, 12_500, 23_000, 2_100),
            CreateRfMode(21, LlrpC1G2MValue.M4, 80_000, 1_500, 12_500, 18_800, 2_100),
            CreateRfMode(22, LlrpC1G2MValue.M4, 80_000, 2_000, 12_500, 18_800, 2_100),
            CreateRfMode(23, LlrpC1G2MValue.M4, 0, 0, 0, 0, 0),
            CreateRfMode(24, LlrpC1G2MValue.Fm0, 320_000, 1_500, 12_500, 18_800, 2_100),
            CreateRfMode(25, LlrpC1G2MValue.Fm0, 320_000, 2_000, 12_500, 18_800, 2_100),
            CreateRfMode(26, LlrpC1G2MValue.M8, 30_000, 1_500, 25_000, 25_000, 0),
            CreateRfMode(27, LlrpC1G2MValue.M8, 30_000, 1_500, 12_500, 23_000, 2_100),
            CreateRfMode(28, LlrpC1G2MValue.M8, 30_000, 2_000, 25_000, 25_000, 0),
            CreateRfMode(29, LlrpC1G2MValue.M8, 30_000, 2_000, 12_500, 23_000, 2_100),
            CreateRfMode(30, LlrpC1G2MValue.M8, 32_000, 1_500, 25_000, 25_000, 0),
            CreateRfMode(31, LlrpC1G2MValue.M8, 32_000, 1_500, 12_500, 23_000, 2_100),
            CreateRfMode(32, LlrpC1G2MValue.M8, 32_000, 2_000, 25_000, 25_000, 0),
            CreateRfMode(33, LlrpC1G2MValue.M8, 32_000, 2_000, 12_500, 23_000, 2_100),
            CreateRfMode(34, LlrpC1G2MValue.M8, 40_000, 1_500, 12_500, 18_800, 2_100),
            CreateRfMode(35, LlrpC1G2MValue.M8, 40_000, 2_000, 12_500, 18_800, 2_100),
            CreateRfMode(36, LlrpC1G2MValue.M4, 120_000, 1_500, 10_400, 10_400, 0),
            CreateRfMode(37, LlrpC1G2MValue.M4, 120_000, 2_000, 10_400, 10_400, 0),
            CreateRfMode(38, LlrpC1G2MValue.M4, 160_000, 1_500, 6_250, 10_400, 4_150),
            CreateRfMode(39, LlrpC1G2MValue.Fm0, 0, 0, 0, 0, 0),
            CreateRfMode(40, LlrpC1G2MValue.M2, 160_000, 1_500, 6_250, 6_250, 0),
        ],
    };

    private static LlrpDeviceC1G2RfMode CreateRfMode(
        uint modeIdentifier,
        LlrpC1G2MValue mValue,
        uint bdrValue,
        uint pieValue,
        uint minTariValue,
        uint maxTariValue,
        uint stepTariValue) => new()
    {
        ModeIdentifier = modeIdentifier,
        DrValue = LlrpC1G2DrValue.Dr64_3,
        EpcHagTcConformance = true,
        MValue = mValue,
        ForwardLinkModulation = LlrpC1G2ForwardLinkModulation.PrAsk,
        SpectralMaskIndicator = LlrpC1G2SpectralMaskIndicator.Di,
        BdrValue = bdrValue,
        PieValue = pieValue,
        MinTariValue = minTariValue,
        MaxTariValue = maxTariValue,
        StepTariValue = stepTariValue,
    };

    /// <summary>Creates the captured Zebra 96008 identity used by the default virtual profile.</summary>
    public static LlrpDeviceIdentity CreateDefaultIdentity() => new()
    {
        ReaderId = 1,
        Name = "Virtual Reader",
        ManufacturerId = 161,
        ModelId = 96008,
        FirmwareVersion = "3.32.37.0",
    };

    /// <summary>
    /// Creates the standard virtual-device capabilities backed by the captured Zebra 96008 RF tables.
    /// The standard virtual device exposes four logical antenna ports by default; callers can pass a
    /// different antenna count when they need a custom or exact physical-reader shape.
    /// </summary>
    public static LlrpDeviceCapabilities CreateDefaultCapabilities(ushort maxNumberOfAntennas = 4)
    {
        if (maxNumberOfAntennas == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNumberOfAntennas));
        }

        return new LlrpDeviceCapabilities
        {
            MaxNumberOfAntennas = maxNumberOfAntennas,
            CanSetAntennaProperties = false,
            HasUtcClockCapability = true,
            SupportsStateAwareSingulation = true,
            SupportsReportBuffer = true,
            SupportsEventAndReportHolding = true,
            ReceiveSensitivityLevels = [new LlrpDeviceReceiveSensitivityLevel(0, 0)],
            RegulatoryCapabilities = CreateDefaultRegulatoryCapabilities(),
        };
    }

    /// <summary>Creates one captured RF configuration for every standard virtual antenna.</summary>
    public static IReadOnlyList<LlrpDeviceAntennaConfiguration> CreateDefaultAntennaConfigurations(
        ushort maxNumberOfAntennas = 4)
    {
        if (maxNumberOfAntennas == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNumberOfAntennas));
        }

        return Enumerable
            .Range(1, maxNumberOfAntennas)
            .Select(static id => new LlrpDeviceAntennaConfiguration
            {
                AntennaId = checked((ushort)id),
                ReceiverSensitivityIndex = 0,
                TransmitPowerIndex = 192,
                HopTableId = 1,
                ChannelIndex = 1,
            })
            .ToArray();
    }

    public LlrpDeviceIdentity Identity { get; init; } = CreateDefaultIdentity();

    public LlrpDeviceCapabilities Capabilities { get; init; } = CreateDefaultCapabilities();

    public LlrpDeviceConfiguration Configuration { get; init; } = new()
    {
        Antennas = CreateDefaultAntennaConfigurations(),
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
