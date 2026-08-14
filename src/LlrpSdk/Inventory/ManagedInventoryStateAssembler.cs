using LlrpNet.Core.Protocol;
using LlrpSdk.Extensions;

namespace LlrpSdk;

/// <summary>
/// Version-neutral reverse-assembly of a managed inventory snapshot: runs the extension contributor query
/// pipeline against the parsed ROSpec custom parameters and produces the final snapshot.
/// </summary>
internal static class ManagedInventoryStateAssembler
{
    public static ManagedRoSpecSnapshot Assemble(
        ParsedManagedRoSpec parsed,
        ReaderIdentity identity,
        ReaderCapabilities capabilities,
        LlrpProtocolVersion protocolVersion,
        IEnumerable<IInventorySettingsContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(contributors);

        var extensionBuilder = new InventorySettingsExtensionBuilder();
        var contributionContext = new InventorySettingsContributionContext(
            identity,
            capabilities,
            protocolVersion,
            parsed.ReportCustomItems,
            parsed.CommandCustomItems);
        foreach (IInventorySettingsContributor contributor in contributors)
        {
            contributor.ContributeQuery(contributionContext, extensionBuilder);
        }

        return new ManagedRoSpecSnapshot(
            parsed.Settings with { Extensions = extensionBuilder.Build() },
            parsed.State);
    }

    /// <summary>Converts LLRP UTC microseconds into a <see cref="DateTimeOffset"/>.</summary>
    internal static DateTimeOffset FromUtcMicroseconds(ulong microseconds)
    {
        try
        {
            return DateTimeOffset.UnixEpoch.AddTicks(checked((long)microseconds * TimeSpan.TicksPerMicrosecond));
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                "The reserved SDK ROSpec contains an out-of-range UTC start timestamp.",
                exception);
        }
    }

    /// <summary>Packs an LLRP bit list into big-endian bytes; the first bit becomes the MSB of the first byte.</summary>
    internal static byte[] BitsToBytes(IReadOnlyList<bool> bits) => bits.Chunk(8)
        .Select(group => Convert.ToByte(group.Select((bit, index) => bit ? 1 << (7 - index) : 0).Sum())).ToArray();
}
