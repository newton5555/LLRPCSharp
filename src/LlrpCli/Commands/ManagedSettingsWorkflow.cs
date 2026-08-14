using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>Shared managed-settings operations used by interactive and one-shot CLI surfaces.</summary>
internal static class ManagedSettingsWorkflow
{
    public static ReaderSettings Load(LlrpReader reader, string path)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return ReaderSettingsSerializer.LoadFromFile(path, GetSerializationContributors(reader));
    }

    public static void Save(LlrpReader reader, string path, ReaderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ReaderSettingsSerializer.SaveToFile(path, settings, GetSerializationContributors(reader));
    }

    public static async Task<ReaderSettings> ResolveSourceAsync(
        LlrpReader reader,
        string source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return source.ToLowerInvariant() switch
        {
            "defaults" => (await reader.GetDefaultSettingsAsync(cancellationToken).ConfigureAwait(false)).Settings,
            "reader" => (await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false)).Settings,
            "generic" => ReaderSettingsDefaults.CreateGeneric().Settings,
            _ => throw new CliUsageException("Settings source must be defaults, reader, or generic."),
        };
    }

    public static Task<SettingsValidationResult> ValidateAsync(
        LlrpReader reader,
        ReaderSettings settings,
        CancellationToken cancellationToken) =>
        reader.ValidateSettingsAsync(settings, cancellationToken);

    public static async Task<ReaderSettingsSnapshot> ApplyAsync(
        LlrpReader reader,
        ReaderSettings settings,
        CancellationToken cancellationToken)
    {
        SettingsValidationResult validation = await ValidateAsync(reader, settings, cancellationToken).ConfigureAwait(false);
        validation.ThrowIfInvalid();
        return await DeployAsync(reader, settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deploys settings that have already been validated, skipping the validation step to avoid double validation.</summary>
    public static async Task<ReaderSettingsSnapshot> DeployAsync(
        LlrpReader reader,
        ReaderSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        await reader.ApplySettingsAsync(settings, cancellationToken).ConfigureAwait(false);
        return await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    public static IEnumerable<IReaderSettingsSerializationContributor> GetSerializationContributors(LlrpReader reader) =>
        reader.Extensions.OfType<IReaderSettingsSerializationContributor>();
}
