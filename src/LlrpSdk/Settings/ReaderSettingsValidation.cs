using LlrpNet.Core.Protocol;

namespace LlrpSdk;

/// <summary>Identifies the severity of one managed-settings diagnostic.</summary>
public enum SettingsDiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>Describes one actionable managed-settings problem.</summary>
public sealed record SettingsDiagnostic(
    string Code,
    SettingsDiagnosticSeverity Severity,
    string Path,
    string Message);

/// <summary>Contains side-effect-free validation results for managed reader settings.</summary>
public sealed record SettingsValidationResult
{
    public IReadOnlyList<SettingsDiagnostic> Diagnostics { get; init; } = Array.Empty<SettingsDiagnostic>();

    public bool IsValid => Diagnostics.All(static diagnostic => diagnostic.Severity != SettingsDiagnosticSeverity.Error);

    /// <summary>Throws with the complete diagnostic set when validation failed.</summary>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new SettingsValidationException(Diagnostics);
        }
    }
}

/// <summary>Thrown when managed settings cannot be compiled for the connected reader.</summary>
public sealed class SettingsValidationException : InvalidOperationException
{
    public SettingsValidationException(IReadOnlyList<SettingsDiagnostic> diagnostics)
        : base(CreateMessage(diagnostics))
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public IReadOnlyList<SettingsDiagnostic> Diagnostics { get; }

    private static string CreateMessage(IReadOnlyList<SettingsDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        SettingsDiagnostic? first = diagnostics.FirstOrDefault(static item => item.Severity == SettingsDiagnosticSeverity.Error);
        return first is null ? "Reader settings validation failed." : $"Reader settings validation failed: {first.Message}";
    }
}

internal static class ReaderSettingsValidator
{
    public static List<SettingsDiagnostic> Validate(
        ReaderSettings settings,
        ReaderCapabilities? capabilities,
        LlrpProtocolVersion protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var diagnostics = new List<SettingsDiagnostic>();
        ValidateConfiguration(settings.Configuration, diagnostics);
        if (settings.Inventory is { } inventory)
        {
            ValidateInventory(inventory, capabilities, protocolVersion, diagnostics);
        }
        return diagnostics;
    }

    private static void ValidateConfiguration(ReaderConfiguration? configuration, List<SettingsDiagnostic> diagnostics)
    {
        if (configuration is null)
        {
            AddError(diagnostics, "SET-CONFIG-001", "configuration", "Reader configuration is required.");
            return;
        }
        if (configuration.Keepalive is null)
        {
            AddError(diagnostics, "SET-CONFIG-002", "configuration.keepalive", "Keepalive configuration is required.");
        }
        else if (configuration.Keepalive.TriggerType == KeepaliveTriggerType.Periodic && configuration.Keepalive.IntervalMs == 0)
        {
            AddError(diagnostics, "SET-CONFIG-003", "configuration.keepalive.intervalMs", "Periodic keepalive requires a positive interval.");
        }

        ValidateUniquePositiveIds(
            configuration.Gpos,
            static value => value.GpoPortNumber,
            "configuration.gpos",
            "SET-CONFIG-004",
            "GPO port numbers must be non-zero and unique.",
            diagnostics);
    }

