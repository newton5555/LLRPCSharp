namespace LlrpSdk;

/// <summary>
/// Normalizes the standard "all antennas" shorthand before a managed ROSpec is deployed.
/// </summary>
/// <remarks>
/// LLRP defines antenna identifier 0 as all antennas. Some readers accept that value while
/// adding a ROSpec but cannot execute the resulting inventory, so the SDK expands it to the
/// explicit antenna identifiers advertised by the reader. The protocol compilers require the
/// normalized, non-zero form.
/// </remarks>
internal static class InventorySettingsNormalizer
{
    public static InventorySettings ExpandAllAntennas(InventorySettings settings, ushort maxAntennas)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.AntennaIds.Count == 0)
        {
            throw new ArgumentException("At least one antenna ID is required.", nameof(settings));
        }

        if (settings.AntennaIds.Count > 1 && settings.AntennaIds.Contains((ushort)0))
        {
            throw new ArgumentException(
                "Antenna ID 0 cannot be combined with explicit antenna IDs.",
                nameof(settings));
        }

        bool selectsAllAntennas = settings.AntennaIds[0] == 0;
        if (selectsAllAntennas && maxAntennas == 0)
        {
            throw new InvalidOperationException(
                "The reader did not advertise an antenna count, so antenna ID 0 cannot be expanded safely.");
        }

        InventoryAntennaConfiguration? commonConfiguration = settings.AntennaConfigurations
            .FirstOrDefault(static configuration => configuration.AntennaId == 0);
        if (!selectsAllAntennas && commonConfiguration is null)
        {
            return settings;
        }

        ushort[] antennaIds = selectsAllAntennas
            ? Enumerable.Range(1, maxAntennas)
                .Select(static antennaId => checked((ushort)antennaId))
                .ToArray()
            : settings.AntennaIds.ToArray();
        if (antennaIds.Length == 0)
        {
            throw new InvalidOperationException("At least one explicit antenna ID is required.");
        }

        Dictionary<ushort, InventoryAntennaConfiguration> explicitConfigurations = settings.AntennaConfigurations
            .Where(static configuration => configuration.AntennaId > 0)
            .Where(configuration => antennaIds.Contains(configuration.AntennaId))
            .GroupBy(static configuration => configuration.AntennaId)
            .ToDictionary(static group => group.Key, static group => group.First());

        InventoryAntennaConfiguration[] configurations = commonConfiguration is not null
            ? antennaIds
                .Select(antennaId => explicitConfigurations.TryGetValue(antennaId, out InventoryAntennaConfiguration? explicitConfiguration)
                    ? explicitConfiguration
                    : commonConfiguration with { AntennaId = antennaId })
                .ToArray()
            : explicitConfigurations.Values
                .OrderBy(static configuration => configuration.AntennaId)
                .ToArray();

        return settings with
        {
            AntennaIds = antennaIds,
            AntennaConfigurations = configurations,
        };
    }
}
