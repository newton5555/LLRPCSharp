using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Transport;
using LlrpNet.Protocol.Registry;
using Microsoft.Extensions.Logging;

namespace LlrpSdk;

/// <summary>
/// Creates the framed transport owned by an <see cref="LlrpReader"/>.
/// </summary>
/// <param name="options">The immutable reader options being used.</param>
/// <returns>A new, disconnected transport whose ownership transfers to the reader.</returns>
public delegate ILlrpTransport LlrpTransportFactory(LlrpReaderOptions options);

/// <summary>
/// Immutable connection and diagnostics settings for an <see cref="LlrpReader"/>.
/// </summary>
public sealed class LlrpReaderOptions
{
    /// <summary>
    /// The default number of decoded reader-initiated messages retained for application consumers.
    /// </summary>
    public const int DefaultIncomingMessageCapacity = 1024;

    internal LlrpReaderOptions(
        string host,
        int port,
        TimeSpan connectTimeout,
        TimeSpan frameAssemblyTimeout,
        TimeSpan requestTimeout,
        uint maximumFrameLength,
        int incomingMessageCapacity,
        ILoggerFactory loggerFactory,
        ILlrpFrameObserver frameObserver,
        LlrpTransportFactory? transportFactory,
        IEnumerable<Action<LlrpCodecRegistry>> protocolConfigurations)
    {
        Host = host;
        Port = port;
        ConnectTimeout = connectTimeout;
        FrameAssemblyTimeout = frameAssemblyTimeout;
        RequestTimeout = requestTimeout;
        MaximumFrameLength = maximumFrameLength;
        IncomingMessageCapacity = incomingMessageCapacity;
        LoggerFactory = loggerFactory;
        FrameObserver = frameObserver;
        TransportFactory = transportFactory ?? CreateTcpTransport;
        ProtocolConfigurations = Array.AsReadOnly(protocolConfigurations.ToArray());
    }

    /// <summary>
    /// Gets the reader hostname or IP address.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Gets the reader TCP port.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the maximum duration of one transport connection attempt.
    /// </summary>
    public TimeSpan ConnectTimeout { get; }

    /// <summary>
    /// Gets the maximum time allowed to assemble a frame after its first octet arrives.
    /// </summary>
    public TimeSpan FrameAssemblyTimeout { get; }

    /// <summary>
    /// Gets the default timeout applied to correlated request/response transactions.
    /// </summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>
    /// Gets the defensive upper bound for one complete LLRP frame.
    /// </summary>
    public uint MaximumFrameLength { get; }

    /// <summary>
    /// Gets the bounded capacity of the decoded reader-initiated message stream.
    /// </summary>
    public int IncomingMessageCapacity { get; }

    /// <summary>
    /// Gets the application logging factory shared by the reader, session, and default transport.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Gets the exact-frame observer used by the default TCP transport.
    /// </summary>
    public ILlrpFrameObserver FrameObserver { get; }

    /// <summary>
    /// Gets the transport factory. A custom factory is useful for alternate transports and deterministic tests.
    /// </summary>
    public LlrpTransportFactory TransportFactory { get; }

    internal IReadOnlyList<Action<LlrpCodecRegistry>> ProtocolConfigurations { get; }

    private static ILlrpTransport CreateTcpTransport(LlrpReaderOptions options)
    {
        return new LlrpTcpTransport(
            new LlrpTcpTransportOptions
            {
                Host = options.Host,
                Port = options.Port,
                ConnectTimeout = options.ConnectTimeout,
                FrameAssemblyTimeout = options.FrameAssemblyTimeout,
                MaximumFrameLength = options.MaximumFrameLength,
            },
            options.LoggerFactory,
            options.FrameObserver);
    }
}
