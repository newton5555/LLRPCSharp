using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using Spectre.Console;
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

namespace LlrpCli.Commands;

/// <summary>Interactive editor for the canonical SDK settings records.</summary>
internal sealed class SettingsEditor(IAnsiConsole console, LlrpReader reader)
{
    public ReaderSettings Edit(ReaderSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ReaderSettings working = source;
        while (true)
        {
            SettingsArea area = console.Prompt(new SelectionPrompt<SettingsArea>()
                .Title("[bold yellow]Settings area[/]")
                .AddChoices(Enum.GetValues<SettingsArea>())
                .UseConverter(FormatSettingsArea));

            switch (area)
            {
                case SettingsArea.AntennasAndRf:
                    working = EditAntennasAndRf(working);
                    break;
                case SettingsArea.Singulation:
                    working = EditSingulation(working);
                    break;
                case SettingsArea.Reports:
                    working = EditReports(working);
                    break;
                case SettingsArea.Filters:
                    working = EditFilters(working);
                    break;
                case SettingsArea.StartAndStopTriggers:
                    working = EditTriggers(working);
                    break;
                case SettingsArea.AttachedData:
                    working = EditAttachedData(working);
                    break;
                case SettingsArea.ReaderConfiguration:
                    working = EditReaderConfiguration(working);
                    break;
                case SettingsArea.VendorExtensions:
                    working = EditVendorExtensions(working);
                    break;
                case SettingsArea.ReviewAndFinish:
                    SettingsRenderer.RenderSummary(console, "Settings draft", working);
                    return working;
                case SettingsArea.Cancel:
                    return source;
            }
        }
    }

