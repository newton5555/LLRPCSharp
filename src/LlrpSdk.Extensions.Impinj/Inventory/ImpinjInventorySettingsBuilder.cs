namespace LlrpSdk.Extensions.Impinj;

/// <summary>Configures typed Impinj options on the core inventory settings builder.</summary>
public sealed class ImpinjInventorySettingsBuilder
{
    private ImpinjInventoryReportOptions report;
    private ImpinjInventoryControlOptions control;

    internal ImpinjInventorySettingsBuilder(
        ImpinjInventoryReportOptions? report,
        ImpinjInventoryControlOptions? control)
    {
        this.report = report ?? new ImpinjInventoryReportOptions();
        this.control = control ?? new ImpinjInventoryControlOptions();
    }

    public ImpinjInventorySettingsBuilder IncludeSerializedTid(bool enabled = true)
    {
        report = report with { IncludeSerializedTid = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder IncludeRfPhaseAngle(bool enabled = true)
    {
        report = report with { IncludeRfPhaseAngle = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder IncludePeakRssi(bool enabled = true)
    {
        report = report with { IncludePeakRssi = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder IncludeGpsCoordinates(bool enabled = true)
    {
        report = report with { IncludeGpsCoordinates = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder IncludeRfDopplerFrequency(bool enabled = true)
    {
        report = report with { IncludeRfDopplerFrequency = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder IncludeTxPower(bool enabled = true)
    {
        report = report with { IncludeTxPower = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder IncludeXpcWords(bool enabled = true)
    {
        report = report with { IncludeXpcWords = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder IncludeCrHandle(bool enabled = true)
    {
        report = report with { IncludeCrHandle = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder IncludeId(bool enabled = true)
    {
        report = report with { IncludeId = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder IncludeEnhancedIntegra(bool enabled = true)
    {
        report = report with { IncludeEnhancedIntegra = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder IncludeEndpointIcVerification(bool enabled = true)
    {
        report = report with { IncludeEndpointIcVerification = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder OptimizedRead(
        ushort opSpecId,
        TagMemoryBank memoryBank,
        ushort wordPointer,
        ushort wordCount,
        uint accessPassword = 0)
    {
        report = report with
        {
            IncludeOptimizedRead = true,
            OptimizedReads = report.OptimizedReads
                .Append(new ImpinjOptimizedReadOperation(opSpecId, memoryBank, wordPointer, wordCount, accessPassword))
                .ToArray(),
        };
        return this;
    }

    public ImpinjInventorySettingsBuilder EnableTagPopulationEstimation(bool enabled = true)
    {
        control = control with { EnableTagPopulationEstimation = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder DisableTagPopulationEstimation()
    {
        control = control with { EnableTagPopulationEstimation = null };
        return this;
    }

    public ImpinjInventorySettingsBuilder AllowUnverifiedReportFields(bool enabled = true)
    {
        report = report with { AllowUnverifiedFields = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder AllowUnverifiedFeatures(bool enabled = true)
    {
        control = control with { AllowUnverifiedFeatures = enabled };
        return this;
    }

    public ImpinjInventorySettingsBuilder AllowUnverified(bool enabled = true)
    {
        report = report with { AllowUnverifiedFields = enabled };
        control = control with { AllowUnverifiedFeatures = enabled };
        return this;
    }

    internal ImpinjInventoryReportOptions Report => report;
    internal ImpinjInventoryControlOptions Control => control;

    internal bool HasReportOptions => report.HasRequestedFields || report.AllowUnverifiedFields;

    internal bool HasControlOptions => control.EnableTagPopulationEstimation is not null ||
        control.TagFilterVerificationMode is not null ||
        control.TruncatedReply is not null ||
        control.Gen2XInventory is not null ||
        control.Gen2XTagSelection is not null ||
        control.EndpointIcVerificationMode is not null ||
        control.RampUpPowerBoostMode is not null ||
        control.AllowUnverifiedFeatures;
}

/// <summary>Provides the typed Impinj inventory entry point without exposing extension dictionary keys.</summary>
public static class ImpinjInventorySettingsBuilderExtensions
{
    public static InventorySettingsBuilder Impinj(
        this InventorySettingsBuilder builder,
        Action<ImpinjInventorySettingsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.TryGetExtension(ImpinjInventoryReportOptions.ExtensionKey, out ImpinjInventoryReportOptions? report);
        builder.TryGetExtension(ImpinjInventoryControlOptions.ExtensionKey, out ImpinjInventoryControlOptions? control);
        var impinj = new ImpinjInventorySettingsBuilder(report, control);
        configure(impinj);

        if (impinj.HasReportOptions)
        {
            builder.SetExtension(ImpinjInventoryReportOptions.ExtensionKey, impinj.Report);
        }
        if (impinj.HasControlOptions)
        {
            builder.SetExtension(ImpinjInventoryControlOptions.ExtensionKey, impinj.Control);
        }
        else
        {
            builder.RemoveExtension(ImpinjInventoryControlOptions.ExtensionKey);
        }
        return builder;
    }
}
