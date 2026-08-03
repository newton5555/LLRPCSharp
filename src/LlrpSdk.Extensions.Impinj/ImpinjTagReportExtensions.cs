using LlrpSdk.Extensions.Impinj.Enumerations.V1_0_1;

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
