namespace LlrpSdk.Extensions.Zebra;

/// <summary>GPS coordinates reported by a Zebra reader (raw 32-bit fields; signedness/scaling per the vendor guide).</summary>
public sealed record ZebraGpsCoordinates(uint Longitude, uint Latitude, uint Altitude);

/// <summary>The two 16-bit XPC words reported by a Zebra reader.</summary>
public sealed record ZebraExtendedPc(ushort XPC1, ushort XPC2);

/// <summary>Convenience accessors for Zebra extension values projected into <see cref="LlrpSdk.TagReport.Extensions"/>.</summary>
public static class ZebraTagReportExtensions
{
    public const string PhaseExtensionKey = "zebra.phase";
    public const string GpsExtensionKey = "zebra.gps";
    public const string ExtendedPcExtensionKey = "zebra.xpc";

    public static short? GetPhase(this TagReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Extensions is not null &&
            report.Extensions.TryGetValue(PhaseExtensionKey, out object? value) &&
            value is short phase
            ? phase
            : null;
    }

    public static ZebraGpsCoordinates? GetGps(this TagReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Extensions is not null &&
            report.Extensions.TryGetValue(GpsExtensionKey, out object? value) &&
            value is ZebraGpsCoordinates gps
            ? gps
            : null;
    }

    public static ZebraExtendedPc? GetExtendedPc(this TagReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Extensions is not null &&
            report.Extensions.TryGetValue(ExtendedPcExtensionKey, out object? value) &&
            value is ZebraExtendedPc xpc
            ? xpc
            : null;
    }
}