    private static void ValidateInventory(
        InventorySettings inventory,
        ReaderCapabilities? capabilities,
        LlrpProtocolVersion protocolVersion,
        List<SettingsDiagnostic> diagnostics)
    {
        if (inventory.InventoryParameterSpecId == 0)
        {
            AddError(diagnostics, "SET-INV-001", "inventory.inventoryParameterSpecId", "Inventory parameter specification ID must be non-zero.");
        }
        if (inventory.Report is null)
        {
            AddError(diagnostics, "SET-INV-002", "inventory.report", "Inventory report settings are required.");
        }
        else if (inventory.ReportEveryNTags == 0 && inventory.Report.Trigger != InventoryReportTrigger.UponNTagsOrEndOfRoSpec)
        {
            AddError(diagnostics, "SET-INV-003", "inventory.reportEveryNTags", "A zero report interval is valid only for reporting at the end of the ROSpec.");
        }
        if (inventory.TagPopulationEstimate == 0)
        {
            AddError(diagnostics, "SET-INV-004", "inventory.tagPopulationEstimate", "Tag population estimate must be positive.");
        }
        if (inventory.Session > 3)
        {
            AddError(diagnostics, "SET-INV-005", "inventory.session", "C1G2 session must be between 0 and 3.");
        }

        IReadOnlyList<ushort>? antennaIds = inventory.AntennaIds;
        if (antennaIds is null || antennaIds.Count == 0)
        {
            AddError(diagnostics, "SET-INV-006", "inventory.antennaIds", "At least one antenna is required; use 0 to select all antennas.");
        }
        else
        {
            if (antennaIds.Count > 1 && antennaIds.Contains((ushort)0))
            {
                AddError(diagnostics, "SET-INV-007", "inventory.antennaIds", "Antenna 0 selects all antennas and cannot be combined with explicit IDs.");
            }
            if (antennaIds.Distinct().Count() != antennaIds.Count)
            {
                AddError(diagnostics, "SET-INV-008", "inventory.antennaIds", "Inventory antenna IDs must be unique.");
            }
            if (capabilities is { MaxNumberOfAntennas: > 0 } && antennaIds.Any(id => id > capabilities.MaxNumberOfAntennas))
            {
                AddError(diagnostics, "SET-INV-009", "inventory.antennaIds", $"The reader reports at most {capabilities.MaxNumberOfAntennas} antennas.");
            }
        }

        IReadOnlyList<InventoryAntennaConfiguration>? antennaConfigurations = inventory.AntennaConfigurations;
        if (antennaConfigurations is null)
        {
            AddError(diagnostics, "SET-INV-010", "inventory.antennaConfigurations", "Inventory antenna configurations cannot be null.");
        }
        else
        {
            ushort[] configuredIds = antennaConfigurations.Select(static item => item.AntennaId).ToArray();
            if (configuredIds.Distinct().Count() != configuredIds.Length || (configuredIds.Contains((ushort)0) && configuredIds.Length != 1))
            {
                AddError(diagnostics, "SET-INV-011", "inventory.antennaConfigurations", "Antenna configurations must have unique IDs; antenna 0 cannot be mixed with explicit IDs.");
            }
            for (int index = 0; index < antennaConfigurations.Count; index++)
            {
                InventoryAntennaConfiguration item = antennaConfigurations[index];
                string path = $"inventory.antennaConfigurations[{index}]";
                bool hasAnyTransmitter = item.TransmitPowerIndex.HasValue || item.HopTableId.HasValue || item.ChannelIndex.HasValue;
                if (hasAnyTransmitter && (!item.TransmitPowerIndex.HasValue || !item.HopTableId.HasValue || !item.ChannelIndex.HasValue))
                {
                    AddError(diagnostics, "SET-INV-012", path, "Transmit power, hop table, and channel index must be supplied together.");
                }
                if (antennaIds is { Count: > 0 } && item.AntennaId != 0 && !antennaIds.Contains((ushort)0) && !antennaIds.Contains(item.AntennaId))
                {
                    AddError(diagnostics, "SET-INV-013", path + ".antennaId", "The antenna configuration must target an antenna selected for inventory.");
                }
            }
        }

        ValidateFilters(inventory, capabilities, protocolVersion, diagnostics);
        ValidateTriggers(inventory, capabilities, diagnostics);
        ValidateAttachedData(inventory.AttachedData, diagnostics);
    }

    private static void ValidateFilters(
        InventorySettings inventory,
        ReaderCapabilities? capabilities,
        LlrpProtocolVersion protocolVersion,
        List<SettingsDiagnostic> diagnostics)
    {
        if (inventory.Filters is null)
        {
            AddError(diagnostics, "SET-INV-014", "inventory.filters", "Inventory filters cannot be null.");
            return;
        }

        bool hasStateAwareFilter = false;
        for (int index = 0; index < inventory.Filters.Count; index++)
        {
            InventorySelectFilter filter = inventory.Filters[index];
            string path = $"inventory.filters[{index}]";
            int bitLength = filter.BitLength == 0 ? checked(filter.Mask.Length * 8) : filter.BitLength;
            if (filter.MemoryBank > 3)
            {
                AddError(diagnostics, "SET-INV-015", path + ".memoryBank", "Filter memory bank must be between 0 and 3.");
            }
            if (filter.Mask.IsEmpty || bitLength <= 0 || bitLength > filter.Mask.Length * 8)
            {
                AddError(diagnostics, "SET-INV-016", path + ".mask", "Filter mask and bit length must describe at least one valid bit.");
            }
            hasStateAwareFilter |= filter.StateAwareAction is not null;
        }

        if (hasStateAwareFilter && inventory.StateAwareSingulation is null)
        {
            AddError(diagnostics, "SET-INV-017", "inventory.stateAwareSingulation", "State-aware filters require state-aware singulation settings.");
        }
        if ((hasStateAwareFilter || inventory.StateAwareSingulation is not null) && capabilities?.CanDoTagInventoryStateAwareSingulation != true)
        {
            AddError(diagnostics, "SET-INV-018", "inventory.stateAwareSingulation", "The connected reader does not advertise state-aware singulation support.");
        }
        if (inventory.StateAwareSingulation?.SelectedFlag == InventorySelectedFlag.All && protocolVersion == LlrpProtocolVersion.Version101)
        {
            AddError(diagnostics, "SET-INV-019", "inventory.stateAwareSingulation.selectedFlag", "SelectedFlag.All requires LLRP 1.1.");
        }
    }

