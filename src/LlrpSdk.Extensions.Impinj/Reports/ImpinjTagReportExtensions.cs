using LlrpNet.Protocol.Impinj.Enumerations.V1_0_1;

namespace LlrpSdk.Extensions.Impinj;

/// <summary>GPS coordinates reported by an Impinj reader, represented in signed microdegrees.</summary>
public sealed record ImpinjGpsCoordinates(int LatitudeMicrodegrees, int LongitudeMicrodegrees)
{
    public double LatitudeDegrees => LatitudeMicrodegrees / 1_000_000d;
    public double LongitudeDegrees => LongitudeMicrodegrees / 1_000_000d;
}

/// <summary>Signed Doppler frequency reported by an Impinj reader.</summary>
public sealed record ImpinjRfDopplerFrequency(short RawValue)
{
    public double Hertz => RawValue / 16d;
}

/// <summary>Bit-vector value from an Impinj report, retaining the exact bit order and length.</summary>
public sealed record ImpinjBitVector(IReadOnlyList<bool> Bits)
{
    public string Hex => ToHex(Bits);

    private static string ToHex(IReadOnlyList<bool> bits)
    {
        if (bits.Count == 0)
        {
            return string.Empty;
        }
        var chars = new char[(bits.Count + 3) / 4];
        for (int index = 0; index < chars.Length; index++)
        {
            int value = 0;
            for (int bit = 0; bit < 4; bit++)
            {
                int source = index * 4 + bit;
                if (source < bits.Count && bits[source])
                {
                    value |= 1 << (3 - bit);
                }
            }
            chars[index] = value < 10 ? (char)('0' + value) : (char)('A' + value - 10);
        }
        return new string(chars);
    }
}

/// <summary>Enhanced Integra result returned by an optimized tag operation.</summary>
public sealed record ImpinjEnhancedIntegraResult(ImpinjEnhancedIntegraResultType Result, ushort OpSpecId);

/// <summary>Endpoint IC verification result returned by a tag report.</summary>
public sealed record ImpinjEndpointIcVerification(byte VerificationOn, byte Identifier);

/// <summary>Stable extension keys and strongly typed readers for vendor fields projected into <see cref="TagReport.Extensions"/>.</summary>
public static class ImpinjTagReportExtensions
{
    /// <summary>Gets the extension key under which the Impinj serialized TID is projected.</summary>
    public const string SerializedTidExtensionKey = "impinj.serializedTid";

    /// <summary>
    /// Gets the Impinj serialized TID as an uppercase hexadecimal string, or <see langword="null"/> when the
    /// connected reader did not report one (for example, when <c>IncludeSerializedTid</c> was not requested).
    /// </summary>
    /// <remarks>
    /// This C# 13-compatible accessor works on every compiler. The C# 14 <c>SerializedTidHex</c> extension
    /// property (disabled below via <c>#if false</c> for toolchain compatibility) delegates to this method, so
    /// both forms stay equivalent when re-enabled.
    /// </remarks>
    public static string? GetSerializedTidHex(this TagReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Extensions is null ||
            !report.Extensions.TryGetValue(SerializedTidExtensionKey, out object? value) ||
            value is not IReadOnlyList<ushort> words)
        {
            return null;
        }

        return string.Concat(words.Select(static word => word.ToString("X4")));
    }

#if false // C# 14 extension property, disabled for broad toolchain compatibility (VS 2022 cannot compile it).
    // Re-enable on a C# 14 toolchain by switching to #if NET10_0_OR_GREATER; the property delegates to
    // GetSerializedTidHex(TagReport) so both forms stay equivalent.
    extension(TagReport report)
    {
        /// <summary>
        /// Gets the Impinj serialized TID as an uppercase hexadecimal string, or <see langword="null"/> when the
        /// connected reader did not report one. Equivalent to <see cref="GetSerializedTidHex(TagReport)"/>.
        /// </summary>
        public string? SerializedTidHex => report.GetSerializedTidHex();
    }
#endif
}
