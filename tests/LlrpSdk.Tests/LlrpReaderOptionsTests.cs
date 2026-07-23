using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Transport;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Registry.V1_0_1;
using LlrpSdk.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlrpSdk.Tests;

public sealed class LlrpReaderOptionsTests
{
    [Fact]
    public void Build_CopiesValuesIntoImmutableOptions()
    {
        var transport = new ScriptedLlrpTransport();
        var builder = new LlrpReaderOptionsBuilder(" reader.local ")
            .WithPort(6000)
            .WithConnectTimeout(TimeSpan.FromSeconds(3))
            .WithFrameAssemblyTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(4))
            .WithMaximumFrameLength(4096)
            .WithIncomingMessageCapacity(64)
            .WithLoggerFactory(NullLoggerFactory.Instance)
            .WithFrameObserver(NullLlrpFrameObserver.Instance)
            .WithTransportFactory(_ => transport);

        LlrpReaderOptions options = builder.Build();
        builder.WithHost("different.local").WithPort(7000);

        Assert.Equal("reader.local", options.Host);
        Assert.Equal(6000, options.Port);
        Assert.Equal(TimeSpan.FromSeconds(3), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), options.FrameAssemblyTimeout);
        Assert.Equal(TimeSpan.FromSeconds(4), options.RequestTimeout);
        Assert.Equal(4096U, options.MaximumFrameLength);
        Assert.Equal(64, options.IncomingMessageCapacity);
        Assert.Same(NullLoggerFactory.Instance, options.LoggerFactory);
        Assert.Same(NullLlrpFrameObserver.Instance, options.FrameObserver);
        Assert.Same(transport, options.TransportFactory(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Build_RejectsInvalidPort(int port)
    {
        var builder = new LlrpReaderOptionsBuilder("reader.local").WithPort(port);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build());
    }

    [Fact]
    public void Build_UsesStandardTcpDefaults()
    {
        LlrpReaderOptions options = new LlrpReaderOptionsBuilder("reader.local").Build();

        Assert.Equal(LlrpTcpTransportOptions.DefaultPort, options.Port);
        Assert.Equal(TimeSpan.FromSeconds(10), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FrameAssemblyTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.RequestTimeout);
        Assert.Equal(
            LlrpReaderOptions.DefaultIncomingMessageCapacity,
            options.IncomingMessageCapacity);
    }

    [Fact]
    public void Build_RejectsNonPositiveIncomingMessageCapacity()
    {
        var builder = new LlrpReaderOptionsBuilder("reader.local")
            .WithIncomingMessageCapacity(0);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build());
    }

    [Fact]
    public void Build_RejectsTimeoutBeyondRuntimeTimerRange()
    {
        var builder = new LlrpReaderOptionsBuilder("reader.local")
            .WithRequestTimeout(TimeSpan.FromDays(100));

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build());
    }

    [Fact]
    public async Task ReaderBuilder_ConfiguresProtocolAfterStandardModuleInAddedOrder()
    {
        var transport = new ScriptedLlrpTransport();
        var calls = new List<string>();
        LlrpReader reader = LlrpReader.CreateBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .ConfigureProtocol(registry =>
            {
                Keepalive decoded = Assert.IsType<Keepalive>(registry.DecodeMessage(
                    LlrpTestFrames.EmptyMessage(Keepalive.MessageType, 1)));
                Assert.Equal(1U, decoded.MessageId);
                calls.Add("first");
            })
            .ConfigureProtocol(_ => calls.Add("second"))
            .Build();

        Assert.Equal(["first", "second"], calls);
        await reader.DisposeAsync();
    }

    [Fact]
    public void ConfigureProtocol_RejectsConflictWithStandardModuleBeforeTransportCreation()
    {
        bool transportCreated = false;
        LlrpReaderBuilder builder = LlrpReader.CreateBuilder("scripted.local")
            .WithTransportFactory(_ =>
            {
                transportCreated = true;
                return new ScriptedLlrpTransport();
            })
            .ConfigureProtocol(Llrp101StandardModule.Register);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("already registered", exception.Message, StringComparison.Ordinal);
        Assert.False(transportCreated);
    }

    [Fact]
    public async Task Build_FreezesProtocolConfigurationCallbacks()
    {
        var transport = new ScriptedLlrpTransport();
        var calls = new List<string>();
        var builder = new LlrpReaderOptionsBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .ConfigureProtocol(_ => calls.Add("frozen"));
        LlrpReaderOptions options = builder.Build();
        builder.ConfigureProtocol(_ => calls.Add("later"));

        var reader = new LlrpReader(options);

        Assert.Equal(["frozen"], calls);
        await reader.DisposeAsync();
    }
}
