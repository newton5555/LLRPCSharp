using LlrpNet.Core.Diagnostics;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions;
using Microsoft.Extensions.Logging;

namespace LlrpSdk;

/// <summary>
/// Provides the application-facing fluent path for creating one configured <see cref="LlrpReader"/>.
/// </summary>
public sealed class LlrpReaderBuilder
{
    private readonly LlrpReaderOptionsBuilder _optionsBuilder;

    /// <summary>
    /// Initializes a reader builder for a hostname or IP address.
    /// </summary>
    /// <param name="host">The reader hostname or IP address.</param>
    public LlrpReaderBuilder(string host)
    {
        _optionsBuilder = new LlrpReaderOptionsBuilder(host);
    }

    /// <summary>
    /// Replaces the reader hostname or IP address.
    /// </summary>
    /// <param name="host">The reader hostname or IP address.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithHost(string host)
    {
        _optionsBuilder.WithHost(host);
        return this;
    }

    /// <summary>
    /// Replaces the reader TCP port.
    /// </summary>
    /// <param name="port">A TCP port from 1 through 65535.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithPort(int port)
    {
        _optionsBuilder.WithPort(port);
        return this;
    }

    /// <summary>
    /// Replaces the connection-attempt timeout.
    /// </summary>
    /// <param name="timeout">A positive duration or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithConnectTimeout(TimeSpan timeout)
    {
        _optionsBuilder.WithConnectTimeout(timeout);
        return this;
    }

    /// <summary>
    /// Replaces the maximum frame assembly duration after the first octet arrives.
    /// </summary>
    /// <param name="timeout">A positive duration or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithFrameAssemblyTimeout(TimeSpan timeout)
    {
        _optionsBuilder.WithFrameAssemblyTimeout(timeout);
        return this;
    }

    /// <summary>
    /// Replaces the default correlated request timeout.
    /// </summary>
    /// <param name="timeout">A non-negative duration or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithRequestTimeout(TimeSpan timeout)
    {
        _optionsBuilder.WithRequestTimeout(timeout);
        return this;
    }

    /// <summary>
    /// Replaces the defensive maximum complete-frame length.
    /// </summary>
    /// <param name="maximumFrameLength">The maximum complete-frame length.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithMaximumFrameLength(uint maximumFrameLength)
    {
        _optionsBuilder.WithMaximumFrameLength(maximumFrameLength);
        return this;
    }

    /// <summary>
    /// Replaces the bounded capacity of decoded reader-initiated messages awaiting application consumption.
    /// </summary>
    /// <param name="capacity">A positive number of complete decoded messages.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithIncomingMessageCapacity(int capacity)
    {
        _optionsBuilder.WithIncomingMessageCapacity(capacity);
        return this;
    }

    /// <summary>
    /// Uses an application logging factory throughout the reader stack.
    /// </summary>
    /// <param name="loggerFactory">The logging factory.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _optionsBuilder.WithLoggerFactory(loggerFactory);
        return this;
    }

    /// <summary>
    /// Uses a best-effort observer for exact complete frames at the default transport boundary.
    /// </summary>
    /// <param name="frameObserver">The exact-frame observer.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithFrameObserver(ILlrpFrameObserver frameObserver)
    {
        _optionsBuilder.WithFrameObserver(frameObserver);
        return this;
    }

    /// <summary>
    /// Uses a custom transport factory instead of the default TCP transport.
    /// </summary>
    /// <param name="transportFactory">A factory that returns a new disconnected transport.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithTransportFactory(LlrpTransportFactory transportFactory)
    {
        _optionsBuilder.WithTransportFactory(transportFactory);
        return this;
    }

    /// <summary>
    /// Adds a custom or vendor codec registration callback before the reader can connect.
    /// </summary>
    /// <param name="configuration">A callback that configures the versioned codec registry.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder ConfigureProtocol(Action<LlrpCodecRegistry> configuration)
    {
        _optionsBuilder.ConfigureProtocol(configuration);
        return this;
    }

    /// <summary>Controls automatic negotiation or forces a specific supported LLRP version.</summary>
    /// <param name="policy">The protocol version selection policy.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithProtocolVersionPolicy(LlrpProtocolVersionPolicy policy)
    {
        _optionsBuilder.WithProtocolVersionPolicy(policy);
        return this;
    }

    /// <summary>Enables bounded automatic reconnect after an unexpected connected-session failure.</summary>
    /// <param name="options">The retry policy to use.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder WithAutomaticReconnect(LlrpAutomaticReconnectOptions options)
    {
        _optionsBuilder.WithAutomaticReconnect(options);
        return this;
    }

    /// <summary>Registers a standard, vendor, or customer protocol module before connection.</summary>
    /// <param name="module">The module that registers its codecs with the reader-owned registry.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder UseProtocolModule(ILlrpProtocolModule module)
    {
        _optionsBuilder.UseProtocolModule(module);
        return this;
    }

    /// <summary>Registers an extension for identity-based activation after the reader initializes.</summary>
    /// <param name="extension">The extension eligible for this reader.</param>
    /// <returns>This builder.</returns>
    public LlrpReaderBuilder UseReaderExtension(IReaderExtension extension)
    {
        _optionsBuilder.UseReaderExtension(extension);
        return this;
    }

    /// <summary>
    /// Builds the immutable options without constructing a reader.
    /// </summary>
    /// <returns>The validated immutable options.</returns>
    public LlrpReaderOptions BuildOptions()
    {
        return _optionsBuilder.Build();
    }

    /// <summary>
    /// Builds the immutable options and constructs the reader session root.
    /// </summary>
    /// <returns>A disconnected reader that owns its configured transport.</returns>
    public LlrpReader Build()
    {
        return new LlrpReader(BuildOptions());
    }
}
