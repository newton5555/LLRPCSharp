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

    public static async Task<(ReaderSettings Settings, SettingsDraftInfo Info)> ResolveSourceAsync(
        LlrpReader reader,
        string source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return source.ToLowerInvariant() switch
        {
            "defaults" => FromDefaults(await reader.GetDefaultSettingsAsync(cancellationToken).ConfigureAwait(false)),
            "reader" => FromReader(await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false)),
            "generic" => (ReaderSettingsDefaults.CreateGeneric().Settings, SettingsDraftInfo.Generic),
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
        await reader.ApplySettingsAsync(settings, cancellationToken).ConfigureAwait(false);
        return await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    public static IEnumerable<IReaderSettingsSerializationContributor> GetSerializationContributors(LlrpReader reader) =>
        reader.Extensions.OfType<IReaderSettingsSerializationContributor>();

    private static (ReaderSettings Settings, SettingsDraftInfo Info) FromDefaults(ReaderSettingsDefaults defaults) =>
        (defaults.Settings, SettingsDraftInfo.FromDefaults(defaults));

    private static (ReaderSettings Settings, SettingsDraftInfo Info) FromReader(ReaderSettingsSnapshot snapshot) =>
        (snapshot.Settings, SettingsDraftInfo.FromReader);
}
