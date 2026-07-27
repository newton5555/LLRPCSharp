using LlrpNet.Core.Protocol;

namespace LlrpSdk;

/// <summary>
/// Supplies the initialized-reader facts used to select a configuration default profile.
/// </summary>
public sealed record ReaderConfigurationProfileContext(
    ReaderIdentity Identity,
    ReaderCapabilities Capabilities,
    LlrpProtocolVersion ProtocolVersion,
    IReadOnlyCollection<string> ActiveExtensionIds);

/// <summary>
/// Returns the resolved configuration defaults together with their selected source.
/// </summary>
public sealed record ReaderConfigurationDefaultsResult(
    ReaderConfiguration Configuration,
    string? ProviderId,
    string? ProfileId)
{
    /// <summary>Gets whether no vendor or model-specific profile matched.</summary>
    public bool IsGenericFallback => ProfileId is null;
}

/// <summary>
/// Describes one vendor or model-specific configuration default profile.
/// </summary>
/// <remarks>
/// A profile supplies only the fields it owns. The SDK applies it to the LLRP-safe baseline.
/// Higher values are more specific; two matching profiles at the same priority are an error.
/// </remarks>
public sealed class ReaderConfigurationProfile
{
    /// <summary>Initializes a profile with a stable identifier, specificity priority, and changes.</summary>
    public ReaderConfigurationProfile(string id, int priority, ReaderConfigurationPatch patch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(patch);
        Id = id;
        Priority = priority;
        Patch = patch;
    }

    /// <summary>Gets the stable, globally unique profile identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the profile specificity. A greater value wins over a lower value.</summary>
    public int Priority { get; }

    /// <summary>Gets the fields this profile changes relative to the safe baseline.</summary>
    public ReaderConfigurationPatch Patch { get; }
}

/// <summary>
/// Provides at most one matching configuration default profile for an initialized reader.
/// </summary>
public interface IReaderConfigurationDefaultsProvider
{
    /// <summary>Gets the stable provider identifier used in diagnostics.</summary>
    public string Id { get; }

    /// <summary>Returns the matching profile, or <see langword="null"/> when this provider does not apply.</summary>
    public ReaderConfigurationProfile? GetProfile(ReaderConfigurationProfileContext context);
}

/// <summary>
/// Represents an explicit, partial change to a <see cref="ReaderConfiguration"/>.
/// </summary>
/// <remarks>
/// This type does not communicate with a reader. It is used by default profiles now and will also be used for
/// user-requested configuration changes, so callers need not construct an accidental full-device overwrite.
/// </remarks>
public sealed record ReaderConfigurationPatch
{
    /// <summary>Gets an optional keepalive replacement.</summary>
    public KeepaliveConfiguration? Keepalive { get; init; }

    /// <summary>Gets an optional antenna configuration replacement.</summary>
    public IReadOnlyList<AntennaConfigurationSettings>? Antennas { get; init; }

    /// <summary>Gets an optional GPO configuration replacement.</summary>
    public IReadOnlyList<GpoConfiguration>? Gpos { get; init; }

    /// <summary>Gets an optional event-notification configuration replacement.</summary>
    public EventNotificationConfiguration? Events { get; init; }

    /// <summary>Gets vendor-owned extension values to add to the result.</summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }

    /// <summary>Applies this patch without mutating <paramref name="configuration"/>.</summary>
    public ReaderConfiguration ApplyTo(ReaderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Dictionary<string, object?> extensions = new(configuration.Extensions, StringComparer.Ordinal);
        if (Extensions is not null)
        {
            foreach ((string key, object? value) in Extensions)
            {
                extensions[key] = value;
            }
        }

        return configuration with
        {
            Keepalive = Keepalive ?? configuration.Keepalive,
            Antennas = Antennas ?? configuration.Antennas,
            Gpos = Gpos ?? configuration.Gpos,
            Events = Events ?? configuration.Events,
            Extensions = extensions.Count == 0
                ? configuration.Extensions
                : new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(extensions)
        };
    }
}
