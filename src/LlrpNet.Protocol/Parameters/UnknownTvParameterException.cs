using LlrpNet.Core.Protocol;

namespace LlrpNet.Protocol.Parameters;

/// <summary>
/// Represents an unregistered TV parameter whose boundary cannot be determined from the wire data.
/// </summary>
public sealed class UnknownTvParameterException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownTvParameterException"/> class.
    /// </summary>
    /// <param name="version">The active LLRP protocol version.</param>
    /// <param name="parameterType">The unregistered seven-bit TV parameter type.</param>
    public UnknownTvParameterException(LlrpProtocolVersion version, byte parameterType)
        : base(
            $"TV parameter type {parameterType} is not registered for LLRP version {version}; " +
            "its length cannot be inferred from the wire header.")
    {
        Version = version;
        ParameterType = parameterType;
    }

    /// <summary>
    /// Gets the active LLRP protocol version.
    /// </summary>
    public LlrpProtocolVersion Version { get; }

    /// <summary>
    /// Gets the unregistered TV parameter type.
    /// </summary>
    public byte ParameterType { get; }
}
