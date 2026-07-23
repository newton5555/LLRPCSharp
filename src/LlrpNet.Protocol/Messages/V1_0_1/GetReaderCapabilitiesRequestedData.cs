namespace LlrpNet.Protocol.Messages.V1_0_1;

/// <summary>
/// Selects the capability subset requested by an LLRP 1.0.1 GET_READER_CAPABILITIES message.
/// </summary>
public enum GetReaderCapabilitiesRequestedData : byte
{
    /// <summary>
    /// Requests every supported capability category.
    /// </summary>
    All = 0,

    /// <summary>
    /// Requests general reader device capabilities.
    /// </summary>
    GeneralDeviceCapabilities = 1,

    /// <summary>
    /// Requests LLRP protocol capabilities.
    /// </summary>
    LlrpCapabilities = 2,

    /// <summary>
    /// Requests regulatory capabilities.
    /// </summary>
    RegulatoryCapabilities = 3,

    /// <summary>
    /// Requests LLRP air-protocol capabilities.
    /// </summary>
    LlrpAirProtocolCapabilities = 4,
}