    private static string FormatSettingsArea(SettingsArea area)
    {
        return area switch
        {
            SettingsArea.AntennasAndRf => "Antennas and RF",
            SettingsArea.Singulation => "Singulation",
            SettingsArea.Reports => "Reports",
            SettingsArea.Filters => "Filters",
            SettingsArea.StartAndStopTriggers => "Start and stop triggers",
            SettingsArea.AttachedData => "Attached data",
            SettingsArea.ReaderConfiguration => "Reader configuration",
            SettingsArea.VendorExtensions => "Vendor extensions",
            SettingsArea.ReviewAndFinish => "[bold green]Review and finish[/]",
            SettingsArea.Cancel => "[bold red]Cancel[/]",
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, null),
        };
    }

    private enum SettingsArea
    {
        AntennasAndRf,
        Singulation,
        Reports,
        Filters,
        StartAndStopTriggers,
        AttachedData,
        ReaderConfiguration,
        VendorExtensions,
        ReviewAndFinish,
        Cancel,
    }

    private ReaderSettings EditAntennasAndRf(ReaderSettings settings)
    {
        InventorySettings inventory = EnsureInventory(settings);
        string antennaText = console.Prompt(new TextPrompt<string>("[grey]Antenna IDs (all or comma-separated):[/]")
            .DefaultValue(FormatAntennaIds(inventory.AntennaIds)));
        IReadOnlyList<ushort> antennas = ParseAntennaIds(antennaText);
        ushort mode = console.Prompt(new TextPrompt<ushort>("[grey]RF ModeIndex (0 = reader default):[/]")
            .DefaultValue(inventory.ModeIndex));
        ushort tari = console.Prompt(new TextPrompt<ushort>("[grey]Tari in ns (0 = reader default):[/]")
            .DefaultValue(inventory.Tari));

        IReadOnlyList<InventoryAntennaConfiguration> antennaConfigurations = inventory.AntennaConfigurations;
        if (console.Confirm("Configure per-antenna RF indexes?", antennaConfigurations.Count != 0))
        {
            var configured = new List<InventoryAntennaConfiguration>();
            foreach (ushort antennaId in antennas)
            {
                InventoryAntennaConfiguration current = antennaConfigurations.FirstOrDefault(item => item.AntennaId == antennaId)
                    ?? new InventoryAntennaConfiguration { AntennaId = antennaId };
                ushort sensitivity = console.Prompt(new TextPrompt<ushort>($"[grey]Antenna {antennaId} Rx sensitivity index (0 = omit):[/]")
                    .DefaultValue(current.ReceiverSensitivityIndex ?? 0));
                ushort transmitPower = console.Prompt(new TextPrompt<ushort>($"[grey]Antenna {antennaId} Tx power index (0 = omit):[/]")
                    .DefaultValue(current.TransmitPowerIndex ?? 0));
                ushort? hopTable = null;
                ushort? channel = null;
                if (transmitPower != 0)
                {
                    hopTable = console.Prompt(new TextPrompt<ushort>($"[grey]Antenna {antennaId} hop table ID:[/]")
                        .DefaultValue(current.HopTableId ?? 1));
                    channel = console.Prompt(new TextPrompt<ushort>($"[grey]Antenna {antennaId} channel index:[/]")
                        .DefaultValue(current.ChannelIndex ?? 1));
                }
                configured.Add(new InventoryAntennaConfiguration
                {
                    AntennaId = antennaId,
                    ReceiverSensitivityIndex = sensitivity == 0 ? null : sensitivity,
                    TransmitPowerIndex = transmitPower == 0 ? null : transmitPower,
                    HopTableId = hopTable,
                    ChannelIndex = channel,
                });
            }
            antennaConfigurations = configured;
        }

        return settings with
        {
            Inventory = inventory with
            {
                AntennaIds = antennas,
                AntennaConfigurations = antennaConfigurations,
                ModeIndex = mode,
                Tari = tari,
            },
        };
    }

    private ReaderSettings EditSingulation(ReaderSettings settings)
    {
        InventorySettings inventory = EnsureInventory(settings);
        byte session = console.Prompt(new SelectionPrompt<byte>()
            .Title("[grey]C1G2 Session:[/]")
            .AddChoices((byte)0, (byte)1, (byte)2, (byte)3)
            .DefaultValue(inventory.Session));
        ushort population = console.Prompt(new TextPrompt<ushort>("[grey]Tag population estimate:[/]")
            .DefaultValue(inventory.TagPopulationEstimate));
        InventoryStateAwareSingulation? stateAware = null;
        if (console.Confirm("Enable state-aware singulation?", inventory.StateAwareSingulation is not null))
        {
            stateAware = new InventoryStateAwareSingulation
            {
                Target = console.Prompt(new SelectionPrompt<InventoryTarget>()
                    .Title("[grey]Inventory target:[/]")
                    .AddChoices(InventoryTarget.StateA, InventoryTarget.StateB)
                    .DefaultValue(inventory.StateAwareSingulation?.Target ?? InventoryTarget.StateA)),
                SelectedFlag = console.Prompt(new SelectionPrompt<InventorySelectedFlag>()
                    .Title("[grey]Selected flag:[/]")
                    .AddChoices(InventorySelectedFlag.Set, InventorySelectedFlag.Clear, InventorySelectedFlag.All)
                    .DefaultValue(inventory.StateAwareSingulation?.SelectedFlag ?? InventorySelectedFlag.Set)),
            };
        }
        return settings with { Inventory = inventory with { Session = session, TagPopulationEstimate = population, StateAwareSingulation = stateAware } };
    }

    private ReaderSettings EditReports(ReaderSettings settings)
    {
        InventorySettings inventory = EnsureInventory(settings);
        InventoryReportTrigger trigger = console.Prompt(new SelectionPrompt<InventoryReportTrigger>()
            .Title("[grey]Report trigger:[/]")
            .AddChoices(InventoryReportTrigger.UponNTagsOrEndOfAiSpec, InventoryReportTrigger.UponNTagsOrEndOfRoSpec, InventoryReportTrigger.None)
            .DefaultValue(inventory.Report.Trigger));
        ushort count = trigger == InventoryReportTrigger.UponNTagsOrEndOfRoSpec && console.Confirm("Report only after the ROSpec stops?", inventory.ReportEveryNTags == 0)
            ? (ushort)0
            : console.Prompt(new TextPrompt<ushort>("[grey]Report every N observed tags:[/]").DefaultValue(Math.Max((ushort)1, inventory.ReportEveryNTags)));
        InventoryReportSettings report = inventory.Report with
        {
            Trigger = trigger,
            IncludeAntennaId = console.Confirm("Include antenna ID?", inventory.Report.IncludeAntennaId),
            IncludePeakRssi = console.Confirm("Include peak RSSI?", inventory.Report.IncludePeakRssi),
            IncludeFirstSeenTimestamp = console.Confirm("Include first-seen timestamp?", inventory.Report.IncludeFirstSeenTimestamp),
            IncludeLastSeenTimestamp = console.Confirm("Include last-seen timestamp?", inventory.Report.IncludeLastSeenTimestamp),
            IncludeTagSeenCount = console.Confirm("Include tag seen count?", inventory.Report.IncludeTagSeenCount),
        };
        return settings with { Inventory = inventory with { ReportEveryNTags = count, Report = report } };
    }

    private ReaderSettings EditFilters(ReaderSettings settings)
    {
        InventorySettings inventory = EnsureInventory(settings);
        string operation = console.Prompt(new SelectionPrompt<string>()
            .Title($"[grey]Current filters: {inventory.Filters.Count}[/]")
            .AddChoices("Add filter", "Clear filters", "Keep filters"));
        if (operation == "Keep filters")
        {
            return settings;
        }
        if (operation == "Clear filters")
        {
            return settings with { Inventory = inventory with { Filters = [] } };
        }

        ushort bank = console.Prompt(new SelectionPrompt<ushort>()
            .Title("[grey]Memory bank:[/]")
            .AddChoices((ushort)0, (ushort)1, (ushort)2, (ushort)3)
            .DefaultValue((ushort)1));
        ushort pointer = console.Prompt(new TextPrompt<ushort>("[grey]Bit pointer:[/]").DefaultValue((ushort)32));
        string maskHex = console.Prompt(new TextPrompt<string>("[grey]Mask hex:[/]")).Trim();
        byte[] mask;
        try
        {
            mask = Convert.FromHexString(maskHex);
        }
        catch (FormatException exception)
        {
            throw new CliUsageException($"Filter mask must be hexadecimal: {exception.Message}");
        }
        ushort bitLength = console.Prompt(new TextPrompt<ushort>("[grey]Meaningful mask bits (0 = all):[/]").DefaultValue((ushort)0));
        var filter = new InventorySelectFilter
        {
            MemoryBank = bank,
            BitPointer = pointer,
            Mask = mask,
            BitLength = bitLength,
        };
        return settings with { Inventory = inventory with { Filters = inventory.Filters.Append(filter).ToArray() } };
    }

    private ReaderSettings EditTriggers(ReaderSettings settings)
    {
        InventorySettings inventory = EnsureInventory(settings);
        InventoryStartTriggerType startType = console.Prompt(new SelectionPrompt<InventoryStartTriggerType>()
            .Title("[grey]Start trigger:[/]")
            .AddChoices(InventoryStartTriggerType.None, InventoryStartTriggerType.Immediate, InventoryStartTriggerType.Periodic, InventoryStartTriggerType.Gpi)
            .DefaultValue(inventory.StartTrigger.Type));
        InventoryStartTrigger start = startType switch
        {
            InventoryStartTriggerType.Periodic => new InventoryStartTrigger
            {
                Type = startType,
                OffsetMilliseconds = console.Prompt(new TextPrompt<uint>("[grey]Periodic offset ms:[/]").DefaultValue(inventory.StartTrigger.OffsetMilliseconds)),
                PeriodMilliseconds = console.Prompt(new TextPrompt<uint>("[grey]Periodic period ms:[/]").DefaultValue(Math.Max(1U, inventory.StartTrigger.PeriodMilliseconds))),
                StartAtUtc = inventory.StartTrigger.StartAtUtc,
            },
            InventoryStartTriggerType.Gpi => new InventoryStartTrigger
            {
                Type = startType,
                GpiPortNumber = console.Prompt(new TextPrompt<ushort>("[grey]Start GPI port:[/]").DefaultValue(Math.Max((ushort)1, inventory.StartTrigger.GpiPortNumber))),
                GpiState = console.Confirm("Start on high GPI state?", inventory.StartTrigger.GpiState),
                TimeoutMilliseconds = console.Prompt(new TextPrompt<uint>("[grey]Start GPI timeout ms (0 = none):[/]").DefaultValue(inventory.StartTrigger.TimeoutMilliseconds)),
            },
            _ => new InventoryStartTrigger { Type = startType },
        };

        InventoryStopTriggerType stopType = console.Prompt(new SelectionPrompt<InventoryStopTriggerType>()
            .Title("[grey]Stop trigger:[/]")
            .AddChoices(InventoryStopTriggerType.None, InventoryStopTriggerType.Duration, InventoryStopTriggerType.GpiWithTimeout)
            .DefaultValue(inventory.StopTrigger.Type));
        InventoryStopTrigger stop = stopType switch
        {
            InventoryStopTriggerType.Duration => new InventoryStopTrigger
            {
                Type = stopType,
                DurationMilliseconds = console.Prompt(new TextPrompt<uint>("[grey]Duration ms:[/]").DefaultValue(Math.Max(1U, inventory.StopTrigger.DurationMilliseconds))),
            },
            InventoryStopTriggerType.GpiWithTimeout => new InventoryStopTrigger
            {
                Type = stopType,
                GpiPortNumber = console.Prompt(new TextPrompt<ushort>("[grey]Stop GPI port:[/]").DefaultValue(Math.Max((ushort)1, inventory.StopTrigger.GpiPortNumber))),
                GpiState = console.Confirm("Stop on high GPI state?", inventory.StopTrigger.GpiState),
                TimeoutMilliseconds = console.Prompt(new TextPrompt<uint>("[grey]Stop GPI timeout ms:[/]").DefaultValue(inventory.StopTrigger.TimeoutMilliseconds)),
            },
            _ => new InventoryStopTrigger { Type = stopType },
        };
        return settings with { Inventory = inventory with { StartTrigger = start, StopTrigger = stop } };
    }

    private ReaderSettings EditAttachedData(ReaderSettings settings)
    {
        InventorySettings inventory = EnsureInventory(settings);
        if (!console.Confirm("Enable AttachedData read?", inventory.AttachedData.Enabled))
        {
            return settings with { Inventory = inventory with { AttachedData = inventory.AttachedData with { Enabled = false } } };
        }
        var attached = new AttachedDataOptions
        {
            Enabled = true,
            MemoryBank = console.Prompt(new SelectionPrompt<ushort>().Title("[grey]Memory bank:[/]").AddChoices((ushort)0, (ushort)1, (ushort)2, (ushort)3).DefaultValue(inventory.AttachedData.MemoryBank)),
            WordPointer = console.Prompt(new TextPrompt<ushort>("[grey]Word pointer:[/]").DefaultValue(inventory.AttachedData.WordPointer)),
            WordCount = console.Prompt(new TextPrompt<ushort>("[grey]Word count:[/]").DefaultValue(inventory.AttachedData.WordCount)),
            AccessPassword = console.Prompt(new TextPrompt<string>("[grey]Access password (8 hex digits):[/]").DefaultValue(inventory.AttachedData.AccessPassword)).ToUpperInvariant(),
        };
        return settings with { Inventory = inventory with { AttachedData = attached } };
    }

    private ReaderSettings EditReaderConfiguration(ReaderSettings settings)
    {
        ReaderConfiguration configuration = settings.Configuration;
        LlrpSdk.KeepaliveTriggerType type = console.Prompt(new SelectionPrompt<LlrpSdk.KeepaliveTriggerType>()
            .Title("[grey]Keepalive:[/]")
            .AddChoices(LlrpSdk.KeepaliveTriggerType.None, LlrpSdk.KeepaliveTriggerType.Periodic)
            .DefaultValue(configuration.Keepalive.TriggerType));
        uint interval = type == LlrpSdk.KeepaliveTriggerType.Periodic
            ? console.Prompt(new TextPrompt<uint>("[grey]Keepalive interval ms:[/]").DefaultValue(Math.Max(1U, configuration.Keepalive.IntervalMs)))
            : 0;
        return settings with { Configuration = configuration with { Keepalive = new KeepaliveConfiguration { TriggerType = type, IntervalMs = interval } } };
    }

    private ReaderSettings EditVendorExtensions(ReaderSettings settings)
    {
        InventorySettings inventory = EnsureInventory(settings);
        if (!reader.Extensions.Any(static extension => extension is ImpinjReaderExtension))
        {
            console.MarkupLine("[yellow]No editable vendor inventory extension is active for this reader.[/]");
            return settings;
        }

        inventory.Extensions.TryGetValue(ImpinjInventoryReportOptions.ExtensionKey, out object? reportValue);
        inventory.Extensions.TryGetValue(ImpinjInventoryControlOptions.ExtensionKey, out object? controlValue);
        var report = reportValue as ImpinjInventoryReportOptions ?? new ImpinjInventoryReportOptions();
        var control = controlValue as ImpinjInventoryControlOptions ?? new ImpinjInventoryControlOptions();
        bool serializedTid = console.Confirm("Include Impinj Serialized TID?", report.IncludeSerializedTid);
        bool phase = console.Confirm("Include Impinj RF phase angle?", report.IncludeRfPhaseAngle);
        bool peakRssi = console.Confirm("Include Impinj peak RSSI?", report.IncludePeakRssi);
        bool population = console.Confirm("Enable Impinj tag population estimation?", control.EnableTagPopulationEstimation == true);
        inventory = inventory.Edit(builder => builder.Impinj(impinj =>
        {
            impinj
                .IncludeSerializedTid(serializedTid)
                .IncludeRfPhaseAngle(phase)
                .IncludePeakRssi(peakRssi);
            if (population)
            {
                impinj.EnableTagPopulationEstimation();
            }
            else
            {
                impinj.DisableTagPopulationEstimation();
            }
        }));
        return settings with { Inventory = inventory };
    }

    private static InventorySettings EnsureInventory(ReaderSettings settings) => settings.Inventory ?? new InventorySettings();

    private static string FormatAntennaIds(IReadOnlyList<ushort> antennaIds) =>
        antennaIds.Count == 1 && antennaIds[0] == 0 ? "all" : string.Join(',', antennaIds);

    internal static IReadOnlyList<ushort> ParseAntennaIds(string value)
    {
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return [0];
        }
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !parts.All(static item => ushort.TryParse(item, out _)))
        {
            throw new CliUsageException("Antenna IDs must be all or a comma-separated list of UInt16 values.");
        }
        ushort[] parsed = parts.Select(static item => ushort.Parse(item)).Distinct().ToArray();
        if (parsed.Contains((ushort)0))
        {
            throw new CliUsageException("Antenna ID 0 selects all antennas; use all instead of combining it with explicit IDs.");
        }
        return parsed;
    }
}
