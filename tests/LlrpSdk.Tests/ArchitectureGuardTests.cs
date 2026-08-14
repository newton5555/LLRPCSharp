using System.Text.RegularExpressions;
using Xunit;

namespace LlrpSdk.Tests;

/// <summary>
/// Machine-enforces the "adapter is the only version boundary" rule: the reader facade must not reference
/// versioned protocol types or aliases. Adding a new LLRP version must not touch <c>LlrpReader.cs</c>.
/// </summary>
public sealed class ArchitectureGuardTests
{
    [Fact]
    public void LlrpReader_ContainsNoVersionedProtocolReferences()
    {
        string facadePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "LlrpSdk", "Reader", "LlrpReader.cs"));
        Assert.True(File.Exists(facadePath), $"Facade source not found at '{facadePath}'.");

        string source = File.ReadAllText(facadePath);
        MatchCollection matches = Regex.Matches(source, @"\bV(101|11|1_0_1|1_1|20|2_0)\b");
        string found = string.Join(", ", matches.Select(static match => match.Value).Distinct());
        Assert.True(
            matches.Count == 0,
            $"LlrpReader.cs must not reference versioned protocol types or aliases. Found: {found}");
    }

    [Fact]
    public void LlrpReader_DelegatesVersionTranslationThroughTheAdapterBoundary()
    {
        // The version boundary components the facade is allowed to talk to are the adapter, the event projector
        // dispatcher, the message factory, and the pre-adapter version negotiator.
        string facadePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "LlrpSdk", "Reader", "LlrpReader.cs"));
        string source = File.ReadAllText(facadePath);

        Assert.Contains("GetProtocolAdapter()", source);
        Assert.Contains("ReaderEventProjector.Project", source);
        Assert.Contains("LlrpProtocolMessageFactory.", source);
        Assert.Contains("LlrpVersionNegotiator.NegotiateAsync", source);
    }
}
