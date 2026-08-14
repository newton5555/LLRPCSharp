using LlrpNet.Core.Protocol;
using LlrpSdk.Tests.Support;
using Xunit;

namespace LlrpSdk.Tests;

public sealed class Llrp20NegotiationTests
{
    [Fact]
    public async Task Force20_RejectsReaderThatSupportsOnly11()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport
        {
            SupportedVersionResponseFactory = id => LlrpTestFrames.SupportedVersionResponse(id, supportedVersion: 2),
        };
        LlrpReaderOptions options = new LlrpReaderOptionsBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force20)
            .Build();
        await using var reader = new LlrpReader(options);

        await Assert.ThrowsAsync<NotSupportedException>(() => reader.ConnectAsync(timeout.Token));
    }

    [Fact]
    public async Task Auto_SendsSetProtocolVersion20_WhenReaderSupports20()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport
        {
            SupportedVersionResponseFactory = id => LlrpTestFrames.SupportedVersionResponse(id, supportedVersion: 3),
        };
        LlrpReaderOptions options = new LlrpReaderOptionsBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .Build();
        await using var reader = new LlrpReader(options);

        // 脚本化设备的后续能力响应仍是 1.0.1 帧,初始化会失败;本测试只验证协商核心:
        // SET_PROTOCOL_VERSION(TargetVersion=3) 已按 Auto 策略发出。
        await Assert.ThrowsAnyAsync<Exception>(() => reader.ConnectAsync(timeout.Token));

        byte[]? setFrame = transport.SentFrames.FirstOrDefault(frame =>
            LlrpMessageHeader.Decode(frame).MessageType ==
            LlrpNet.Protocol.Messages.V1_1.SET_PROTOCOL_VERSION.MessageType);
        Assert.NotNull(setFrame);
        // MessageId 在 10 字节帧头中,载荷仅 TargetVersion(1 字节)
        Assert.True(
            setFrame!.Length >= LlrpMessageHeader.EncodedLength + 1,
            $"SET_PROTOCOL_VERSION frame is {setFrame.Length} bytes: {Convert.ToHexString(setFrame)}");
        Assert.Equal((byte)LlrpProtocolVersion.Version20, setFrame[LlrpMessageHeader.EncodedLength]);
    }
}
