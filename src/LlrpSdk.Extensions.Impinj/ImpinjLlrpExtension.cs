using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions;
using LlrpSdk.Extensions.Impinj.Enumerations.V1_0_1;
using LlrpSdk.Extensions.Impinj.Messages.V1_0_1;
using LlrpSdk.Extensions.Impinj.Parameters.V1_0_1;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LlrpSdk.Extensions.Impinj;

/// <summary>Registers the generated Impinj LLRP 1.0.1 custom codecs before a reader connects.</summary>
public sealed class ImpinjProtocolModule : ILlrpProtocolModule
{
    /// <summary>Gets the singleton Impinj protocol module.</summary>
    public static ImpinjProtocolModule Instance { get; } = new();

    /// <inheritdoc />
    public string Id => "impinj-llrp-1.0.1";

    /// <inheritdoc />
    public void Register(LlrpCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry.V1_0_1.ImpinjProtocolModule.Register(registry);
    }
}

/// <summary>Marks a connected LLRP 1.0.1 reader as an Impinj reader.</summary>
public sealed class ImpinjReaderExtension :
    IReaderExtension,
    IReaderSettingsContributor,
    IInventoryContributor,
    IInventorySettingsContributor,
    IReaderSettingsSerializationContributor,
    ITagReportContributor
{
    /// <summary>Gets the IANA manufacturer identifier assigned to Impinj.</summary>
    public const uint ManufacturerId = 25882;

    /// <summary>Gets the singleton reader extension.</summary>
    public static ImpinjReaderExtension Instance { get; } = new();

    /// <inheritdoc />
    public string Id => "impinj-reader-llrp-1.0.1";

    /// <inheritdoc />
    public string? MutualExclusionGroup => "reader-vendor";

    /// <inheritdoc />
    public bool Matches(ReaderExtensionMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ManufacturerId == ManufacturerId &&
            context.ProtocolVersion == LlrpProtocolVersion.Version101;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task InitializeConnectionAsync(
        IReaderConnection connection,
        System.Threading.CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var enableMsg = new IMPINJ_ENABLE_EXTENSIONS(connection.NextMessageId(), []);
        await connection.TransactAsync<IMPINJ_ENABLE_EXTENSIONS_RESPONSE>(enableMsg, timeout: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool CanHandle(ReaderSettingsExtensionScope scope, string key, object? value)
    {
        return (scope, key) switch
        {
            (ReaderSettingsExtensionScope.Configuration, ImpinjReaderConfiguration.ExtensionKey) =>
                value is null or ImpinjReaderConfiguration,
            (ReaderSettingsExtensionScope.Configuration, ImpinjReaderFacts.ExtensionKey) =>
                value is null or ImpinjReaderFacts,
            (ReaderSettingsExtensionScope.Inventory, ImpinjInventoryReportOptions.ExtensionKey) =>
                value is null or ImpinjInventoryReportOptions,
            (ReaderSettingsExtensionScope.Inventory, ImpinjInventoryControlOptions.ExtensionKey) =>
                value is null or ImpinjInventoryControlOptions,
            _ => false,
        };
    }

    /// <inheritdoc />
    public JsonNode Serialize(ReaderSettingsExtensionScope scope, string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        object typed = (scope, key) switch
        {
            (ReaderSettingsExtensionScope.Configuration, ImpinjReaderConfiguration.ExtensionKey)
                when value is ImpinjReaderConfiguration configuration => configuration,
            (ReaderSettingsExtensionScope.Configuration, ImpinjReaderFacts.ExtensionKey)
                when value is ImpinjReaderFacts facts => facts,
            (ReaderSettingsExtensionScope.Inventory, ImpinjInventoryReportOptions.ExtensionKey)
                when value is ImpinjInventoryReportOptions report => report,
            (ReaderSettingsExtensionScope.Inventory, ImpinjInventoryControlOptions.ExtensionKey)
                when value is ImpinjInventoryControlOptions control => control,
            _ => throw new NotSupportedException($"Impinj does not own settings extension '{key}' at {scope}."),
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
            throw new JsonException($"Impinj settings extension '{key}' must declare version 1 and a value.");
        }
        return (scope, key) switch
        {
            (ReaderSettingsExtensionScope.Configuration, ImpinjReaderConfiguration.ExtensionKey) =>
                document["value"]!.Deserialize<ImpinjReaderConfiguration>(JsonOptions)
                    ?? throw new JsonException("Impinj configuration cannot be null."),
            (ReaderSettingsExtensionScope.Configuration, ImpinjReaderFacts.ExtensionKey) =>
                document["value"]!.Deserialize<ImpinjReaderFacts>(JsonOptions)
                    ?? throw new JsonException("Impinj reader facts cannot be null."),
            (ReaderSettingsExtensionScope.Inventory, ImpinjInventoryReportOptions.ExtensionKey) =>
                document["value"]!.Deserialize<ImpinjInventoryReportOptions>(JsonOptions)
                    ?? throw new JsonException("Impinj inventory report options cannot be null."),
            (ReaderSettingsExtensionScope.Inventory, ImpinjInventoryControlOptions.ExtensionKey) =>
                document["value"]!.Deserialize<ImpinjInventoryControlOptions>(JsonOptions)
                    ?? throw new JsonException("Impinj inventory control options cannot be null."),
            _ => throw new NotSupportedException($"Impinj does not own settings extension '{key}' at {scope}."),
        };
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <inheritdoc />
    public IReadOnlyList<global::LlrpNet.Protocol.Parameters.ILlrpParameter> BuildQueryParameters() =>
    [
        new ImpinjRequestedData(ImpinjRequestedDataType.All_Configuration, [])
    ];

    /// <inheritdoc />
    public void ContributeQuery(ReaderSettingsContributionContext context, ReaderConfigurationExtensionBuilder extensions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extensions);

        ImpinjSubRegulatoryRegion? region = context.CustomItems.OfType<ImpinjSubRegulatoryRegion>().FirstOrDefault();
        ImpinjReaderTemperature? temperature = context.CustomItems.OfType<ImpinjReaderTemperature>().FirstOrDefault();
        ImpinjLinkMonitorConfiguration? linkMonitor = context.CustomItems.OfType<ImpinjLinkMonitorConfiguration>().FirstOrDefault();
        ImpinjReportBufferConfiguration? reportBuffer = context.CustomItems.OfType<ImpinjReportBufferConfiguration>().FirstOrDefault();
        ImpinjAccessSpecConfiguration? accessSpec = context.CustomItems.OfType<ImpinjAccessSpecConfiguration>().FirstOrDefault();

        var debounce = context.CustomItems
            .OfType<ImpinjGPIDebounceConfiguration>()
            .Select(static item => new ImpinjGpiDebounceSetting(item.GPIPortNum, item.GPIDebounceTimerMSec))
            .OrderBy(static item => item.GpiPortNumber)
            .ToArray();

        var configuration = new ImpinjReaderConfiguration
        {
            InventorySearchMode = context.CustomItems.OfType<ImpinjInventorySearchMode>().FirstOrDefault()?.InventorySearchMode,
            FixedFrequency = context.CustomItems.OfType<ImpinjFixedFrequencyList>().FirstOrDefault() is { } fixedFrequency
                ? new ImpinjFixedFrequencySettings(fixedFrequency.FixedFrequencyMode, fixedFrequency.ChannelList)
                : null,
            ReducedPowerFrequency = context.CustomItems.OfType<ImpinjReducedPowerFrequencyList>().FirstOrDefault() is { } reducedPower
                ? new ImpinjReducedPowerFrequencySettings(reducedPower.ReducedPowerMode, reducedPower.ChannelList)
                : null,
            LowDutyCycle = context.CustomItems.OfType<ImpinjLowDutyCycle>().FirstOrDefault() is { } lowDutyCycle
                ? new ImpinjLowDutyCycleSettings(lowDutyCycle.LowDutyCycleMode, lowDutyCycle.EmptyFieldTimeout, lowDutyCycle.FieldPingInterval)
                : null,
            GpiDebounce = debounce,
            LinkMonitor = linkMonitor is null ? null : new ImpinjLinkMonitorSettings(
                linkMonitor.LinkMonitorMode == ImpinjLinkMonitorMode.Enabled, linkMonitor.LinkDownThreshold),
            ReportBufferMode = reportBuffer?.ReportBufferMode,
            AccessSpec = accessSpec is null ? null : new ImpinjAccessSpecSettings(
                accessSpec.ImpinjBlockWriteWordCount?.WordCount,
                accessSpec.ImpinjOpSpecRetryCount?.RetryCount,
                accessSpec.ImpinjAccessSpecOrdering?.OrderingMode),
            AdvancedGpos = context.CustomItems.OfType<ImpinjAdvancedGPOConfiguration>()
                .Select(static item => new ImpinjAdvancedGpoSetting(item.GPOPortNum, item.GPOMode, item.GPOPulseDurationMSec))
                .OrderBy(static item => item.GpoPortNumber).ToArray(),
        };

        extensions.Add(ImpinjReaderConfiguration.ExtensionKey, configuration);
        extensions.Add(ImpinjReaderFacts.ExtensionKey, new ImpinjReaderFacts
        {
            RegulatoryRegion = region?.RegulatoryRegion,
            TemperatureCelsius = temperature?.Temperature,
        });

    }

    /// <inheritdoc />
    public IReadOnlyList<global::LlrpNet.Protocol.Parameters.ILlrpParameter> BuildApplyParameters(
        ReaderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.Extensions.TryGetValue(ImpinjReaderConfiguration.ExtensionKey, out object? value) || value is null)
        {
            return [];
        }
        if (value is not ImpinjReaderConfiguration settings)
        {
            throw new ArgumentException(
                $"ReaderConfiguration.Extensions['{ImpinjReaderConfiguration.ExtensionKey}'] must be an {nameof(ImpinjReaderConfiguration)} instance.");
        }

        var parameters = new List<global::LlrpNet.Protocol.Parameters.ILlrpParameter>();
        if (settings.InventorySearchMode is { } searchMode)
        {
            parameters.Add(new ImpinjInventorySearchMode(searchMode, []));
        }
        if (settings.FixedFrequency is { } fixedFrequency)
        {
            parameters.Add(new ImpinjFixedFrequencyList(fixedFrequency.Mode, fixedFrequency.ChannelList, []));
        }
        if (settings.ReducedPowerFrequency is { } reducedPower)
        {
            parameters.Add(new ImpinjReducedPowerFrequencyList(reducedPower.Mode, reducedPower.ChannelList, []));
        }
        if (settings.LowDutyCycle is { } lowDutyCycle)
        {
            parameters.Add(new ImpinjLowDutyCycle(lowDutyCycle.Mode, lowDutyCycle.EmptyFieldTimeoutMilliseconds, lowDutyCycle.FieldPingIntervalMilliseconds, []));
        }
        foreach (ImpinjGpiDebounceSetting debounce in settings.GpiDebounce)
        {
            parameters.Add(new ImpinjGPIDebounceConfiguration(debounce.GpiPortNumber, debounce.DebounceMilliseconds, []));
        }
        if (settings.LinkMonitor is { } linkMonitor)
        {
            parameters.Add(new ImpinjLinkMonitorConfiguration(linkMonitor.Enabled ? ImpinjLinkMonitorMode.Enabled : ImpinjLinkMonitorMode.Disabled, linkMonitor.LinkDownThreshold, []));
        }
        if (settings.ReportBufferMode is { } reportBuffer)
        {
            parameters.Add(new ImpinjReportBufferConfiguration(reportBuffer, []));
        }
        if (settings.AccessSpec is { } accessSpec)
        {
            parameters.Add(new ImpinjAccessSpecConfiguration(
                accessSpec.BlockWriteWordCount is { } wordCount ? new ImpinjBlockWriteWordCount(wordCount, []) : null,
                accessSpec.OpSpecRetryCount is { } retryCount ? new ImpinjOpSpecRetryCount(retryCount, []) : null,
                accessSpec.OrderingMode is { } orderingMode ? new ImpinjAccessSpecOrdering(orderingMode, []) : null,
                []));
        }
        foreach (ImpinjAdvancedGpoSetting gpo in settings.AdvancedGpos)
        {
            parameters.Add(new ImpinjAdvancedGPOConfiguration(gpo.GpoPortNumber, gpo.Mode, gpo.PulseDurationMilliseconds, []));
        }
        return parameters;
    }

    /// <inheritdoc />
    public void Contribute(InventoryContributionContext context, InventoryExtensionBuilder extensions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extensions);
        if (context.Settings.Extensions.TryGetValue(ImpinjInventoryControlOptions.ExtensionKey, out object? controlValue) &&
            controlValue is not null)
        {
            if (controlValue is not ImpinjInventoryControlOptions controls)
            {
                throw new ArgumentException(
                    $"InventorySettings.Extensions['{ImpinjInventoryControlOptions.ExtensionKey}'] must be an " +
                    $"{nameof(ImpinjInventoryControlOptions)} instance.");
            }
            var controlReader = new ReaderExtensionMatchContext(
                context.Identity.ManufacturerId, context.Identity.ModelId, context.Identity.FirmwareVersion, context.ProtocolVersion);
            foreach (global::LlrpNet.Protocol.Parameters.ILlrpParameter item in
                ImpinjInventoryControlConfigurator.BuildCustomItems(controlReader, controls))
            {
                extensions.AddC1G2InventoryCommandCustomItem(item);
            }
        }

        if (!context.Settings.Extensions.TryGetValue(ImpinjInventoryReportOptions.ExtensionKey, out object? value) || value is null)
        {
            return;
        }
        if (value is not ImpinjInventoryReportOptions options)
        {
            throw new ArgumentException(
                $"InventorySettings.Extensions['{ImpinjInventoryReportOptions.ExtensionKey}'] must be an " +
                $"{nameof(ImpinjInventoryReportOptions)} instance.");
        }

        var reader = new ReaderExtensionMatchContext(
            context.Identity.ManufacturerId,
            context.Identity.ModelId,
            context.Identity.FirmwareVersion,
            context.ProtocolVersion);
        foreach (global::LlrpNet.Protocol.Parameters.ILlrpParameter item in
            ImpinjInventoryReportConfigurator.BuildCustomItems(reader, options))
        {
            extensions.AddRoReportSpecCustomItem(item);
        }
    }

    /// <inheritdoc />
    public void ContributeQuery(InventorySettingsContributionContext context, InventorySettingsExtensionBuilder extensions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extensions);
        ImpinjTagReportContentSelector? selector = context.RoReportSpecCustomItems
            .OfType<ImpinjTagReportContentSelector>()
            .SingleOrDefault();
        if (selector is null)
        {
            // Continue: inventory-command extensions are independent of report extensions.
        }
        else if (selector.CustomItems.Count != 0 || selector.ImpinjEnableOptimizedRead?.C1G2ReadItems.Count > 0)
        {
            throw new InvalidOperationException(
                "The SDK-reserved ROSpec contains Impinj report options that the current high-level model cannot represent.");
        }

        else
        {
            extensions.Add(ImpinjInventoryReportOptions.ExtensionKey, new ImpinjInventoryReportOptions
            {
            IncludeSerializedTid = selector.ImpinjEnableSerializedTID?.SerializedTIDMode == ImpinjSerializedTIDMode.Enabled,
            IncludeRfPhaseAngle = selector.ImpinjEnableRFPhaseAngle?.RFPhaseAngleMode == ImpinjRFPhaseAngleMode.Enabled,
            IncludePeakRssi = selector.ImpinjEnablePeakRSSI?.PeakRSSIMode == ImpinjPeakRSSIMode.Enabled,
            IncludeGpsCoordinates = selector.ImpinjEnableGPSCoordinates?.GPSCoordinatesMode == ImpinjGPSCoordinatesMode.Enabled,
            IncludeOptimizedRead = selector.ImpinjEnableOptimizedRead?.OptimizedReadMode == ImpinjOptimizedReadMode.Enabled,
            IncludeRfDopplerFrequency = selector.ImpinjEnableRFDopplerFrequency?.RFDopplerFrequencyMode == ImpinjRFDopplerFrequencyMode.Enabled,
            IncludeTxPower = selector.ImpinjEnableTxPower?.TxPowerReportingMode == ImpinjTxPowerReportingModeEnum.Enabled,
            IncludeXpcWords = selector.ImpinjEnableXPCWords?.XPCWordsMode == ImpinjXPCWordsMode.Enabled,
            IncludeCrHandle = selector.ImpinjEnableCRHandle?.CRHandleMode == ImpinjCRHandleMode.Enabled,
            IncludeId = selector.ImpinjEnableID?.IDMode == ImpinjIDMode.Enabled,
            IncludeEnhancedIntegra = selector.ImpinjEnableEnhancedIntegra?.EnhancedIntegraMode == ImpinjEnhancedIntegraMode.Enabled,
            IncludeEndpointIcVerification = selector.ImpinjEnableEndpointICVerification?.EndpointICVerificationReportMode == ImpinjEndpointICVerificationReportMode.Enabled,
            });
        }

        ImpinjEnableTagPopulationEstimationAlgorithm? population = context.C1G2InventoryCommandCustomItems
            .OfType<ImpinjEnableTagPopulationEstimationAlgorithm>().SingleOrDefault();
        if (population is not null)
        {
            extensions.Add(ImpinjInventoryControlOptions.ExtensionKey, new ImpinjInventoryControlOptions
            {
                EnableTagPopulationEstimation = population.TagPopulationEstimationMode == ImpinjTagPopulationEstimationMode.Enabled
            });
        }
    }

    /// <inheritdoc />
    public void Contribute(TagReportContributionContext context, TagReportExtensionBuilder extensions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extensions);

        foreach (global::LlrpNet.Protocol.Parameters.ILlrpParameter item in context.CustomItems)
        {
            switch (item)
            {
                case ImpinjSerializedTID serializedTid:
                    extensions.Add("impinj.serializedTid", serializedTid.TID);
                    break;
                case ImpinjRFPhaseAngle phaseAngle:
                    extensions.Add("impinj.rfPhaseAngle", phaseAngle.PhaseAngle);
                    break;
                case ImpinjPeakRSSI peakRssi:
                    extensions.Add("impinj.peakRssi", peakRssi.RSSI);
                    break;
            }
        }
    }
}

/// <summary>Configures an <see cref="LlrpReaderBuilder"/> for Impinj LLRP 1.0.1 custom data.</summary>
public static class ImpinjLlrpReaderBuilderExtensions
{
    /// <summary>
    /// Registers Impinj codecs before connection and activates the Impinj reader extension after standard initialization.
    /// </summary>
    /// <param name="builder">The reader builder to configure.</param>
    /// <returns>The same builder.</returns>
    public static LlrpReaderBuilder UseImpinj(this LlrpReaderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .UseProtocolModule(ImpinjProtocolModule.Instance)
            .UseReaderExtension(ImpinjReaderExtension.Instance);
    }
}
