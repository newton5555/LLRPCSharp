using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Frames;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Transport;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlrpSdk;

/// <summary>
/// Builds validated immutable options for one reader connection.
/// </summary>
public sealed class LlrpReaderOptionsBuilder
{
    private static readonly TimeSpan MaximumTimerTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    private string _host;
    private int _port = LlrpTcpTransportOptions.DefaultPort;
    private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
    private TimeSpan _frameAssemblyTimeout = TimeSpan.FromSeconds(10);
    private TimeSpan _requestTimeout = TimeSpan.FromSeconds(10);
    private uint _maximumFrameLength = LlrpFrameDecoder.DefaultMaximumFrameLength;
    private int _incomingMessageCapacity = LlrpReaderOptions.DefaultIncomingMessageCapacity;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private ILlrpFrameObserver _frameObserver = NullLlrpFrameObserver.Instance;
    private LlrpAutomaticReconnectOptions? _automaticReconnect;
    private LlrpTransportFactory? _transportFactory;
    private readonly List<ILlrpProtocolModule> _protocolModules = [];
    private readonly List<Action<LlrpCodecRegistry>> _protocolConfigurations = [];

    /// <summary>
    /// Initializes a builder for a reader hostname or IP address.
    /// </summary>
    /// <param name="host">The initial reader hostname or IP address.</param>
    public LlrpReaderOptionsBuilder(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _host = host.Trim();
    }

    /// <summary>
    /// Replaces the reader hostname or IP address.
    /// </summary>
    /// <param name="host">The reader hostname or IP address.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _host = host.Trim();
        return this;
    }

    /// <summary>
    /// Replaces the reader TCP port.
    /// </summary>
    /// <param name="port">A TCP port from 1 through 65535.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithPort(int port)
    {
        _port = port;
        return this;
    }

    /// <summary>
    /// Replaces the connection-attempt timeout.
    /// </summary>
    /// <param name="timeout">A positive duration or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithConnectTimeout(TimeSpan timeout)
    {
        _connectTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Replaces the maximum frame assembly duration after the first octet arrives.
    /// </summary>
    /// <param name="timeout">A positive duration or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithFrameAssemblyTimeout(TimeSpan timeout)
    {
        _frameAssemblyTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Replaces the default correlated request timeout.
    /// </summary>
    /// <param name="timeout">A non-negative duration or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithRequestTimeout(TimeSpan timeout)
    {
        _requestTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Replaces the defensive maximum complete-frame length.
    /// </summary>
    /// <param name="maximumFrameLength">The maximum complete-frame length.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithMaximumFrameLength(uint maximumFrameLength)
    {
        _maximumFrameLength = maximumFrameLength;
        return this;
    }

    /// <summary>
    /// Replaces the bounded capacity of the decoded reader-initiated message stream.
    /// </summary>
    /// <param name="capacity">A positive number of complete decoded messages.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithIncomingMessageCapacity(int capacity)
    {
        _incomingMessageCapacity = capacity;
        return this;
    }

    /// <summary>
    /// Uses an application logging factory throughout the reader stack.
    /// </summary>
    /// <param name="loggerFactory">The logging factory.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// Uses a best-effort observer for complete frames at the default transport boundary.
    /// </summary>
    /// <param name="frameObserver">The exact-frame observer.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithFrameObserver(ILlrpFrameObserver frameObserver)
    {
        ArgumentNullException.ThrowIfNull(frameObserver);
        _frameObserver = frameObserver;
        return this;
    }

    /// <summary>
    /// Uses a custom transport factory instead of the default TCP transport.
    /// </summary>
    /// <param name="transportFactory">A factory that returns a new disconnected transport.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithTransportFactory(LlrpTransportFactory transportFactory)
    {
        ArgumentNullException.ThrowIfNull(transportFactory);
        _transportFactory = transportFactory;
        return this;
    }

    /// <summary>
    /// Adds a protocol registry configuration that runs after the standard LLRP 1.0.1 module and before transport creation.
    /// </summary>
    /// <param name="configuration">A registration callback for custom or vendor message and parameter codecs.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Callbacks run in the order added. Duplicate standard, custom wire, or CLR mappings fail immediately while the
    /// reader is being built, before it can connect.
    /// </remarks>
    public LlrpReaderOptionsBuilder ConfigureProtocol(Action<LlrpCodecRegistry> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _protocolConfigurations.Add(configuration);
        return this;
    }

    /// <summary>Enables bounded automatic reconnect after an unexpected connected-session failure.</summary>
    /// <param name="options">The retry policy to use.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderOptionsBuilder WithAutomaticReconnect(LlrpAutomaticReconnectOptions options)
    {
        _automaticReconnect = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    /// <summary>
    /// Registers a cohesive protocol module before the reader can connect.
    /// </summary>
    /// <param name="module">The standard, vendor, or customer protocol module.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Modules run after the built-in standard module and before low-level <see cref="ConfigureProtocol"/> callbacks.
    /// Duplicate module identifiers and conflicting codec registrations fail while the reader is being built.
    /// </remarks>
    public LlrpReaderOptionsBuilder UseProtocolModule(ILlrpProtocolModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (string.IsNullOrWhiteSpace(module.Id))
        {
            throw new ArgumentException("A protocol module must have a non-empty identifier.", nameof(module));
        }

        _protocolModules.Add(module);
        return this;
    }

    /// <summary>
    /// Validates the accumulated values and creates immutable reader options.
    /// </summary>
    /// <returns>The immutable options.</returns>
    public LlrpReaderOptions Build()
    {
        Validate();
        return new LlrpReaderOptions(
            _host,
            _port,
            _connectTimeout,
            _frameAssemblyTimeout,
            _requestTimeout,
            _maximumFrameLength,
            _incomingMessageCapacity,
            _loggerFactory,
            _frameObserver,
            _automaticReconnect,
            _transportFactory,
            _protocolModules,
            _protocolConfigurations);
    }

    private void Validate()
    {
        if (_port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_port),
                _port,
                "The TCP port must be from 1 through 65535.");
        }

        if ((_connectTimeout <= TimeSpan.Zero || _connectTimeout > MaximumTimerTimeout) &&
            _connectTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_connectTimeout),
                _connectTimeout,
                $"The connection timeout must be positive, no greater than {MaximumTimerTimeout}, or infinite.");
        }

        if ((_frameAssemblyTimeout <= TimeSpan.Zero ||
             _frameAssemblyTimeout > MaximumTimerTimeout) &&
            _frameAssemblyTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_frameAssemblyTimeout),
                _frameAssemblyTimeout,
                $"The frame assembly timeout must be positive, no greater than {MaximumTimerTimeout}, or infinite.");
        }

        if ((_requestTimeout < TimeSpan.Zero || _requestTimeout > MaximumTimerTimeout) &&
            _requestTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_requestTimeout),
                _requestTimeout,
                $"The request timeout must be non-negative, no greater than {MaximumTimerTimeout}, or infinite.");
        }

        if (_maximumFrameLength is < LlrpMessageHeader.EncodedLength or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_maximumFrameLength),
                _maximumFrameLength,
                $"The maximum frame length must be from {LlrpMessageHeader.EncodedLength} through {int.MaxValue} octets.");
        }


        if (_incomingMessageCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_incomingMessageCapacity),
                _incomingMessageCapacity,
                "The incoming message capacity must be positive.");
        }

        string? duplicateModuleId = _protocolModules
            .GroupBy(static module => module.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;
        if (duplicateModuleId is not null)
        {
            throw new InvalidOperationException(
                $"Protocol module identifier '{duplicateModuleId}' was configured more than once.");
        }
    }
}
