namespace LlrpSdk;

/// <summary>Controls how a reader connection selects its LLRP protocol version.</summary>
public enum LlrpProtocolVersionPolicy
{
    /// <summary>Probe for LLRP 1.1 and retain LLRP 1.0.1 when the reader rejects the probe.</summary>
    Auto = 0,

    /// <summary>Use LLRP 1.0.1 without sending a higher-version negotiation probe.</summary>
    Force101 = 1,

    /// <summary>Require LLRP 1.1; reject connection when version negotiation cannot select it.</summary>
    Force11 = 2,
}
