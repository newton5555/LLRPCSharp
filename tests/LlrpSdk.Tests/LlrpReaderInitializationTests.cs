using System.Collections.Concurrent;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using System.Reflection;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpSdk.Tests.Support;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V11Parameters = LlrpNet.Protocol.Parameters.V1_1;

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
        GET_READER_CAPABILITIES_RESPONSE rawResponse =
            Assert.IsType<GET_READER_CAPABILITIES_RESPONSE>(capabilities.RawResponse);
        Assert.Single(rawResponse.CustomItems);

        byte[][] requests = transport.SentFrames.Where(static frame =>
            LlrpMessageHeader.Decode(frame).MessageType == V101Messages.GET_READER_CAPABILITIES.MessageType)
            .ToArray();
        Assert.Equal(2, requests.Length);

        byte[] firstRequest = requests[0];
        LlrpMessageHeader firstHeader = LlrpMessageHeader.Decode(firstRequest);
        Assert.NotEqual(0U, firstHeader.MessageId);
        Assert.Equal((byte)GetReaderCapabilitiesRequestedData.General_Device_Capabilities, firstRequest[LlrpMessageHeader.EncodedLength]);

        byte[] secondRequest = requests[1];
        LlrpMessageHeader secondHeader = LlrpMessageHeader.Decode(secondRequest);
        Assert.NotEqual(0U, secondHeader.MessageId);
        Assert.Equal((byte)GetReaderCapabilitiesRequestedData.All, secondRequest[LlrpMessageHeader.EncodedLength]);
        Assert.Equal(secondHeader.MessageId, rawResponse.MessageId);
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
        Assert.Equal((ushort)StatusCode.M_ParameterError, exception.StatusCode);
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
        await transport.ReadSentFrameAsync(V101Messages.GET_READER_CAPABILITIES.MessageType, testTimeout.Token);
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
            CapabilityResponseFactory = messageId =>
            {
                int count = Interlocked.Increment(ref requestCount);
                if (count <= 2)
                {
                    return LlrpTestFrames.CapabilitiesResponse(
                        messageId,
                        parameters:
                        [
                            LlrpTestFrames.GeneralCapabilities(
                                maxNumberOfAntennas: 1,
                                manufacturerId: 10,
                                modelId: 11,
                                firmwareVersion: "first"),
                        ]);
                }
                return null;
            }
        };
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        await transport.ReadSentFrameAsync(V101Messages.GET_READER_CAPABILITIES.MessageType, timeout.Token);
        await transport.ReadSentFrameAsync(V101Messages.GET_READER_CAPABILITIES.MessageType, timeout.Token);
        ReaderIdentity firstIdentity = Assert.IsType<ReaderIdentity>(reader.Identity);
        ReaderCapabilities firstCapabilities = Assert.IsType<ReaderCapabilities>(reader.Capabilities);

        Task reconnectTask = reader.ReconnectAsync(timeout.Token);
        byte[] secondRequest1 = await transport.ReadSentFrameAsync(
            V101Messages.GET_READER_CAPABILITIES.MessageType,
            timeout.Token);
        Assert.Equal(ReaderConnectionState.Initializing, reader.ConnectionState);
        Assert.Null(reader.Identity);
        Assert.Null(reader.Capabilities);
        uint messageId1 = LlrpMessageHeader.Decode(secondRequest1).MessageId;
        transport.EnqueueFrame(LlrpTestFrames.CapabilitiesResponse(
            messageId1,
            parameters:
            [
                LlrpTestFrames.GeneralCapabilities(
                    maxNumberOfAntennas: 2,
                    manufacturerId: 20,
                    modelId: 21,
                    firmwareVersion: "second"),
            ]));

        byte[] secondRequest2 = await transport.ReadSentFrameAsync(
            V101Messages.GET_READER_CAPABILITIES.MessageType,
            timeout.Token);
        uint messageId2 = LlrpMessageHeader.Decode(secondRequest2).MessageId;
        transport.EnqueueFrame(LlrpTestFrames.CapabilitiesResponse(
            messageId2,
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
        Assert.Equal(4, requestCount);
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
            V101Messages.GET_READER_CAPABILITIES.MessageType,
            timeout.Token);
        Assert.Equal(ReaderConnectionState.Initializing, reader.ConnectionState);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.Protocol.SendAsync(new V101Messages.KEEPALIVE_ACK(99), timeout.Token));
        Assert.Contains(nameof(ReaderConnectionState.Initializing), exception.Message, StringComparison.Ordinal);

        uint messageId = LlrpMessageHeader.Decode(request).MessageId;
        transport.EnqueueFrame(LlrpTestFrames.CapabilitiesResponse(messageId));

        byte[] allRequest = await transport.ReadSentFrameAsync(
            V101Messages.GET_READER_CAPABILITIES.MessageType,
            timeout.Token);
        uint allMsgId = LlrpMessageHeader.Decode(allRequest).MessageId;
        transport.EnqueueFrame(LlrpTestFrames.CapabilitiesResponse(allMsgId));

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
            if (header.MessageType == V101Messages.GET_READER_CAPABILITIES.MessageType)
            {
                transport.EnqueueFrame(LlrpTestFrames.EmptyMessage(
                    V101Messages.KEEPALIVE.MessageType,
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
        V101Messages.KEEPALIVE keepalive= Assert.IsType<V101Messages.KEEPALIVE>(messages.Current);
        byte[] acknowledgement = await transport.ReadSentFrameAsync(
            V101Messages.KEEPALIVE_ACK.MessageType,
            timeout.Token);
        LlrpMessageHeader acknowledgementHeader = LlrpMessageHeader.Decode(acknowledgement);
        Assert.Equal(keepalive.MessageId, acknowledgementHeader.MessageId);
    }

    [Fact]
    public async Task Connect_WhenExtensionRegistered_ExecutesActiveInitialization()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();

        GeneralDeviceCapabilities general = LlrpTestFrames.GeneralCapabilities(
            manufacturerId: 12_345,
            modelId: 67_890,
            firmwareVersion: "fw-初始化");

        transport.CapabilityResponseFactory = messageId => LlrpTestFrames.CapabilitiesResponse(
            messageId,
            parameters: [general]);

        LlrpCodecRegistry registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);

        transport.OnSendAsync = (frame, ct) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            if (header.MessageType == V101Messages.ENABLE_ROSPEC.MessageType)
            {
                var response = new V101Messages.ENABLE_ROSPEC_RESPONSE(
                    header.MessageId,
                    new LLRPStatus(StatusCode.M_Success, string.Empty, null, null));
                byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, response);
                transport.EnqueueFrame(encoded);
            }
            return ValueTask.CompletedTask;
        };

        var extension = new MockActiveExtension();
        LlrpReaderBuilder builder = LlrpReader.CreateBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .UseReaderExtension(extension);

        await using LlrpReader reader = builder.Build();
        await reader.ConnectAsync(timeout.Token);

        var sentTypes = transport.SentFrames
            .Select(frame => LlrpMessageHeader.Decode(frame).MessageType)
            .Where(type => type == V101Messages.GET_READER_CAPABILITIES.MessageType || type == V101Messages.ENABLE_ROSPEC.MessageType)
            .ToArray();

        Assert.Equal(3, sentTypes.Length);
        Assert.Equal(V101Messages.GET_READER_CAPABILITIES.MessageType, sentTypes[0]);
        Assert.Equal(V101Messages.ENABLE_ROSPEC.MessageType, sentTypes[1]);
        Assert.Equal(V101Messages.GET_READER_CAPABILITIES.MessageType, sentTypes[2]);
    }

    [Fact]
    public async Task InventoryContributor_ReceivesInitializedIdentityCapabilitiesAndProtocolVersion()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        var extension = new CapturingInventoryExtension();
        await using LlrpReader reader = LlrpReader.CreateBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .UseReaderExtension(extension)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        MethodInfo buildInventoryCustomItems = typeof(LlrpReader).GetMethod(
            "BuildInventoryCustomItems",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        _ = buildInventoryCustomItems.Invoke(reader, [new InventorySettings()]);

        LlrpSdk.Extensions.InventoryContributionContext context = Assert.IsType<LlrpSdk.Extensions.InventoryContributionContext>(extension.Context);
        Assert.Equal(LlrpTestFrames.DefaultManufacturerId, context.Identity.ManufacturerId);
        Assert.Equal(LlrpTestFrames.DefaultModelId, context.Identity.ModelId);
        Assert.Equal(LlrpTestFrames.DefaultFirmwareVersion, context.Identity.FirmwareVersion);
        Assert.Same(reader.Capabilities, context.Capabilities);
        Assert.Equal(LlrpProtocolVersion.Version101, context.ProtocolVersion);
    }

    [Fact]
    public async Task GetDefaultSettings_UsesTheActiveReaderProfileAndDoesNotQueryResources()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        var extension = new DefaultSettingsExtension();
        await using LlrpReader reader = LlrpReader.CreateBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .UseReaderExtension(extension)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync(timeout.Token);

        Assert.Equal("test.defaults", defaults.ProfileId);
        Assert.Equal(ReaderSettingsDefaultSource.ReaderProfile, defaults.Source);
        Assert.Equal([(ushort)2], defaults.Settings.Inventory!.AntennaIds);
        Assert.Equal(0, transport.SentFrames.Count(frame =>
        {
            ushort messageType = LlrpMessageHeader.Decode(frame).MessageType;
            return messageType is GET_READER_CONFIG.MessageType or GET_ROSPECS.MessageType or GET_ACCESSSPECS.MessageType;
        }));
    }

    [Fact]
    public void InventoryCompilers_PlaceContributedCustomItemsOnRoReportSpec()
    {
        var customItem = new RawCustomParameter(
            LlrpProtocolVersion.Version101,
            vendorId: 25_882,
            subtype: 53,
            data: [1]);
        var settings = new InventorySettings();

        var v101 = (V101Parameters.ROSpec)InvokeInventoryCompiler(
            "LlrpSdk.Llrp101InventoryCompiler",
            settings,
            [customItem]);
        var v11 = (V11Parameters.ROSpec)InvokeInventoryCompiler(
            "LlrpSdk.Llrp11InventoryCompiler",
            settings,
            [customItem]);

        Assert.Same(customItem, Assert.Single(v101.ROReportSpec!.CustomItems));
        Assert.Empty(Assert.IsType<V101Parameters.AISpec>(Assert.Single(v101.SpecParameterItems)).CustomItems);
        Assert.Same(customItem, Assert.Single(v11.ROReportSpec!.CustomItems));
        Assert.Empty(Assert.IsType<V11Parameters.AISpec>(Assert.Single(v11.SpecParameterItems)).CustomItems);
    }

    [Fact]
    public void Llrp11InventoryCompiler_PreservesHighLevelReportSettings()
    {
        var settings = new InventorySettings
        {
            Report = new InventoryReportSettings
            {
                Trigger = InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
                IncludeRoSpecId = false,
                IncludeAntennaId = false,
                IncludePeakRssi = false,
                IncludePcBits = true,
                IncludeCrc = true,
            },
        };

        var roSpec = (V11Parameters.ROSpec)InvokeInventoryCompiler(
            "LlrpSdk.Llrp11InventoryCompiler", settings, []);
        V11Parameters.ROReportSpec report = roSpec.ROReportSpec!;
        V11Parameters.TagReportContentSelector selector = report.TagReportContentSelector;

        Assert.Equal("Upon_N_Tags_Or_End_Of_ROSpec", report.ROReportTrigger.ToString());
        Assert.False(selector.EnableROSpecID);
        Assert.False(selector.EnableAntennaID);
        Assert.False(selector.EnablePeakRSSI);
        var epc = Assert.IsType<V11Parameters.C1G2EPCMemorySelector>(Assert.Single(selector.AirProtocolEPCMemorySelectorItems));
        Assert.True(epc.EnableCRC);
        Assert.True(epc.EnablePCBits);
    }

    private static ILlrpParameter InvokeInventoryCompiler(
        string typeName,
        InventorySettings settings,
        IReadOnlyList<ILlrpParameter> customItems)
    {
        Type compilerType = typeof(LlrpReader).Assembly.GetType(typeName, throwOnError: true)!;
        MethodInfo compile = compilerType.GetMethod(
            "Compile",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(InventorySettings), typeof(IReadOnlyList<ILlrpParameter>), typeof(bool)],
            modifiers: null)!;
        return Assert.IsAssignableFrom<ILlrpParameter>(compile.Invoke(null, [settings, customItems, false]));
    }

    private sealed class MockActiveExtension : LlrpSdk.Extensions.IReaderExtension
    {
        public string Id => "mock-active-extension";
        public string? MutualExclusionGroup => "reader-vendor";

        public bool Matches(LlrpSdk.Extensions.ReaderExtensionMatchContext context)
        {
            return context.ManufacturerId == 12_345U;
        }

        public async Task InitializeConnectionAsync(
            LlrpSdk.Extensions.IReaderConnection connection,
            CancellationToken cancellationToken)
        {
            await connection.TransactAsync<V101Messages.ENABLE_ROSPEC_RESPONSE>(
                new V101Messages.ENABLE_ROSPEC(connection.NextMessageId(), 99),
                timeout: null,
                cancellationToken: cancellationToken);
        }
    }

    private sealed class CapturingInventoryExtension :
        LlrpSdk.Extensions.IReaderExtension,
        LlrpSdk.Extensions.IInventoryContributor
    {
        public string Id => "capturing-inventory-extension";
        public string? MutualExclusionGroup => null;
        public LlrpSdk.Extensions.InventoryContributionContext? Context { get; private set; }

        public bool Matches(LlrpSdk.Extensions.ReaderExtensionMatchContext context) =>
            context.ManufacturerId == LlrpTestFrames.DefaultManufacturerId;

        public void Contribute(
            LlrpSdk.Extensions.InventoryContributionContext context,
            LlrpSdk.Extensions.InventoryExtensionBuilder extensions)
        {
            Context = context;
        }
    }

    private sealed class DefaultSettingsExtension :
        LlrpSdk.Extensions.IReaderExtension,
        IReaderSettingsDefaultsContributor
    {
        public string Id => "test-default-settings";
        public string? MutualExclusionGroup => null;

        public bool Matches(LlrpSdk.Extensions.ReaderExtensionMatchContext context) =>
            context.ManufacturerId == LlrpTestFrames.DefaultManufacturerId;

        public ReaderSettingsDefaults? GetDefaultSettings(ReaderSettingsDefaultContext context) => new()
        {
            ProfileId = "test.defaults",
            Source = ReaderSettingsDefaultSource.ReaderProfile,
            Settings = new ReaderSettings { Inventory = new InventorySettings { AntennaIds = [2] } }
        };
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
