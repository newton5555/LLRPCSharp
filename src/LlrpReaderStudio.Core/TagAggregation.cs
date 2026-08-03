using LlrpSdk;

namespace LlrpReaderStudio.Core;

public sealed record TagObservation(
    string Epc,
    long ReadCount,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    sbyte? LastRssi,
    IReadOnlyList<string> Readers,
    IReadOnlyList<ushort> Antennas);

public sealed class TagAggregateStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, MutableObservation> observations = new(StringComparer.OrdinalIgnoreCase);

    public TagObservation Add(ReaderProfile profile, TagReport report, DateTimeOffset? observedAt = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(report);
        string epc = HexCodec.FormatBytes(report.ElectronicProductCode);
        DateTimeOffset timestamp = observedAt ?? DateTimeOffset.Now;

        lock (gate)
        {
            if (!observations.TryGetValue(epc, out MutableObservation? observation))
            {
                observation = new MutableObservation(epc, timestamp);
                observations.Add(epc, observation);
            }

            observation.ReadCount += Math.Max(1, (int)(report.SeenCount ?? 1));
            observation.LastSeen = timestamp;
            observation.LastRssi = report.PeakRssi;
            observation.Readers.Add(profile.Name);
            if (report.AntennaId is ushort antenna)
            {
                observation.Antennas.Add(antenna);
            }

            return observation.Snapshot();
        }
    }

    public IReadOnlyList<TagObservation> Snapshot()
    {
        lock (gate)
        {
            return observations.Values
                .Select(static observation => observation.Snapshot())
                .OrderByDescending(static observation => observation.LastSeen)
                .ToArray();
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            observations.Clear();
        }
    }

    private sealed class MutableObservation(string epc, DateTimeOffset firstSeen)
    {
        public string Epc { get; } = epc;
        public long ReadCount { get; set; }
        public DateTimeOffset FirstSeen { get; } = firstSeen;
        public DateTimeOffset LastSeen { get; set; } = firstSeen;
        public sbyte? LastRssi { get; set; }
        public HashSet<string> Readers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<ushort> Antennas { get; } = [];

        public TagObservation Snapshot() => new(
            Epc,
            ReadCount,
            FirstSeen,
            LastSeen,
            LastRssi,
            Readers.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            Antennas.OrderBy(static value => value).ToArray());
    }
}
