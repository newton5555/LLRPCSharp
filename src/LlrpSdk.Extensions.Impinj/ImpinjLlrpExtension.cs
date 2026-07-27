using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions;
using LlrpSdk.Extensions.Impinj.Enumerations.V1_0_1;
using LlrpSdk.Extensions.Impinj.Messages.V1_0_1;
using LlrpSdk.Extensions.Impinj.Parameters.V1_0_1;

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

        extensions.Add("impinj.readerSettings", new ImpinjReaderSettings
        {
            RegulatoryRegion = region?.RegulatoryRegion,
            GpiDebounce = debounce,
            TemperatureCelsius = temperature?.Temperature,
            LinkMonitor = linkMonitor is null
                ? null
                : new ImpinjLinkMonitorSettings(
                    linkMonitor.LinkMonitorMode == ImpinjLinkMonitorMode.Enabled,
                    linkMonitor.LinkDownThreshold),
            ReportBufferMode = reportBuffer?.ReportBufferMode,
            AccessSpec = accessSpec is null
                ? null
                : new ImpinjAccessSpecSettings(
                    accessSpec.ImpinjBlockWriteWordCount?.WordCount,
                    accessSpec.ImpinjOpSpecRetryCount?.RetryCount,
                    accessSpec.ImpinjAccessSpecOrdering?.OrderingMode)
        });
    }

    /// <inheritdoc />
    /// <remarks>Impinj settings are intentionally read-only until an explicit profile and restore workflow is implemented.</remarks>
    public IReadOnlyList<global::LlrpNet.Protocol.Parameters.ILlrpParameter> BuildApplyParameters(
        ReaderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return [];
    }

    /// <inheritdoc />
    public void Contribute(InventoryContributionContext context, InventoryExtensionBuilder extensions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extensions);
        if (!context.Settings.Extensions.TryGetValue(ImpinjInventoryReportOptions.ExtensionKey, out object? value) ||
            value is null)
        {
            return;
        }
        if (value is not ImpinjInventoryReportOptions options)
        {
            throw new ArgumentException(
                $"ReaderSettings.Extensions['{ImpinjInventoryReportOptions.ExtensionKey}'] must be an " +
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
