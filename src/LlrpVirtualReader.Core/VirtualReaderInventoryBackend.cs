namespace LlrpVirtualReader;

/// <summary>Deterministic inventory backend used by <see cref="VirtualReaderDeviceBackend"/>.</summary>
public sealed class VirtualReaderInventoryBackend : ILlrpReaderInventoryBackend
{
    private readonly IVirtualTagSource _tagSource;
    private readonly VirtualReaderRfSimulationOptions _options;

    /// <summary>Creates a deterministic backend over one mutable tag source.</summary>
    public VirtualReaderInventoryBackend(
        IVirtualTagSource tagSource,
        VirtualReaderRfSimulationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tagSource);
        _tagSource = tagSource;
        _options = options ?? new VirtualReaderRfSimulationOptions();
    }

    /// <inheritdoc />
    public IReadOnlyList<VirtualTag> Observe(VirtualReaderInventoryRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        VirtualTag[] tags = _tagSource.GetTags().ToArray();
        if (tags.Length == 0)
        {
            return [];
        }

        ushort[] antennas = round.AntennaIds.Count == 0 || round.AntennaIds.Contains((ushort)0)
            ? []
            : round.AntennaIds.Distinct().ToArray();
        var observations = new List<VirtualTag>(tags.Length);
        for (int index = 0; index < tags.Length; index++)
        {
            VirtualTag tag = tags[index];
            if (antennas.Length > 0 && !antennas.Contains(tag.AntennaId))
            {
                continue;
            }

            if (_options.Scenario == VirtualReaderRfScenario.MovingTags &&
                !IsPresentInMovingScenario(index, round.Sequence))
            {
                continue;
            }

            if (_options.Scenario == VirtualReaderRfScenario.Noisy &&
                !PassesDetection(index, round.Sequence))
            {
                continue;
            }

            if (_options.Scenario == VirtualReaderRfScenario.Noisy && _options.RssiJitterDb > 0)
            {
                int jitter = StableRange(index, round.Sequence, _options.RssiJitterDb * 2 + 1) -
                    _options.RssiJitterDb;
                tag = tag with { PeakRssi = checked((short)(tag.PeakRssi + jitter)) };
            }

            observations.Add(tag);
        }

        if (_options.MaxTagsPerRound > 0 && observations.Count > _options.MaxTagsPerRound)
        {
            observations.RemoveRange(_options.MaxTagsPerRound, observations.Count - _options.MaxTagsPerRound);
        }

        return observations;
    }

    /// <inheritdoc />
    public bool TryReadWords(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        int wordPointer,
        int wordCount,
        out IReadOnlyList<ushort> words) =>
        _tagSource.TryReadWords(electronicProductCode, memoryBank, wordPointer, wordCount, out words);

    /// <inheritdoc />
    public bool TryWriteWords(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        int wordPointer,
        IReadOnlyList<ushort> words) =>
        _tagSource.TryWriteWords(electronicProductCode, memoryBank, wordPointer, words);

    /// <inheritdoc />
    public bool TryGetMemoryBytes(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        out ReadOnlyMemory<byte> bytes) =>
        _tagSource.TryGetMemoryBytes(electronicProductCode, memoryBank, out bytes);

    private bool IsPresentInMovingScenario(int tagIndex, int sequence)
    {
        int cycle = _options.PresenceCycleRounds;
        int phase = Math.Abs(sequence / cycle + tagIndex) % 2;
        return phase == 0;
    }

    private bool PassesDetection(int tagIndex, int sequence)
    {
        if (_options.DetectionProbability >= 1)
        {
            return true;
        }

        if (_options.DetectionProbability <= 0)
        {
            return false;
        }

        int sample = StableRange(tagIndex, sequence, 10_000);
        return sample < _options.DetectionProbability * 10_000;
    }

    private int StableRange(int tagIndex, int sequence, int exclusiveMax)
    {
        uint value = unchecked((uint)_options.RandomSeed);
        value = Mix(value, unchecked((uint)tagIndex));
        value = Mix(value, unchecked((uint)sequence));
        return (int)(value % (uint)exclusiveMax);
    }

    private static uint Mix(uint value, uint input)
    {
        value ^= input + 0x9E3779B9u + (value << 6) + (value >> 2);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }
}
