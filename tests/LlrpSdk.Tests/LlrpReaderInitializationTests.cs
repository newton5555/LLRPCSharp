using System.Collections.Concurrent;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpSdk.Tests.Support;

namespace LlrpSdk.Tests;

public sealed class LlrpReaderInitializationTests
{
    [Fact]
    public async Task Connect_QueriesAllCapabilitiesAndPublishesImmutableMetadata()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        var additional = new RawCustomParameter(
            LlrpProtocolVersion.Version101,
            vendorId: 12_345,
            subtype: 7,
            data: [0x30]);
        GeneralDeviceCapabilities general = LlrpTestFrames.GeneralCapabilities(
            maxNumberOfAntennas: 8,
            canSetAntennaProperties: false,
            hasUtcClockCapability: true,
            manufacturerId: 12_345,
            modelId: 67_890,
            firmwareVersion: "fw-初始化");
        transport.CapabilityResponseFactory = messageId => LlrpTestFrames.CapabilitiesResponse(
            messageId,
            parameters: [general, additional]);
        await using LlrpReader reader = CreateReader(transport);
        var transitions = new ConcurrentQueue<ReaderConnectionState>();
        reader.ConnectionChanged += (_, args) => transitions.Enqueue(args.CurrentState);

        await reader.ConnectAsync(timeout.Token);

        Assert.Equal(
            [
                ReaderConnectionState.Connecting,
                ReaderConnectionState.Negotiating,
                ReaderConnectionState.Initializing,
                ReaderConnectionState.Ready,
            ],
            transitions.ToArray());
        ReaderIdentity identity = Assert.IsType<ReaderIdentity>(reader.Identity);
        Assert.Equal(12_345U, identity.ManufacturerId);
        Assert.Equal(67_890U, identity.ModelId);
        Assert.Equal("fw-初始化", identity.FirmwareVersion);

        ReaderCapabilities capabilities = Assert.IsType<ReaderCapabilities>(reader.Capabilities);
        Assert.Equal(8, capabilities.MaxNumberOfAntennas);
        Assert.False(capabilities.CanSetAntennaProperties);
        Assert.True(capabilities.HasUtcClockCapability);
        Assert.Equal(3, capabilities.GeneralDeviceParameters.Count);
        Assert.IsType<ReceiveSensitivityTableEntry>(capabilities.GeneralDeviceParameters[0]);
        RawCustomParameter decodedAdditional =
            Assert.IsType<RawCustomParameter>(Assert.Single(capabilities.AdditionalParameters));
        Assert.Equal(12_345U, decodedAdditional.VendorId);
        Assert.Equal(7U, decodedAdditional.Subtype);
        Assert.Equal(new byte[] { 0x30 }, decodedAdditional.Data.ToArray());
        Assert.Single(capabilities.RawResponse.CustomItems);

