namespace LlrpSdk.Extensions.Zebra;

/// <summary>Configures Zebra tag-report content not represented by the standard LLRP report selector.</summary>
public sealed record ZebraInventoryReportOptions
{
    public const string ExtensionKey = "zebra.inventoryReport";

    public bool IncludeZoneId { get; init; }
    public bool IncludeZoneName { get; init; }
    public bool IncludeAntennaPhysicalPortConfig { get; init; }
    public bool IncludePhase { get; init; }
    public bool IncludeGps { get; init; }
    public bool IncludeMltReport { get; init; }
}