    private static void ValidateTriggers(
        InventorySettings inventory,
        ReaderCapabilities? capabilities,
        List<SettingsDiagnostic> diagnostics)
    {
        if (inventory.StartTrigger is null)
        {
            AddError(diagnostics, "SET-INV-020", "inventory.startTrigger", "Inventory start trigger is required.");
        }
        else
        {
            if (inventory.StartTrigger.Type == InventoryStartTriggerType.Periodic && inventory.StartTrigger.PeriodMilliseconds == 0)
            {
                AddError(diagnostics, "SET-INV-021", "inventory.startTrigger.periodMilliseconds", "Periodic start requires a positive period.");
            }
            if (inventory.StartTrigger.Type == InventoryStartTriggerType.Gpi && inventory.StartTrigger.GpiPortNumber == 0)
            {
                AddError(diagnostics, "SET-INV-022", "inventory.startTrigger.gpiPortNumber", "GPI start requires a non-zero port number.");
            }
            if (inventory.StartTrigger.StartAtUtc is not null && capabilities?.HasUtcClockCapability != true)
            {
                AddError(diagnostics, "SET-INV-023", "inventory.startTrigger.startAtUtc", "A UTC start time requires a reader with UTC clock capability.");
            }
        }

        if (inventory.StopTrigger is null)
        {
            AddError(diagnostics, "SET-INV-024", "inventory.stopTrigger", "Inventory stop trigger is required.");
        }
        else
        {
            if (inventory.StopTrigger.Type == InventoryStopTriggerType.Duration && inventory.StopTrigger.DurationMilliseconds == 0)
            {
                AddError(diagnostics, "SET-INV-025", "inventory.stopTrigger.durationMilliseconds", "Duration stop requires a positive duration.");
            }
            if (inventory.StopTrigger.Type == InventoryStopTriggerType.GpiWithTimeout && inventory.StopTrigger.GpiPortNumber == 0)
            {
                AddError(diagnostics, "SET-INV-026", "inventory.stopTrigger.gpiPortNumber", "GPI stop requires a non-zero port number.");
            }
        }
    }

    private static void ValidateAttachedData(AttachedDataOptions? options, List<SettingsDiagnostic> diagnostics)
    {
        if (options is null)
        {
            AddError(diagnostics, "SET-INV-027", "inventory.attachedData", "Attached-data options are required.");
            return;
        }
        if (!options.Enabled)
        {
            return;
        }
        if (options.MemoryBank > (ushort)TagMemoryBank.User)
        {
            AddError(diagnostics, "SET-INV-028", "inventory.attachedData.memoryBank", "Attached-data memory bank must be between 0 and 3.");
        }
        if (options.WordCount == 0)
        {
            AddError(diagnostics, "SET-INV-029", "inventory.attachedData.wordCount", "Attached-data word count must be positive.");
        }
        if (options.AccessPassword is null || options.AccessPassword.Length != 8 ||
            !uint.TryParse(options.AccessPassword, System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            AddError(diagnostics, "SET-INV-030", "inventory.attachedData.accessPassword", "Attached-data access password must contain eight hexadecimal digits.");
        }
    }

    private static void ValidateUniquePositiveIds<T>(
        IReadOnlyList<T>? values,
        Func<T, ushort> selectId,
        string path,
        string code,
        string message,
        List<SettingsDiagnostic> diagnostics)
    {
        if (values is null || values.Any(item => selectId(item) == 0) || values.Select(selectId).Distinct().Count() != values.Count)
        {
            AddError(diagnostics, code, path, message);
        }
    }

    private static void AddError(List<SettingsDiagnostic> diagnostics, string code, string path, string message) =>
        diagnostics.Add(new SettingsDiagnostic(code, SettingsDiagnosticSeverity.Error, path, message));
}
