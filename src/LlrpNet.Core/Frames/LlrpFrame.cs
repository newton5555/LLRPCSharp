using System.Buffers;
using LlrpNet.Core.Protocol;

namespace LlrpNet.Core.Frames;

/// <summary>
/// Describes one complete LLRP wire frame without taking ownership of its backing memory.
/// </summary>
/// <param name="Header">The validated message header.</param>
/// <param name="Bytes">The complete header and message value exactly as received.</param>
public readonly record struct LlrpFrame(
    LlrpMessageHeader Header,
    ReadOnlySequence<byte> Bytes)
{
    /// <summary>
    /// Gets the message value following the fixed header.
    /// </summary>
    public ReadOnlySequence<byte> Payload => Bytes.Slice(LlrpMessageHeader.EncodedLength);
}

