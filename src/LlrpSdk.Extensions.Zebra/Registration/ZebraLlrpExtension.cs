using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Zebra.Parameters.V1_0_1;
using LlrpSdk;
using LlrpSdk.Extensions;

namespace LlrpSdk.Extensions.Zebra;

/// <summary>Registers the generated Zebra (Moto) LLRP 1.0.1 custom codecs before a reader connects.</summary>
public sealed class ZebraProtocolModule : ILlrpProtocolModule
{
    /// <summary>Gets the singleton Zebra protocol module.</summary>
    public static ZebraProtocolModule Instance { get; } = new();

    /// <inheritdoc />
    public string Id => "zebra-llrp-1.0.1";

    /// <inheritdoc />
    public void Register(LlrpCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        LlrpNet.Protocol.Zebra.Registry.V1_0_1.ZebraProtocolModule.Register(registry);
    }
}

/// <summary>Marks a connected LLRP 1.0.1 reader as a Zebra (Moto) reader and contributes its high-level extensions.</summary>
public sealed class ZebraReaderExtension :
    IReaderExtension,
    IReaderSettingsContributor,
    IInventoryContributor,
    IInventorySettingsContributor,
    IReaderSettingsSerializationContributor,
    ITagReportContributor
{
    /// <summary>Gets the IANA manufacturer identifier assigned to Zebra.</summary>
    public const uint ManufacturerId = 161;

    /// <summary>Gets the singleton reader extension.</summary>
    public static ZebraReaderExtension Instance { get; } = new();

    /// <inheritdoc />
    public string Id => "zebra-reader-llrp-1.0.1";

    /// <inheritdoc />
    public string? MutualExclusionGroup => "reader-vendor";

    /// <inheritdoc />
    public bool Matches(ReaderExtensionMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ManufacturerId == ManufacturerId &&
            context.ProtocolVersion == LlrpProtocolVersion.Version101;
    }
    // Zebra extensions require no enable message; InitializeConnectionAsync keeps its no-op default.

    /// <inheritdoc />
    public IReadOnlyList<ILlrpParameter> BuildQueryParameters() =>
    [
        new MotoGeneralGetParams(0) // RequestedData 0 = All
    ];

    /// <inheritdoc />
    public IReadOnlyList<ILlrpParameter> BuildApplyParameters(ReaderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.Extensions.TryGetValue(ZebraReaderConfiguration.ExtensionKey, out object? value) || value is null)
        {
            return [];
        }
        if (value is not ZebraReaderConfiguration settings)
        {
            throw new ArgumentException(
                $"ReaderConfiguration.Extensions['{ZebraReaderConfiguration.ExtensionKey}'] must be a {nameof(ZebraReaderConfiguration)} instance.");
        }

        var parameters = new List<ILlrpParameter>();
        if (settings.RadioPowerState is bool powerState)
        {
            parameters.Add(new MotoRadioPowerState(powerState));
        }
        if (settings.RadioTransmitDelay is byte transmitDelay)
        {
            parameters.Add(new MotoRadioTransmitDelay(transmitDelay));
        }
        if (settings.AutonomousModeState is bool autonomous)
        {
            parameters.Add(new MotoAutonomousState(autonomous));
        }
        if (settings.SaveConfiguration is bool || settings.SaveTagData is bool || settings.SaveTagEventData is bool)
        {
            parameters.Add(new MotoPersistenceSaveParams(
                settings.SaveConfiguration ?? false,
                settings.SaveTagData ?? false,
                settings.SaveTagEventData ?? false));
        }
        if (settings.EnableNxpSetAndResetQuietCommands is bool nxp)
        {
            parameters.Add(new MotoCustomCommandOptions(nxp));
        }

        return parameters;
    }

    /// <inheritdoc />
    public void ContributeQuery(ReaderSettingsContributionContext context, ReaderConfigurationExtensionBuilder extensions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extensions);

        MotoRadioPowerState? power = context.CustomItems.OfType<MotoRadioPowerState>().FirstOrDefault();
        MotoRadioTransmitDelay? delay = context.CustomItems.OfType<MotoRadioTransmitDelay>().FirstOrDefault();
        MotoAutonomousState? autonomous = context.CustomItems.OfType<MotoAutonomousState>().FirstOrDefault();
        MotoPersistenceSaveParams? persistence = context.CustomItems.OfType<MotoPersistenceSaveParams>().FirstOrDefault();
        MotoCustomCommandOptions? nxp = context.CustomItems.OfType<MotoCustomCommandOptions>().FirstOrDefault();

        extensions.Add(ZebraReaderConfiguration.ExtensionKey, new ZebraReaderConfiguration
        {
            RadioPowerState = power?.RadioPowerState,
            RadioTransmitDelay = delay?.RadioTransmitDelay,
            AutonomousModeState = autonomous?.AutonomousModeState,
            SaveConfiguration = persistence?.SaveConfiguration,
            SaveTagData = persistence?.SaveTagData,
            SaveTagEventData = persistence?.SaveTagEventData,
            EnableNxpSetAndResetQuietCommands = nxp?.EnableNXPSetAndResetQuietCommands,
        });
    }

    /// <inheritdoc />
    public void Contribute(InventoryContributionContext context, InventoryExtensionBuilder extensions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extensions);
        if (!context.Settings.Extensions.TryGetValue(ZebraInventoryReportOptions.ExtensionKey, out object? value) || value is null)
        {
            return;
        }
        if (value is not ZebraInventoryReportOptions options)
        {
            throw new ArgumentException(
                $"InventorySettings.Extensions['{ZebraInventoryReportOptions.ExtensionKey}'] must be a {nameof(ZebraInventoryReportOptions)} instance.");
        }

        extensions.AddRoReportSpecCustomItem(new MotoTagReportContentSelector(
            options.IncludeZoneId,
            options.IncludeZoneName,
            options.IncludeAntennaPhysicalPortConfig,
            options.IncludePhase,
            options.IncludeGps,
            options.IncludeMltReport));
    }

    /// <inheritdoc />
    public void ContributeQuery(InventorySettingsContributionContext context, InventorySettingsExtensionBuilder extensions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extensions);
        MotoTagReportContentSelector? selector = context.RoReportSpecCustomItems
            .OfType<MotoTagReportContentSelector>().SingleOrDefault();
        if (selector is null)
        {
            return;
        }

        extensions.Add(ZebraInventoryReportOptions.ExtensionKey, new ZebraInventoryReportOptions
        {
            IncludeZoneId = selector.EnableZoneID,
            IncludeZoneName = selector.EnableZoneName,
            IncludeAntennaPhysicalPortConfig = selector.EnableAntennaPhysicalPortConfig,
            IncludePhase = selector.EnablePhase,
            IncludeGps = selector.EnableGPS,
            IncludeMltReport = selector.EnableMLTReport,
        });
    }

    /// <inheritdoc />
    public void Contribute(TagReportContributionContext context, TagReportExtensionBuilder extensions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extensions);
        foreach (ILlrpParameter item in context.CustomItems)
        {
            switch (item)
            {
                case MotoTagPhase phase:
                    extensions.Add(ZebraTagReportExtensions.PhaseExtensionKey, phase.Phase);
                    break;
                case MotoTagGPS gps:
                    extensions.Add(ZebraTagReportExtensions.GpsExtensionKey,
                        new ZebraGpsCoordinates(gps.longitude, gps.latitude, gps.altitude));
                    break;
                case MotoC1G2ExtendedPC xpc:
                    extensions.Add(ZebraTagReportExtensions.ExtendedPcExtensionKey,
                        new ZebraExtendedPc(xpc.XPC1, xpc.XPC2));
                    break;
            }
        }
    }

    /// <inheritdoc />
    public bool CanHandle(ReaderSettingsExtensionScope scope, string key, object? value)
    {
        return scope == ReaderSettingsExtensionScope.Configuration &&
            key == ZebraReaderConfiguration.ExtensionKey &&
            (value is null or ZebraReaderConfiguration);
    }

    /// <inheritdoc />
    public JsonNode Serialize(ReaderSettingsExtensionScope scope, string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        object typed = (scope, key) switch
        {
            (ReaderSettingsExtensionScope.Configuration, ZebraReaderConfiguration.ExtensionKey)
                when value is ZebraReaderConfiguration configuration => configuration,
            _ => throw new NotSupportedException($"Zebra does not own settings extension '{key}' at {scope}."),
        };
        return new JsonObject
        {
            ["version"] = 1,
            ["value"] = JsonSerializer.SerializeToNode(typed, typed.GetType(), JsonOptions)
        };
    }

    /// <inheritdoc />
    public object? Deserialize(ReaderSettingsExtensionScope scope, string key, JsonNode value)
    {
        JsonObject document = value.AsObject();
        if (document["version"]?.GetValue<int>() != 1 || document["value"] is null)
        {
            throw new JsonException($"Zebra settings extension '{key}' must declare version 1 and a value.");
        }

        return (scope, key) switch
        {
            (ReaderSettingsExtensionScope.Configuration, ZebraReaderConfiguration.ExtensionKey) =>
                document["value"]!.Deserialize<ZebraReaderConfiguration>(JsonOptions)
                    ?? throw new JsonException("Zebra configuration cannot be null."),
            _ => throw new NotSupportedException($"Zebra does not own settings extension '{key}' at {scope}."),
        };
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>Registers the Zebra protocol module and reader extension in one call.</summary>
public static class ZebraLlrpReaderBuilderExtensions
{
    public static LlrpReaderBuilder UseZebra(this LlrpReaderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .UseProtocolModule(ZebraProtocolModule.Instance)
            .UseReaderExtension(ZebraReaderExtension.Instance);
    }
}
