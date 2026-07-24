using LlrpNet.Core.Protocol;

namespace LlrpSdk.Extensions;

/// <summary>Describes a reader-level extension that may be activated after standard reader initialization.</summary>
/// <remarks>
/// This is separate from <see cref="ILlrpProtocolModule"/>. Protocol modules register codecs before connection;
/// reader extensions are matched only after the standard identity and capabilities are available.
/// </remarks>
public interface IReaderExtension
{
    /// <summary>Gets a stable, globally unique extension identifier.</summary>
    public string Id { get; }

    /// <summary>
    /// Gets the optional mutual-exclusion group, such as <c>reader-vendor</c>.
    /// </summary>
    /// <remarks>At most one matching extension in a non-empty group may activate for one reader.</remarks>
    public string? MutualExclusionGroup { get; }

    /// <summary>Determines whether this extension applies to the initialized reader.</summary>
    /// <param name="context">The immutable standard identity and negotiated-version context.</param>
    /// <returns><see langword="true"/> when the extension should activate.</returns>
    public bool Matches(ReaderExtensionMatchContext context);
}

/// <summary>Supplies standard reader identity to extension matching without exposing a reader session.</summary>
public sealed record ReaderExtensionMatchContext(
    uint ManufacturerId,
    uint ModelId,
    string FirmwareVersion,
    LlrpProtocolVersion ProtocolVersion);
