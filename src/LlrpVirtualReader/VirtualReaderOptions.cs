namespace LlrpVirtualReader;

using LlrpNet.Protocol.Enumerations.V1_0_1;

/// <summary>Configures the deterministic tag exposed by <see cref="VirtualReaderHost"/>.</summary>
public sealed record VirtualReaderOptions
{
    /// <summary>Gets the 96-bit EPC reported by the virtual tag.</summary>
    public ReadOnlyMemory<byte> ElectronicProductCode { get; init; } =
        Convert.FromHexString("E28011710000020D056E9BEE");

    /// <summary>Gets the initial 16-bit User-memory words for the virtual tag.</summary>
    public IReadOnlyList<ushort> UserMemory { get; init; } = [0, 0, 0, 0];

    /// <summary>
    /// Gets request message types for which the virtual reader intentionally withholds a response.
    /// </summary>
    /// <remarks>
    /// This deterministic fault is intended for request-timeout and cancellation tests. Asynchronous reports
    /// associated with the dropped response are withheld as well.
    /// </remarks>
    public IReadOnlySet<ushort> DropResponseForMessageTypes { get; init; } = new HashSet<ushort>();

    /// <summary>Gets request message types for which the virtual reader returns an injected LLRP error response.</summary>
    public IReadOnlyDictionary<ushort, VirtualReaderErrorResponse> ErrorResponseForMessageTypes { get; init; } =
        new Dictionary<ushort, VirtualReaderErrorResponse>();

    /// <summary>Gets request message types after which the virtual reader closes the current TCP connection once.</summary>
    /// <remarks>This fault is evaluated before request state is mutated or a response is written.</remarks>
    public IReadOnlySet<ushort> CloseConnectionAfterRequestMessageTypes { get; init; } = new HashSet<ushort>();

    /// <summary>Gets request message types for which the response is intentionally truncated before the connection closes.</summary>
    public IReadOnlySet<ushort> TruncateResponseForMessageTypes { get; init; } = new HashSet<ushort>();
}

/// <summary>Describes one deterministic virtual-reader error response.</summary>
public sealed record VirtualReaderErrorResponse(StatusCode StatusCode, string Description);