        byte[] request = transport.SentFrames.Single(static frame =>
            LlrpMessageHeader.Decode(frame).MessageType == GetReaderCapabilities.MessageType);
        LlrpMessageHeader requestHeader = LlrpMessageHeader.Decode(request);
        Assert.NotEqual(0U, requestHeader.MessageId);
        Assert.Equal((byte)GetReaderCapabilitiesRequestedData.All, request[LlrpMessageHeader.EncodedLength]);
        Assert.Equal(requestHeader.MessageId, capabilities.RawResponse.MessageId);
    }

    [Fact]
    public async Task Connect_NonSuccessStatusThrowsOperationExceptionAndDoesNotPublishMetadata()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport
        {
            CapabilityResponseFactory = messageId => LlrpTestFrames.CapabilitiesResponse(
                messageId,
                new LLRPStatus(StatusCode.M_ParameterError, "capabilities rejected", null, null),
                []),
        };
        await using LlrpReader reader = CreateReader(transport);

        LlrpReaderOperationException exception =
            await Assert.ThrowsAsync<LlrpReaderOperationException>(() => reader.ConnectAsync(timeout.Token));

        Assert.Equal("GET_READER_CAPABILITIES", exception.Operation);
        Assert.Equal(StatusCode.M_ParameterError, exception.StatusCode);
        Assert.Equal("capabilities rejected", exception.ErrorDescription);
        Assert.Contains(nameof(StatusCode.M_ParameterError), exception.Message, StringComparison.Ordinal);
        Assert.Equal(ReaderConnectionState.Faulted, reader.ConnectionState);
        Assert.Null(reader.Identity);
        Assert.Null(reader.Capabilities);
    }

    [Fact]
    public async Task Connect_RequiresGeneralDeviceCapabilities()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport
        {
            CapabilityResponseFactory = messageId =>
                LlrpTestFrames.CapabilitiesResponse(messageId, parameters: []),
        };
        await using LlrpReader reader = CreateReader(transport);

        LlrpReaderInitializationException exception =
            await Assert.ThrowsAsync<LlrpReaderInitializationException>(() =>
                reader.ConnectAsync(timeout.Token));

        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
        Assert.Contains("0", exception.Message, StringComparison.Ordinal);
        Assert.Equal(ReaderConnectionState.Faulted, reader.ConnectionState);
        Assert.Null(reader.Identity);
        Assert.Null(reader.Capabilities);
    }

    [Fact]
    public async Task Connect_DuplicateGeneralDeviceCapabilitiesIsInitializationFailure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport
        {
            CapabilityResponseFactory = messageId =>
                LlrpTestFrames.CapabilitiesResponseWithDuplicateGeneral(messageId),
        };
        await using LlrpReader reader = CreateReader(transport);

        LlrpReaderInitializationException exception =
            await Assert.ThrowsAsync<LlrpReaderInitializationException>(() =>
                reader.ConnectAsync(timeout.Token));

        Assert.Contains("could not be decoded", exception.Message, StringComparison.Ordinal);
        Assert.IsType<LlrpProtocolException>(exception.InnerException);
        Assert.Equal(ReaderConnectionState.Faulted, reader.ConnectionState);
        Assert.Null(reader.Identity);
        Assert.Null(reader.Capabilities);
    }

    [Fact]
    public async Task Connect_CancellationDuringInitializationReturnsToDisconnected()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var connectCancellation = new CancellationTokenSource();
        var transport = new ScriptedLlrpTransport
        {
            CapabilityResponseFactory = _ => null,
        };
        await using LlrpReader reader = CreateReader(transport, TimeSpan.FromSeconds(30));

        Task connectTask = reader.ConnectAsync(connectCancellation.Token);
        await transport.ReadSentFrameAsync(GetReaderCapabilities.MessageType, testTimeout.Token);
        Assert.Equal(ReaderConnectionState.Initializing, reader.ConnectionState);
        connectCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connectTask);
        Assert.Equal(ReaderConnectionState.Disconnected, reader.ConnectionState);
        Assert.Null(reader.Identity);
        Assert.Null(reader.Capabilities);
    }

    [Fact]
    public async Task Connect_InitializationTimeoutTransitionsToFaulted()
    {
        var transport = new ScriptedLlrpTransport
        {
            CapabilityResponseFactory = _ => null,
        };
        await using LlrpReader reader = CreateReader(transport, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<TimeoutException>(() => reader.ConnectAsync());

        Assert.Equal(ReaderConnectionState.Faulted, reader.ConnectionState);
        Assert.Null(reader.Identity);
        Assert.Null(reader.Capabilities);
    }

    [Fact]
    public async Task Reconnect_InvalidatesAndRefreshesMetadataFromNewResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int requestCount = 0;
        var transport = new ScriptedLlrpTransport
        {
            CapabilityResponseFactory = messageId => Interlocked.Increment(ref requestCount) == 1
                ? LlrpTestFrames.CapabilitiesResponse(
                    messageId,
                    parameters:
                    [
                        LlrpTestFrames.GeneralCapabilities(
                            maxNumberOfAntennas: 1,
                            manufacturerId: 10,
                            modelId: 11,
                            firmwareVersion: "first"),
                    ])
                : null,
        };
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        await transport.ReadSentFrameAsync(GetReaderCapabilities.MessageType, timeout.Token);
        ReaderIdentity firstIdentity = Assert.IsType<ReaderIdentity>(reader.Identity);
        ReaderCapabilities firstCapabilities = Assert.IsType<ReaderCapabilities>(reader.Capabilities);

        Task reconnectTask = reader.ReconnectAsync(timeout.Token);
        byte[] secondRequest = await transport.ReadSentFrameAsync(
            GetReaderCapabilities.MessageType,
            timeout.Token);
        Assert.Equal(ReaderConnectionState.Initializing, reader.ConnectionState);
        Assert.Null(reader.Identity);
        Assert.Null(reader.Capabilities);
        uint messageId = LlrpMessageHeader.Decode(secondRequest).MessageId;
        transport.EnqueueFrame(LlrpTestFrames.CapabilitiesResponse(
            messageId,
            parameters:
            [
                LlrpTestFrames.GeneralCapabilities(
                    maxNumberOfAntennas: 2,
                    manufacturerId: 20,
                    modelId: 21,
                    firmwareVersion: "second"),
            ]));

        await reconnectTask;

        ReaderIdentity secondIdentity = Assert.IsType<ReaderIdentity>(reader.Identity);
        ReaderCapabilities secondCapabilities = Assert.IsType<ReaderCapabilities>(reader.Capabilities);
        Assert.NotSame(firstIdentity, secondIdentity);
        Assert.NotSame(firstCapabilities, secondCapabilities);
        Assert.Equal(20U, secondIdentity.ManufacturerId);
        Assert.Equal("second", secondIdentity.FirmwareVersion);
        Assert.Equal(2, secondCapabilities.MaxNumberOfAntennas);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task PublicProtocol_IsRejectedUntilInitializationCompletes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport
        {
            CapabilityResponseFactory = _ => null,
        };
        await using LlrpReader reader = CreateReader(transport, TimeSpan.FromSeconds(3));

        Task connectTask = reader.ConnectAsync(timeout.Token);
        byte[] request = await transport.ReadSentFrameAsync(
            GetReaderCapabilities.MessageType,
            timeout.Token);
        Assert.Equal(ReaderConnectionState.Initializing, reader.ConnectionState);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.Protocol.SendAsync(new KeepaliveAck(99), timeout.Token));
        Assert.Contains(nameof(ReaderConnectionState.Initializing), exception.Message, StringComparison.Ordinal);

        uint messageId = LlrpMessageHeader.Decode(request).MessageId;
        transport.EnqueueFrame(LlrpTestFrames.CapabilitiesResponse(messageId));
        await connectTask;
        Assert.Equal(ReaderConnectionState.Ready, reader.ConnectionState);
    }

    [Fact]
    public async Task Initialization_IgnoresAndAcknowledgesReaderMessageWithCollidingId()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport
        {
            AutoRespondToCapabilities = false,
        };
        transport.OnSendAsync = (frame, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            if (header.MessageType == GetReaderCapabilities.MessageType)
            {
                transport.EnqueueFrame(LlrpTestFrames.EmptyMessage(
                    Keepalive.MessageType,
                    header.MessageId));
                transport.EnqueueFrame(LlrpTestFrames.CapabilitiesResponse(header.MessageId));
            }

            return ValueTask.CompletedTask;
        };
        await using LlrpReader reader = CreateReader(transport);
        await using IAsyncEnumerator<LlrpNet.Protocol.Messages.ILlrpMessage> messages = reader
            .ReadMessagesAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        await reader.ConnectAsync(timeout.Token);

        Assert.Equal(ReaderConnectionState.Ready, reader.ConnectionState);
        Assert.True(await messages.MoveNextAsync());
        Keepalive keepalive = Assert.IsType<Keepalive>(messages.Current);
        byte[] acknowledgement = await transport.ReadSentFrameAsync(
            KeepaliveAck.MessageType,
            timeout.Token);
        LlrpMessageHeader acknowledgementHeader = LlrpMessageHeader.Decode(acknowledgement);
        Assert.Equal(keepalive.MessageId, acknowledgementHeader.MessageId);
    }

    private static LlrpReader CreateReader(
        ScriptedLlrpTransport transport,
        TimeSpan? requestTimeout = null)
    {
        LlrpReaderBuilder builder = LlrpReader.CreateBuilder("scripted.local")
            .WithTransportFactory(_ => transport);
        if (requestTimeout is TimeSpan configuredTimeout)
        {
            builder.WithRequestTimeout(configuredTimeout);
        }

        return builder.Build();
    }
}
