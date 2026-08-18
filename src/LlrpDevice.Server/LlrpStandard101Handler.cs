using System.Buffers.Binary;
using LlrpDevice.Abstractions;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using V101Choices = LlrpNet.Protocol.Choices.V1_0_1;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V101Registry = LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpDevice.Server;

internal interface ILlrpDeviceVersionProfile : ILlrpDeviceMessageHandler
{
    public LlrpProtocolVersion Version { get; }

    public ILlrpMessage CreateError(uint messageId, ushort statusCode, string description);

    public ILlrpMessage CreateKeepalive(uint messageId);

    public ILlrpMessage CreateReaderEventNotification(uint messageId, LlrpDeviceEvent? deviceEvent = null);

    public ILlrpMessage CreateCloseConnection(uint messageId);

    public IReadOnlyList<ILlrpMessage> BuildInventoryReports(uint roSpecId, int roundSequence);
}

/// <summary>
/// Handles the standard LLRP 1.0.1 device messages and owns the canonical resource-state transitions.
/// </summary>
internal sealed class LlrpStandard101Handler : ILlrpDeviceVersionProfile
{
    private readonly LlrpDeviceServerState _state;

    public LlrpStandard101Handler(LlrpDeviceServerState state)
    {
        _state = state;
    }

    public string Name => "standard-llrp-1.0.1";

    public LlrpProtocolVersion Version => LlrpProtocolVersion.Version101;

    public bool CanHandle(LlrpProtocolVersion version, ILlrpMessage message) => version == Version;

    public ValueTask<LlrpDeviceDispatchResult> HandleAsync(
        LlrpDeviceRequestContext context,
        ILlrpMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LlrpDeviceDispatchResult result = message switch
        {
            V101Messages.GET_READER_CAPABILITIES request => Response(Capabilities(request)),
            V101Messages.GET_READER_CONFIG request => Response(GetReaderConfig(request)),
            V101Messages.SET_READER_CONFIG request => Response(SetReaderConfig(request)),
            V101Messages.ADD_ROSPEC request => Response(AddRoSpec(request)),
            V101Messages.GET_ROSPECS request => Response(GetRoSpecs(request)),
            V101Messages.DELETE_ROSPEC request => Response(DeleteRoSpec(request)),
            V101Messages.ENABLE_ROSPEC request => Response(EnableRoSpec(request)),
            V101Messages.DISABLE_ROSPEC request => Response(DisableRoSpec(request)),
            V101Messages.START_ROSPEC request => StartRoSpec(context, request),
            V101Messages.STOP_ROSPEC request => StopRoSpec(context, request),
            V101Messages.ADD_ACCESSSPEC request => Response(AddAccessSpec(request)),
            V101Messages.GET_ACCESSSPECS request => Response(GetAccessSpecs(request)),
            V101Messages.DELETE_ACCESSSPEC request => Response(DeleteAccessSpec(request)),
            V101Messages.ENABLE_ACCESSSPEC request => EnableAccessSpec(request),
            V101Messages.DISABLE_ACCESSSPEC request => Response(DisableAccessSpec(request)),
            V101Messages.GET_REPORT request => new LlrpDeviceDispatchResult(
                null,
                [_state.TakeBufferedReport(request.MessageId)]),
            V101Messages.KEEPALIVE request => Response(new V101Messages.KEEPALIVE_ACK(request.MessageId)),
            V101Messages.KEEPALIVE_ACK => new LlrpDeviceDispatchResult(null, []),
            V101Messages.CLOSE_CONNECTION request => new(
                new V101Messages.CLOSE_CONNECTION_RESPONSE(request.MessageId, Status(V101Enumerations.StatusCode.M_Success, string.Empty)),
                [],
                CloseConnection: true),
            V101Messages.ENABLE_EVENTS_AND_REPORTS => EnableEventsAndReports(),
            _ => Response(CreateError(message.MessageId, (ushort)V101Enumerations.StatusCode.M_UnsupportedMessage,
                "The LLRP device does not implement this LLRP 1.0.1 message.")),
        };

        return ValueTask.FromResult(result);
    }

    public ILlrpMessage CreateError(uint messageId, ushort statusCode, string description) =>
        new V101Messages.ERROR_MESSAGE(
            messageId,
            new V101Parameters.LLRPStatus(
                (V101Enumerations.StatusCode)statusCode,
                description,
                null,
                null));

    public ILlrpMessage CreateKeepalive(uint messageId) => new V101Messages.KEEPALIVE(messageId);

    public ILlrpMessage CreateReaderEventNotification(uint messageId, LlrpDeviceEvent? deviceEvent = null)
    {
        ulong microseconds = checked((ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000));
        var data = new V101Parameters.ReaderEventNotificationData(
            new V101Parameters.UTCTimestamp(microseconds),
            null,
            deviceEvent?.Name == "gpi.changed" && deviceEvent.GpiPortNumber is ushort gpiPort && deviceEvent.GpiState is bool gpiState
                ? new V101Parameters.GPIEvent(gpiPort, gpiState)
                : null,
            deviceEvent?.Name is "rospec.started" or "rospec.stopped" && deviceEvent.RoSpecId is uint roSpecId
                ? new V101Parameters.ROSpecEvent(
                    deviceEvent.Name == "rospec.started"
                        ? V101Enumerations.ROSpecEventType.Start_Of_ROSpec
                        : V101Enumerations.ROSpecEventType.End_Of_ROSpec,
                    roSpecId,
                    0)
                : null,
            deviceEvent?.Name == "report.buffer.warning" && deviceEvent.ReportBufferPercentage is byte percentage
                ? new V101Parameters.ReportBufferLevelWarningEvent(percentage)
                : null,
            deviceEvent?.Name == "report.buffer.overflow"
                ? new V101Parameters.ReportBufferOverflowErrorEvent()
                : null,
            deviceEvent?.Name == "reader.exception"
                ? new V101Parameters.ReaderExceptionEvent(
                    deviceEvent.Detail ?? deviceEvent.Error?.Message ?? "Reader exception",
                    deviceEvent.RoSpecId is uint exceptionRoSpecId ? new V101Parameters.ROSpecID(exceptionRoSpecId) : null,
                    deviceEvent.SpecIndex is ushort exceptionSpecIndex ? new V101Parameters.SpecIndex(exceptionSpecIndex) : null,
                    deviceEvent.InventoryParameterSpecId is ushort exceptionInventorySpecId
                        ? new V101Parameters.InventoryParameterSpecID(exceptionInventorySpecId)
                        : null,
                    deviceEvent.AntennaId is ushort exceptionAntennaId ? new V101Parameters.AntennaID(exceptionAntennaId) : null,
                    deviceEvent.AccessSpecId is uint exceptionAccessSpecId ? new V101Parameters.AccessSpecID(exceptionAccessSpecId) : null,
                    deviceEvent.OpSpecId is ushort exceptionOpSpecId ? new V101Parameters.OpSpecID(exceptionOpSpecId) : null,
                    [])
                : null,
            null,
            null,
            deviceEvent?.Name == "antenna.changed" && deviceEvent.AntennaId is ushort antennaId && deviceEvent.AntennaConnected is bool connected
                ? new V101Parameters.AntennaEvent(
                    connected
                        ? V101Enumerations.AntennaEventType.Antenna_Connected
                        : V101Enumerations.AntennaEventType.Antenna_Disconnected,
                    antennaId)
                : null,
            deviceEvent is null
                ? new V101Parameters.ConnectionAttemptEvent(V101Enumerations.ConnectionAttemptStatusType.Success)
                : null,
            null,
            []);
        return new V101Messages.READER_EVENT_NOTIFICATION(messageId, data);
    }

    public ILlrpMessage CreateCloseConnection(uint messageId) => new V101Messages.CLOSE_CONNECTION(messageId);

    public IReadOnlyList<ILlrpMessage> BuildInventoryReports(uint roSpecId, int roundSequence)
    {
        if (!_state.TryGetRoSpec(roSpecId, out V101Parameters.ROSpec? roSpec) ||
            roSpec is null ||
            roSpec.CurrentState != V101Enumerations.ROSpecState.Active)
        {
            return [];
        }

        LlrpInventoryPlan plan = _state.BuildInventoryPlan(roSpecId);
        int requestedAntennaCount = plan.AntennaIds.Count;
        plan = plan with
        {
            AntennaIds = plan.AntennaIds.Where(_state.IsAntennaConnected).ToArray(),
        };
        if (requestedAntennaCount > 0 && plan.AntennaIds.Count == 0)
        {
            return [];
        }
        LlrpDeviceTag[] tags = _state.Inventory.Observe(
                plan,
                new LlrpInventoryRound(
                    roSpecId,
                    roundSequence,
                    plan.AntennaIds))
            .ToArray();
        if (tags.Length == 0)
        {
            return [];
        }

        V101Parameters.ROReportSpec? reportSpec = roSpec.ROReportSpec;
        V101Parameters.TagReportContentSelector selector = reportSpec?.TagReportContentSelector ?? FullTagReportSelector();
        ushort inventoryParameterSpecId = plan.InventoryParameterSpecId ?? 1;
        var tagReports = tags
            .Select(tag => BuildTagReport(roSpecId, null, tag, [], selector, inventoryParameterSpecId))
            .ToArray();
        ushort reportEvery = reportSpec?.N ?? 1;
        V101Enumerations.ROReportTriggerType trigger = reportSpec?.ROReportTrigger ??
            V101Enumerations.ROReportTriggerType.Upon_N_Tags_Or_End_Of_AISpec;
        if (trigger == V101Enumerations.ROReportTriggerType.Upon_N_Tags_Or_End_Of_ROSpec)
        {
            _state.AccumulateRoSpecReport(roSpecId, tagReports);
            return reportEvery == 0
                ? []
                : _state.TakeReadyRoSpecReports(roSpecId, reportEvery, NextAsyncMessageId)
                    .Cast<ILlrpMessage>()
                    .ToArray();
        }

        if (reportEvery == 0 || trigger == V101Enumerations.ROReportTriggerType.None)
        {
            return [new V101Messages.RO_ACCESS_REPORT(NextAsyncMessageId(), tagReports, [], [])];
        }

        return tagReports
            .Chunk(reportEvery)
            .Select(chunk => (ILlrpMessage)new V101Messages.RO_ACCESS_REPORT(
                NextAsyncMessageId(),
                chunk,
                [],
                []))
            .ToArray();
    }

    private LlrpDeviceDispatchResult EnableEventsAndReports()
    {
        _state.ReleaseHeldEventsAndReports();
        var messages = new List<ILlrpMessage>();
        messages.AddRange(_state.DrainHeldEvents().Select(deviceEvent =>
            CreateReaderEventNotification(NextAsyncMessageId(), deviceEvent)));
        messages.AddRange(_state.DrainBufferedReports());
        return new LlrpDeviceDispatchResult(null, messages);
    }

    private static LlrpDeviceDispatchResult Response(ILlrpMessage response) =>
        LlrpDeviceDispatchResult.FromResponse(response);

    private V101Messages.GET_READER_CAPABILITIES_RESPONSE Capabilities(V101Messages.GET_READER_CAPABILITIES request)
    {
        bool all = request.RequestedData == V101Enumerations.GetReaderCapabilitiesRequestedData.All;
        return new V101Messages.GET_READER_CAPABILITIES_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty),
            all || request.RequestedData == V101Enumerations.GetReaderCapabilitiesRequestedData.General_Device_Capabilities
                ? BuildGeneralDeviceCapabilities()
                : null,
            all || request.RequestedData == V101Enumerations.GetReaderCapabilitiesRequestedData.LLRP_Capabilities
                ? BuildLlrpCapabilities()
                : null,
            all || request.RequestedData == V101Enumerations.GetReaderCapabilitiesRequestedData.Regulatory_Capabilities
                ? BuildRegulatoryCapabilities()
                : null,
            all || request.RequestedData == V101Enumerations.GetReaderCapabilitiesRequestedData.LLRP_Air_Protocol_Capabilities
                ? BuildC1G2Capabilities()
                : null,
            _state.GetReaderCapabilitiesCustomItems());
    }

    private V101Parameters.GeneralDeviceCapabilities BuildGeneralDeviceCapabilities()
    {
        LlrpDeviceCapabilities capabilities = _state.Device.Capabilities;
        IReadOnlyList<V101Parameters.ReceiveSensitivityTableEntry> receiveSensitivityTable =
            capabilities.ReceiveSensitivityLevels.Count > 0
                ? capabilities.ReceiveSensitivityLevels
                    .Select(static level => new V101Parameters.ReceiveSensitivityTableEntry(
                        level.Index,
                        level.ReceiveSensitivityValue))
                    .ToArray()
                : Enumerable
                    .Range(1, capabilities.MaxNumberOfAntennas)
                    .Select(static id => new V101Parameters.ReceiveSensitivityTableEntry(checked((ushort)id), 0))
                    .ToArray();

        return new V101Parameters.GeneralDeviceCapabilities(
            capabilities.MaxNumberOfAntennas,
            capabilities.CanSetAntennaProperties,
            capabilities.HasUtcClockCapability,
            _state.Device.Identity.ManufacturerId,
            _state.Device.Identity.ModelId,
            _state.Device.Identity.FirmwareVersion,
            receiveSensitivityTable,
            [],
            new V101Parameters.GPIOCapabilities(
                capabilities.MaxNumberOfGpis,
                capabilities.MaxNumberOfGpos),
            Enumerable.Range(1, capabilities.MaxNumberOfAntennas)
                .Select(static id => new V101Parameters.PerAntennaAirProtocol(
                    checked((ushort)id),
                    [V101Enumerations.AirProtocols.EPCGlobalClass1Gen2]))
                .ToArray());
    }

    private V101Parameters.LLRPCapabilities? BuildLlrpCapabilities()
    {
        if (_state.Options.ProtocolVersion == LlrpProtocolVersion.Version20)
        {
            return null;
        }

        LlrpDeviceCapabilities capabilities = _state.Device.Capabilities;
        return new V101Parameters.LLRPCapabilities(
            CanDoRFSurvey: false,
            CanReportBufferFillWarning: capabilities.SupportsReportBuffer,
            SupportsClientRequestOpSpec: false,
            CanDoTagInventoryStateAwareSingulation: capabilities.SupportsStateAwareSingulation,
            SupportsEventAndReportHolding: capabilities.SupportsEventAndReportHolding,
            MaxNumPriorityLevelsSupported: capabilities.MaxNumPriorityLevelsSupported,
            ClientRequestOpSpecTimeout: capabilities.ClientRequestOpSpecTimeout,
            MaxNumROSpecs: capabilities.MaxNumROSpecs,
            MaxNumSpecsPerROSpec: capabilities.MaxNumSpecsPerROSpec,
            MaxNumInventoryParameterSpecsPerAISpec: capabilities.MaxNumInventoryParameterSpecsPerAISpec,
            MaxNumAccessSpecs: capabilities.SupportsTagAccess ? capabilities.MaxNumAccessSpecs : 0u,
            MaxNumOpSpecsPerAccessSpec: capabilities.SupportsTagAccess ? capabilities.MaxNumOpSpecsPerAccessSpec : 0u);
    }

    private V101Parameters.C1G2LLRPCapabilities? BuildC1G2Capabilities()
    {
        if (_state.Options.ProtocolVersion == LlrpProtocolVersion.Version20)
        {
            return null;
        }

        LlrpDeviceCapabilities capabilities = _state.Device.Capabilities;
        return capabilities.SupportsEpcGlobalClass1Gen2 && capabilities.SupportsTagAccess
            ? new V101Parameters.C1G2LLRPCapabilities(
                capabilities.SupportsBlockErase,
                capabilities.SupportsBlockWrite,
                capabilities.MaxNumSelectFiltersPerQuery)
            : null;
    }

    private V101Parameters.RegulatoryCapabilities? BuildRegulatoryCapabilities()
    {
        LlrpDeviceRegulatoryCapabilities? regulatory = _state.Device.Capabilities.RegulatoryCapabilities;
        if (regulatory is null ||
            regulatory.TransmitPowerLevels.Count == 0 ||
            regulatory.C1G2RfModes.Count == 0)
        {
            return null;
        }

        var rfModeTable = new V101Parameters.C1G2UHFRFModeTable(
            regulatory.C1G2RfModes
                .Select(static mode => new V101Parameters.C1G2UHFRFModeTableEntry(
                    mode.ModeIdentifier,
                    MapDrValue(mode.DrValue),
                    mode.EpcHagTcConformance,
                    MapMValue(mode.MValue),
                    MapForwardLinkModulation(mode.ForwardLinkModulation),
                    MapSpectralMaskIndicator(mode.SpectralMaskIndicator),
                    mode.BdrValue,
                    mode.PieValue,
                    mode.MinTariValue,
                    mode.MaxTariValue,
                    mode.StepTariValue))
                .ToArray());

        var rfModeTables = new V101Choices.IAirProtocolUHFRFModeTable[] { rfModeTable };
        var frequencyInformation = new V101Parameters.FrequencyInformation(
            regulatory.Hopping,
            regulatory.FrequencyHopTables
                .Select(static table => new V101Parameters.FrequencyHopTable(table.HopTableId, table.Frequencies))
                .ToArray(),
            regulatory.FixedFrequencies.Count == 0
                ? null
                : new V101Parameters.FixedFrequencyTable(regulatory.FixedFrequencies));

        return new V101Parameters.RegulatoryCapabilities(
            regulatory.CountryCode,
            MapCommunicationsStandard(regulatory.CommunicationsStandard),
            new V101Parameters.UHFBandCapabilities(
                regulatory.TransmitPowerLevels
                    .Select(static power => new V101Parameters.TransmitPowerLevelTableEntry(
                        power.Index,
                        power.TransmitPowerValue))
                    .ToArray(),
                frequencyInformation,
                rfModeTables),
            []);
    }

    private static V101Enumerations.CommunicationsStandard MapCommunicationsStandard(
        LlrpCommunicationsStandard standard) => standard switch
    {
        LlrpCommunicationsStandard.Unspecified => V101Enumerations.CommunicationsStandard.Unspecified,
        LlrpCommunicationsStandard.UsFccPart15 => V101Enumerations.CommunicationsStandard.US_FCC_Part_15,
        LlrpCommunicationsStandard.Etsi302208 => V101Enumerations.CommunicationsStandard.ETSI_302_208,
        LlrpCommunicationsStandard.Etsi300220 => V101Enumerations.CommunicationsStandard.ETSI_300_220,
        LlrpCommunicationsStandard.AustraliaLipd1W => V101Enumerations.CommunicationsStandard.Australia_LIPD_1W,
        LlrpCommunicationsStandard.AustraliaLipd4W => V101Enumerations.CommunicationsStandard.Australia_LIPD_4W,
        LlrpCommunicationsStandard.JapanAribStdT89 => V101Enumerations.CommunicationsStandard.Japan_ARIB_STD_T89,
        LlrpCommunicationsStandard.HongKongOfta1049 => V101Enumerations.CommunicationsStandard.Hong_Kong_OFTA_1049,
        LlrpCommunicationsStandard.TaiwanDgtLp0002 => V101Enumerations.CommunicationsStandard.Taiwan_DGT_LP0002,
        LlrpCommunicationsStandard.KoreaMicArticle52 => V101Enumerations.CommunicationsStandard.Korea_MIC_Article_5_2,
        _ => throw new ArgumentOutOfRangeException(nameof(standard), standard, "Unsupported communications standard."),
    };

    private static V101Enumerations.C1G2DRValue MapDrValue(LlrpC1G2DrValue value) => value switch
    {
        LlrpC1G2DrValue.Dr8 => V101Enumerations.C1G2DRValue.DRV_8,
        LlrpC1G2DrValue.Dr64_3 => V101Enumerations.C1G2DRValue.DRV_64_3,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported C1G2 DR value."),
    };

    private static V101Enumerations.C1G2MValue MapMValue(LlrpC1G2MValue value) => value switch
    {
        LlrpC1G2MValue.Fm0 => V101Enumerations.C1G2MValue.MV_FM0,
        LlrpC1G2MValue.M2 => V101Enumerations.C1G2MValue.MV_2,
        LlrpC1G2MValue.M4 => V101Enumerations.C1G2MValue.MV_4,
        LlrpC1G2MValue.M8 => V101Enumerations.C1G2MValue.MV_8,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported C1G2 M value."),
    };

    private static V101Enumerations.C1G2ForwardLinkModulation MapForwardLinkModulation(
        LlrpC1G2ForwardLinkModulation value) => value switch
    {
        LlrpC1G2ForwardLinkModulation.PrAsk => V101Enumerations.C1G2ForwardLinkModulation.PR_ASK,
        LlrpC1G2ForwardLinkModulation.SsbAsk => V101Enumerations.C1G2ForwardLinkModulation.SSB_ASK,
        LlrpC1G2ForwardLinkModulation.DsbAsk => V101Enumerations.C1G2ForwardLinkModulation.DSB_ASK,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported C1G2 modulation."),
    };

    private static V101Enumerations.C1G2SpectralMaskIndicator MapSpectralMaskIndicator(
        LlrpC1G2SpectralMaskIndicator value) => value switch
    {
        LlrpC1G2SpectralMaskIndicator.Unknown => V101Enumerations.C1G2SpectralMaskIndicator.Unknown,
        LlrpC1G2SpectralMaskIndicator.Si => V101Enumerations.C1G2SpectralMaskIndicator.SI,
        LlrpC1G2SpectralMaskIndicator.Mi => V101Enumerations.C1G2SpectralMaskIndicator.MI,
        LlrpC1G2SpectralMaskIndicator.Di => V101Enumerations.C1G2SpectralMaskIndicator.DI,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported C1G2 spectral mask indicator."),
    };

    private V101Messages.GET_READER_CONFIG_RESPONSE GetReaderConfig(V101Messages.GET_READER_CONFIG request)
    {
        IReadOnlyList<V101Parameters.AntennaProperties> properties = Enumerable
            .Range(1, _state.Device.Capabilities.MaxNumberOfAntennas)
            .Select(id => new V101Parameters.AntennaProperties(
                _state.IsAntennaConnected(checked((ushort)id)),
                checked((ushort)id),
                0))
            .Where(item => request.AntennaID == 0 || item.AntennaID == request.AntennaID)
            .ToArray();
        IReadOnlyList<V101Parameters.GPIPortCurrentState> gpis = _state.GetGpiStates(request.GPIPortNum);
        IReadOnlyList<V101Parameters.AntennaConfiguration> antennaConfigurations = _state.GetAntennaConfigurations()
            .Where(item => request.AntennaID == 0 || item.AntennaID == request.AntennaID)
            .ToArray();
        IReadOnlyList<V101Parameters.GPOWriteData> gpo = _state.GetGpoWriteData()
            .Where(item => request.GPOPortNum == 0 || item.GPOPortNumber == request.GPOPortNum)
            .ToArray();
        bool all = request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.All;

        return new V101Messages.GET_READER_CONFIG_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty),
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.Identification
                ? new V101Parameters.Identification(
                V101Enumerations.IdentificationType.EPC,
                ReaderIdBytes(_state.Device.Identity.ReaderId))
                : null,
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.AntennaProperties ? properties : [],
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.AntennaConfiguration ? antennaConfigurations : [],
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.ReaderEventNotificationSpec ? _state.GetReaderEventNotificationSpec() : null,
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.ROReportSpec ? _state.GetRoReportSpec() : null,
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.AccessReportSpec ? _state.GetAccessReportSpec() : null,
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.LLRPConfigurationStateValue
                ? new V101Parameters.LLRPConfigurationStateValue(_state.GetConfigurationStateValue())
                : null,
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.KeepaliveSpec ? _state.GetKeepaliveSpec() : null,
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.GPIPortCurrentState ? gpis : [],
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.GPOWriteData ? gpo : [],
            all || request.RequestedData == V101Enumerations.GetReaderConfigRequestedData.EventsAndReports ? _state.GetEventsAndReports() : null,
            _state.GetReaderConfigurationCustomItems());
    }

    private V101Messages.SET_READER_CONFIG_RESPONSE SetReaderConfig(V101Messages.SET_READER_CONFIG request)
    {
        LlrpDeviceOperationResult result = _state.SetConfiguration(
            request.ResetToFactoryDefault,
            request.AntennaConfigurationItems,
            request.ReaderEventNotificationSpec,
            request.ROReportSpec,
            request.AccessReportSpec,
            request.KeepaliveSpec,
            request.GPOWriteDataItems,
            request.EventsAndReports,
            request.CustomItems);
        if (!result.Succeeded)
        {
            return new V101Messages.SET_READER_CONFIG_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, result.Error?.Message ?? "Device configuration failed."));
        }

        if (request.ResetToFactoryDefault)
        {
            _state.ClearRuntimeReports();
        }

        return new V101Messages.SET_READER_CONFIG_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private V101Messages.ADD_ROSPEC_RESPONSE AddRoSpec(V101Messages.ADD_ROSPEC request)
    {
        if (_state.Options.UseStrictStandardInventoryProfile &&
            ValidateStrictInventory(request.ROSpec) is string validationError)
        {
            return new V101Messages.ADD_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, validationError));
        }

        if (request.ROSpec.ROSpecID == 0)
        {
            return new V101Messages.ADD_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "ROSpecID must be non-zero."));
        }

        if (request.ROSpec.CurrentState != V101Enumerations.ROSpecState.Disabled ||
            !_state.TryAddRoSpec(request.ROSpec))
        {
            return new V101Messages.ADD_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "ROSpec already exists or is not disabled."));
        }

        return new V101Messages.ADD_ROSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private static string? ValidateStrictInventory(V101Parameters.ROSpec roSpec)
    {
        V101Parameters.AISpec[] aiSpecs = roSpec.SpecParameterItems.OfType<V101Parameters.AISpec>().ToArray();
        if (aiSpecs.Length == 0)
        {
            return "Strict inventory profile requires an AISpec.";
        }

        foreach (V101Parameters.AISpec aiSpec in aiSpecs)
        {
            if (aiSpec.AntennaIDs.Count == 0 || aiSpec.AntennaIDs.Contains((ushort)0))
            {
                return "Strict inventory profile requires explicit, non-zero AISpec antenna IDs.";
            }

            foreach (V101Parameters.AntennaConfiguration antenna in aiSpec.InventoryParameterSpecItems
                .SelectMany(static inventory => inventory.AntennaConfigurationItems))
            {
                if (antenna.AntennaID == 0)
                {
                    return "Strict inventory profile requires explicit, non-zero antenna configuration IDs.";
                }

                foreach (V101Choices.IAirProtocolInventoryCommandSettings command in antenna.AirProtocolInventoryCommandSettingsItems)
                {
                    if (command is not V101Parameters.C1G2InventoryCommand c1g2 || c1g2.C1G2RFControl is not { } rfControl)
                    {
                        continue;
                    }

                    bool validTari = rfControl.ModeIndex == 20
                        && rfControl.Tari >= 12_500
                        && rfControl.Tari <= 23_000
                        && (rfControl.Tari - 12_500) % 2_100 == 0;
                    if (!validTari)
                    {
                        return $"C1G2RFControl Tari {rfControl.Tari} is invalid for mode {rfControl.ModeIndex}.";
                    }
                }
            }
        }

        return null;
    }

    private V101Messages.GET_ROSPECS_RESPONSE GetRoSpecs(V101Messages.GET_ROSPECS request) =>
        new(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty),
            _state.GetRoSpecs());

    private V101Messages.DELETE_ROSPEC_RESPONSE DeleteRoSpec(V101Messages.DELETE_ROSPEC request)
    {
        if (request.ROSpecID != 0 &&
            _state.TryGetRoSpec(request.ROSpecID, out V101Parameters.ROSpec? existing) &&
            existing?.CurrentState == V101Enumerations.ROSpecState.Active)
        {
            return new V101Messages.DELETE_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "An active ROSpec must be stopped before deletion."));
        }

        if (!_state.TryDeleteRoSpec(request.ROSpecID, out bool deletedAll))
        {
            return new V101Messages.DELETE_ROSPEC_RESPONSE(
                request.MessageId,
                MissingRoSpec(request.ROSpecID));
        }

        if (deletedAll)
        {
            _state.ClearAllAccumulatedRoSpecReports();
        }
        else
        {
            _state.ClearAccumulatedRoSpecReport(request.ROSpecID);
        }
        return new V101Messages.DELETE_ROSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private V101Messages.ENABLE_ROSPEC_RESPONSE EnableRoSpec(V101Messages.ENABLE_ROSPEC request)
    {
        if (request.ROSpecID == 0)
        {
            foreach (uint roSpecId in _state.GetRoSpecIds())
            {
                if (_state.TryGetRoSpec(roSpecId, out V101Parameters.ROSpec? allRoSpec) &&
                    allRoSpec?.CurrentState == V101Enumerations.ROSpecState.Disabled)
                {
                    _state.TryUpdateRoSpec(roSpecId, static current => current with { CurrentState = V101Enumerations.ROSpecState.Inactive });
                    _state.MarkRoSpecEnabled(roSpecId);
                }
            }

            return new V101Messages.ENABLE_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_Success, string.Empty));
        }

        if (!_state.TryGetRoSpec(request.ROSpecID, out V101Parameters.ROSpec? roSpec) || roSpec is null)
        {
            return new V101Messages.ENABLE_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
        }

        if (roSpec.CurrentState != V101Enumerations.ROSpecState.Disabled)
        {
            if (_state.Options.RelaxedRoSpecStateChecks)
            {
                return new V101Messages.ENABLE_ROSPEC_RESPONSE(
                    request.MessageId,
                    Status(V101Enumerations.StatusCode.M_Success, string.Empty));
            }

            return new V101Messages.ENABLE_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "ROSpec is already enabled."));
        }

        _state.TryUpdateRoSpec(
            request.ROSpecID,
            static current => current with { CurrentState = V101Enumerations.ROSpecState.Inactive });
        _state.MarkRoSpecEnabled(request.ROSpecID);
        return new V101Messages.ENABLE_ROSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private V101Messages.DISABLE_ROSPEC_RESPONSE DisableRoSpec(V101Messages.DISABLE_ROSPEC request)
    {
        if (request.ROSpecID == 0)
        {
            if (!_state.Options.RelaxedRoSpecStateChecks &&
                _state.GetRoSpecs().Any(static item => item.CurrentState == V101Enumerations.ROSpecState.Active))
            {
                return new V101Messages.DISABLE_ROSPEC_RESPONSE(
                    request.MessageId,
                    Status(V101Enumerations.StatusCode.M_ParameterError, "All active ROSpecs must be stopped before they can be disabled."));
            }

            foreach (uint roSpecId in _state.GetRoSpecIds())
            {
                if (_state.TryGetRoSpec(roSpecId, out V101Parameters.ROSpec? allRoSpec) &&
                    allRoSpec?.CurrentState == V101Enumerations.ROSpecState.Active)
                {
                    _state.TryUpdateRoSpec(
                        roSpecId,
                        static current => current with { CurrentState = V101Enumerations.ROSpecState.Inactive });
                    _state.MarkRoSpecStopped(roSpecId);
                }

                _state.TryUpdateRoSpec(roSpecId, static current => current with { CurrentState = V101Enumerations.ROSpecState.Disabled });
                _state.MarkRoSpecStopped(roSpecId);
            }

            return new V101Messages.DISABLE_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_Success, string.Empty));
        }

        if (!_state.TryGetRoSpec(request.ROSpecID, out V101Parameters.ROSpec? roSpec) || roSpec is null)
        {
            return new V101Messages.DISABLE_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
        }

        if (roSpec.CurrentState == V101Enumerations.ROSpecState.Active &&
            _state.Options.RelaxedRoSpecStateChecks)
        {
            _state.TryUpdateRoSpec(
                request.ROSpecID,
                static current => current with { CurrentState = V101Enumerations.ROSpecState.Inactive });
            _state.MarkRoSpecStopped(request.ROSpecID);
            roSpec = roSpec with { CurrentState = V101Enumerations.ROSpecState.Inactive };
        }

        if (roSpec.CurrentState == V101Enumerations.ROSpecState.Disabled &&
            _state.Options.RelaxedRoSpecStateChecks)
        {
            return new V101Messages.DISABLE_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_Success, string.Empty));
        }

        if (roSpec.CurrentState != V101Enumerations.ROSpecState.Inactive)
        {
            return new V101Messages.DISABLE_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "Only an inactive ROSpec can be disabled."));
        }

        _state.TryUpdateRoSpec(
            request.ROSpecID,
            static current => current with { CurrentState = V101Enumerations.ROSpecState.Disabled });
        _state.MarkRoSpecStopped(request.ROSpecID);
        return new V101Messages.DISABLE_ROSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private LlrpDeviceDispatchResult StartRoSpec(
        LlrpDeviceRequestContext context,
        V101Messages.START_ROSPEC request)
    {
        if (request.ROSpecID == 0)
        {
            foreach (uint roSpecId in _state.GetRoSpecIds())
            {
                if (_state.TryGetRoSpec(roSpecId, out V101Parameters.ROSpec? allRoSpec) &&
                    allRoSpec?.CurrentState == V101Enumerations.ROSpecState.Inactive)
                {
                    _state.ClearAccumulatedRoSpecReport(roSpecId);
                    _state.TryUpdateRoSpec(roSpecId, static current => current with { CurrentState = V101Enumerations.ROSpecState.Active });
                    _state.MarkRoSpecStarted(roSpecId);
                    context.Server.PublishDeviceEvent(new LlrpDeviceEvent
                    {
                        Name = "rospec.started",
                        RoSpecId = roSpecId,
                    });
                }
            }

            return Response(new V101Messages.START_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_Success, string.Empty)));
        }

        if (!_state.TryGetRoSpec(request.ROSpecID, out V101Parameters.ROSpec? roSpec) || roSpec is null)
        {
            return Response(new V101Messages.START_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID)));
        }

        if (roSpec.CurrentState == V101Enumerations.ROSpecState.Active &&
            _state.Options.RelaxedRoSpecStateChecks)
        {
            return Response(new V101Messages.START_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_Success, string.Empty)));
        }

        if (roSpec.CurrentState != V101Enumerations.ROSpecState.Inactive)
        {
            return Response(new V101Messages.START_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "ROSpec must be inactive before it can be started.")));
        }

        _state.TryUpdateRoSpec(
            request.ROSpecID,
            static current => current with { CurrentState = V101Enumerations.ROSpecState.Active });
        _state.ClearAccumulatedRoSpecReport(request.ROSpecID);
        _state.MarkRoSpecStarted(request.ROSpecID);
        context.Server.PublishDeviceEvent(new LlrpDeviceEvent
        {
            Name = "rospec.started",
            RoSpecId = request.ROSpecID,
        });
        return Response(new V101Messages.START_ROSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty)));
    }

    private LlrpDeviceDispatchResult StopRoSpec(
        LlrpDeviceRequestContext context,
        V101Messages.STOP_ROSPEC request)
    {
        if (request.ROSpecID == 0)
        {
            var reports = new List<ILlrpMessage>();
            foreach (uint roSpecId in _state.GetRoSpecIds())
            {
                if (_state.TryGetRoSpec(roSpecId, out V101Parameters.ROSpec? allRoSpec) &&
                    allRoSpec?.CurrentState == V101Enumerations.ROSpecState.Active)
                {
                    _state.TryUpdateRoSpec(roSpecId, static current => current with { CurrentState = V101Enumerations.ROSpecState.Inactive });
                    _state.MarkRoSpecStopped(roSpecId);
                    context.Server.PublishDeviceEvent(new LlrpDeviceEvent
                    {
                        Name = "rospec.stopped",
                        RoSpecId = roSpecId,
                    });
                    if (_state.TakeAccumulatedRoSpecReport(roSpecId, NextAsyncMessageId()) is V101Messages.RO_ACCESS_REPORT report)
                    {
                        reports.Add(report);
                    }
                }
            }

            return new LlrpDeviceDispatchResult(
                new V101Messages.STOP_ROSPEC_RESPONSE(
                    request.MessageId,
                    Status(V101Enumerations.StatusCode.M_Success, string.Empty)),
                reports);
        }

        if (!_state.TryGetRoSpec(request.ROSpecID, out V101Parameters.ROSpec? roSpec) || roSpec is null)
        {
            return Response(new V101Messages.STOP_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID)));
        }

        if (roSpec.CurrentState != V101Enumerations.ROSpecState.Active)
        {
            if (roSpec.CurrentState == V101Enumerations.ROSpecState.Inactive ||
                (roSpec.CurrentState == V101Enumerations.ROSpecState.Disabled &&
                 _state.Options.RelaxedRoSpecStateChecks))
            {
                return Response(new V101Messages.STOP_ROSPEC_RESPONSE(
                    request.MessageId,
                    Status(V101Enumerations.StatusCode.M_Success, string.Empty)));
            }

            return Response(new V101Messages.STOP_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "Only an active ROSpec can be stopped.")));
        }

        _state.TryUpdateRoSpec(
            request.ROSpecID,
            static current => current with { CurrentState = V101Enumerations.ROSpecState.Inactive });
        _state.MarkRoSpecStopped(request.ROSpecID);
        context.Server.PublishDeviceEvent(new LlrpDeviceEvent
        {
            Name = "rospec.stopped",
            RoSpecId = request.ROSpecID,
        });
        var additional = new List<ILlrpMessage>();
        if (_state.TakeAccumulatedRoSpecReport(request.ROSpecID, NextAsyncMessageId()) is V101Messages.RO_ACCESS_REPORT finalReport)
        {
            additional.Add(finalReport);
        }

        return new LlrpDeviceDispatchResult(
            new V101Messages.STOP_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_Success, string.Empty)),
            additional);
    }

    private V101Messages.ADD_ACCESSSPEC_RESPONSE AddAccessSpec(V101Messages.ADD_ACCESSSPEC request)
    {
        if (request.AccessSpec.CurrentState != V101Enumerations.AccessSpecState.Disabled ||
            !_state.TryAddAccessSpec(request.AccessSpec))
        {
            return new V101Messages.ADD_ACCESSSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "AccessSpec already exists or its ROSpec is missing."));
        }

        return new V101Messages.ADD_ACCESSSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private V101Messages.GET_ACCESSSPECS_RESPONSE GetAccessSpecs(V101Messages.GET_ACCESSSPECS request) =>
        new(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty),
            _state.GetAccessSpecs());

    private V101Messages.DELETE_ACCESSSPEC_RESPONSE DeleteAccessSpec(V101Messages.DELETE_ACCESSSPEC request)
    {
        if (request.AccessSpecID != 0 &&
            _state.TryGetAccessSpec(request.AccessSpecID, out V101Parameters.AccessSpec? accessSpec) &&
            accessSpec?.CurrentState == V101Enumerations.AccessSpecState.Active)
        {
            return new V101Messages.DELETE_ACCESSSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "An active AccessSpec must be disabled before deletion."));
        }

        if (!_state.TryDeleteAccessSpec(request.AccessSpecID))
        {
            return new V101Messages.DELETE_ACCESSSPEC_RESPONSE(
                request.MessageId,
                MissingAccessSpec(request.AccessSpecID));
        }

        return new V101Messages.DELETE_ACCESSSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private LlrpDeviceDispatchResult EnableAccessSpec(V101Messages.ENABLE_ACCESSSPEC request)
    {
        if (request.AccessSpecID == 0)
        {
            var reports = new List<ILlrpMessage>();
            foreach (uint accessSpecId in _state.GetAccessSpecIds())
            {
                if (!_state.TryGetAccessSpec(accessSpecId, out V101Parameters.AccessSpec? allAccessSpec) ||
                    allAccessSpec is null ||
                    allAccessSpec.CurrentState != V101Enumerations.AccessSpecState.Disabled ||
                    !_state.TryGetRoSpec(allAccessSpec.ROSpecID, out V101Parameters.ROSpec? allRoSpec) ||
                    allRoSpec?.CurrentState == V101Enumerations.ROSpecState.Disabled)
                {
                    continue;
                }

                _state.TryUpdateAccessSpec(
                    accessSpecId,
                    static current => current with { CurrentState = V101Enumerations.AccessSpecState.Active });
                reports.Add(BuildAccessReport(allAccessSpec with
                {
                    CurrentState = V101Enumerations.AccessSpecState.Active,
                }));
            }

            return new LlrpDeviceDispatchResult(
                new V101Messages.ENABLE_ACCESSSPEC_RESPONSE(
                    request.MessageId,
                    Status(V101Enumerations.StatusCode.M_Success, string.Empty)),
                reports);
        }

        if (!_state.TryGetAccessSpec(request.AccessSpecID, out V101Parameters.AccessSpec? accessSpec) || accessSpec is null)
        {
            return Response(new V101Messages.ENABLE_ACCESSSPEC_RESPONSE(request.MessageId, MissingAccessSpec(request.AccessSpecID)));
        }

        if (accessSpec.CurrentState != V101Enumerations.AccessSpecState.Disabled)
        {
            return Response(new V101Messages.ENABLE_ACCESSSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "AccessSpec is already enabled.")));
        }

        if (!_state.TryGetRoSpec(accessSpec.ROSpecID, out V101Parameters.ROSpec? roSpec) ||
            roSpec?.CurrentState == V101Enumerations.ROSpecState.Disabled)
        {
            return Response(new V101Messages.ENABLE_ACCESSSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "The associated ROSpec is not active.")));
        }

        _state.TryUpdateAccessSpec(
            request.AccessSpecID,
            static current => current with { CurrentState = V101Enumerations.AccessSpecState.Active });
        var response = new V101Messages.ENABLE_ACCESSSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
        return LlrpDeviceDispatchResult.WithMessages(response, BuildAccessReport(accessSpec with
        {
            CurrentState = V101Enumerations.AccessSpecState.Active,
        }));
    }

    private V101Messages.DISABLE_ACCESSSPEC_RESPONSE DisableAccessSpec(V101Messages.DISABLE_ACCESSSPEC request)
    {
        if (request.AccessSpecID == 0)
        {
            foreach (uint accessSpecId in _state.GetAccessSpecIds())
            {
                _state.TryUpdateAccessSpec(
                    accessSpecId,
                    static current => current with { CurrentState = V101Enumerations.AccessSpecState.Disabled });
            }

            return new V101Messages.DISABLE_ACCESSSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_Success, string.Empty));
        }

        if (!_state.TryGetAccessSpec(request.AccessSpecID, out V101Parameters.AccessSpec? accessSpec) || accessSpec is null)
        {
            return new V101Messages.DISABLE_ACCESSSPEC_RESPONSE(request.MessageId, MissingAccessSpec(request.AccessSpecID));
        }

        if (accessSpec.CurrentState != V101Enumerations.AccessSpecState.Active)
        {
            return new V101Messages.DISABLE_ACCESSSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "Only an active AccessSpec can be disabled."));
        }

        _state.TryUpdateAccessSpec(
            request.AccessSpecID,
            static current => current with { CurrentState = V101Enumerations.AccessSpecState.Disabled });
        return new V101Messages.DISABLE_ACCESSSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private V101Messages.RO_ACCESS_REPORT BuildAccessReport(V101Parameters.AccessSpec accessSpec)
    {
        LlrpDeviceTag tag = _state.Inventory.Observe(
                new LlrpInventoryRound(
                    accessSpec.ROSpecID,
                    0,
                    _state.GetInventoryAntennaIds(accessSpec.ROSpecID)))
            .FirstOrDefault() ?? new LlrpDeviceTag
        {
            ElectronicProductCode = Convert.FromHexString("E28011710000020D056E9BEE"),
        };
        bool selected = MatchesTag(accessSpec.AccessCommand.AirProtocolTagSpec, tag);
        IReadOnlyList<ILlrpParameter> results = BuildOperationResults(accessSpec, selected, tag);
        return new V101Messages.RO_ACCESS_REPORT(
            NextAsyncMessageId(),
            [BuildTagReport(accessSpec.ROSpecID, accessSpec.AccessSpecID, tag, results)],
            [],
            []);
    }

    private IReadOnlyList<ILlrpParameter> BuildOperationResults(
        V101Parameters.AccessSpec accessSpec,
        bool selected,
        LlrpDeviceTag tag)
    {
        IReadOnlyList<ILlrpParameter> operations = accessSpec.AccessCommand.AccessCommandOpSpecItems;
        if (!selected)
        {
            return operations.Select(CreateNoResponseResult).ToArray();
        }

        if (accessSpec.AccessCommand.AirProtocolTagSpec is not V101Parameters.C1G2TagSpec tagSpec ||
            tagSpec.C1G2TargetTagItems.Count == 0)
        {
            return operations.Select(CreateNoResponseResult).ToArray();
        }

        V101Parameters.C1G2TargetTag target = tagSpec.C1G2TargetTagItems[0];
        var request = new LlrpTagAccessRequest
        {
            AccessSpecId = accessSpec.AccessSpecID,
            RoSpecId = accessSpec.ROSpecID,
            Selector = BuildSelector(target),
            Operations = operations.Select(BuildAccessOperation).ToArray(),
        };
        LlrpTagAccessResult? result = _state.Device.ExecuteTagAccessAsync(request)
            .AsTask().GetAwaiter().GetResult().FirstOrDefault();
        return operations.Select(operation => MapOperationResult(
            operation,
            result?.Operations.FirstOrDefault(item => item.OperationId == GetOperationId(operation)))).ToArray();
    }

    private ILlrpParameter CreateNoResponseResult(ILlrpParameter operation) => operation switch
    {
        V101Parameters.C1G2Read read => new V101Parameters.C1G2ReadOpSpecResult(
            V101Enumerations.C1G2ReadResultType.No_Response_From_Tag, read.OpSpecID, []),
        V101Parameters.C1G2Write write => new V101Parameters.C1G2WriteOpSpecResult(
            V101Enumerations.C1G2WriteResultType.No_Response_From_Tag, write.OpSpecID, 0),
        V101Parameters.C1G2BlockWrite blockWrite => new V101Parameters.C1G2BlockWriteOpSpecResult(
            V101Enumerations.C1G2BlockWriteResultType.No_Response_From_Tag, blockWrite.OpSpecID, 0),
        V101Parameters.C1G2Lock lockOperation => new V101Parameters.C1G2LockOpSpecResult(
            V101Enumerations.C1G2LockResultType.No_Response_From_Tag, lockOperation.OpSpecID),
        V101Parameters.C1G2Kill kill => new V101Parameters.C1G2KillOpSpecResult(
            V101Enumerations.C1G2KillResultType.No_Response_From_Tag, kill.OpSpecID),
        V101Parameters.C1G2BlockErase erase => new V101Parameters.C1G2BlockEraseOpSpecResult(
            V101Enumerations.C1G2BlockEraseResultType.No_Response_From_Tag, erase.OpSpecID),
        _ => throw new NotSupportedException($"The device server does not implement access operation {operation.GetType().Name}."),
    };

    private LlrpTagSelector BuildSelector(V101Parameters.C1G2TargetTag target) => new()
    {
        MemoryBank = (LlrpTagMemoryBank)target.MB,
        BitPointer = target.Pointer,
        BitLength = checked((ushort)target.TagMask.Count),
        Mask = PackBits(target.TagMask),
        Data = PackBits(target.TagData),
        Match = target.Match,
    };

    private static LlrpTagAccessOperation BuildAccessOperation(ILlrpParameter operation) => operation switch
    {
        V101Parameters.C1G2Read read => new LlrpTagAccessOperation
        {
            OperationId = read.OpSpecID,
            Kind = LlrpTagAccessOperationKind.Read,
            AccessPassword = read.AccessPassword,
            MemoryBank = (LlrpTagMemoryBank)read.MB,
            WordPointer = read.WordPointer,
            WordCount = read.WordCount,
        },
        V101Parameters.C1G2Write write => new LlrpTagAccessOperation
        {
            OperationId = write.OpSpecID,
            Kind = LlrpTagAccessOperationKind.Write,
            AccessPassword = write.AccessPassword,
            MemoryBank = (LlrpTagMemoryBank)write.MB,
            WordPointer = write.WordPointer,
            WordCount = checked((ushort)write.WriteData.Count),
            WriteData = write.WriteData,
        },
        V101Parameters.C1G2BlockWrite blockWrite => new LlrpTagAccessOperation
        {
            OperationId = blockWrite.OpSpecID,
            Kind = LlrpTagAccessOperationKind.BlockWrite,
            AccessPassword = blockWrite.AccessPassword,
            MemoryBank = (LlrpTagMemoryBank)blockWrite.MB,
            WordPointer = blockWrite.WordPointer,
            WordCount = checked((ushort)blockWrite.WriteData.Count),
            WriteData = blockWrite.WriteData,
        },
        V101Parameters.C1G2Lock lockOperation => new LlrpTagAccessOperation
        {
            OperationId = lockOperation.OpSpecID,
            Kind = LlrpTagAccessOperationKind.Lock,
            AccessPassword = lockOperation.AccessPassword,
            LockRequests = lockOperation.C1G2LockPayloadItems.Select(static payload => new LlrpTagLockRequest(
                payload.Privilege switch
                {
                    V101Enumerations.C1G2LockPrivilege.Read_Write => LlrpTagLockPrivilege.ReadWrite,
                    V101Enumerations.C1G2LockPrivilege.Perma_Unlock => LlrpTagLockPrivilege.PermaUnlock,
                    V101Enumerations.C1G2LockPrivilege.Unlock => LlrpTagLockPrivilege.Unlock,
                    _ => LlrpTagLockPrivilege.PermaLock,
                },
                payload.DataField switch
                {
                    V101Enumerations.C1G2LockDataField.EPC_Memory => LlrpTagMemoryBank.ElectronicProductCode,
                    V101Enumerations.C1G2LockDataField.TID_Memory => LlrpTagMemoryBank.Tid,
                    V101Enumerations.C1G2LockDataField.User_Memory => LlrpTagMemoryBank.User,
                    _ => LlrpTagMemoryBank.Reserved,
                })).ToArray(),
        },
        V101Parameters.C1G2Kill kill => new LlrpTagAccessOperation
        {
            OperationId = kill.OpSpecID,
            Kind = LlrpTagAccessOperationKind.Kill,
            KillPassword = kill.KillPassword,
        },
        V101Parameters.C1G2BlockErase erase => new LlrpTagAccessOperation
        {
            OperationId = erase.OpSpecID,
            Kind = LlrpTagAccessOperationKind.BlockErase,
            AccessPassword = erase.AccessPassword,
            MemoryBank = (LlrpTagMemoryBank)erase.MB,
            WordPointer = erase.WordPointer,
            WordCount = erase.WordCount,
        },
        _ => throw new NotSupportedException($"The device server does not implement access operation {operation.GetType().Name}."),
    };

    private ILlrpParameter MapOperationResult(
        ILlrpParameter operation,
        LlrpTagAccessOperationResult? result) => operation switch
    {
        V101Parameters.C1G2Read read => new V101Parameters.C1G2ReadOpSpecResult(
            MapReadResult(result?.Result), read.OpSpecID, result?.ReadData ?? []),
        V101Parameters.C1G2Write write => new V101Parameters.C1G2WriteOpSpecResult(
            MapWriteResult(result?.Result), write.OpSpecID, result?.WordsWritten ?? 0),
        V101Parameters.C1G2BlockWrite blockWrite => new V101Parameters.C1G2BlockWriteOpSpecResult(
            MapBlockWriteResult(result?.Result), blockWrite.OpSpecID, result?.WordsWritten ?? 0),
        V101Parameters.C1G2Lock lockOperation => new V101Parameters.C1G2LockOpSpecResult(
            MapLockResult(result?.Result), lockOperation.OpSpecID),
        V101Parameters.C1G2Kill kill => new V101Parameters.C1G2KillOpSpecResult(
            MapKillResult(result?.Result), kill.OpSpecID),
        V101Parameters.C1G2BlockErase erase => new V101Parameters.C1G2BlockEraseOpSpecResult(
            MapBlockEraseResult(result?.Result), erase.OpSpecID),
        _ => throw new NotSupportedException($"The device server does not implement access operation {operation.GetType().Name}."),
    };

    private static ushort GetOperationId(ILlrpParameter operation) => operation switch
    {
        V101Parameters.C1G2Read read => read.OpSpecID,
        V101Parameters.C1G2Write write => write.OpSpecID,
        V101Parameters.C1G2BlockWrite blockWrite => blockWrite.OpSpecID,
        V101Parameters.C1G2Lock lockOperation => lockOperation.OpSpecID,
        V101Parameters.C1G2Kill kill => kill.OpSpecID,
        V101Parameters.C1G2BlockErase erase => erase.OpSpecID,
        _ => throw new NotSupportedException(),
    };

    private static V101Enumerations.C1G2ReadResultType MapReadResult(LlrpTagAccessResultCode? result) => result switch
    {
        LlrpTagAccessResultCode.Success => V101Enumerations.C1G2ReadResultType.Success,
        LlrpTagAccessResultCode.NoResponseFromTag => V101Enumerations.C1G2ReadResultType.No_Response_From_Tag,
        _ => V101Enumerations.C1G2ReadResultType.Nonspecific_Tag_Error,
    };

    private static V101Enumerations.C1G2WriteResultType MapWriteResult(LlrpTagAccessResultCode? result) => result switch
    {
        LlrpTagAccessResultCode.Success => V101Enumerations.C1G2WriteResultType.Success,
        LlrpTagAccessResultCode.NoResponseFromTag => V101Enumerations.C1G2WriteResultType.No_Response_From_Tag,
        LlrpTagAccessResultCode.Locked => V101Enumerations.C1G2WriteResultType.Tag_Memory_Locked_Error,
        LlrpTagAccessResultCode.MemoryOverrun => V101Enumerations.C1G2WriteResultType.Tag_Memory_Overrun_Error,
        _ => V101Enumerations.C1G2WriteResultType.Nonspecific_Tag_Error,
    };

    private static V101Enumerations.C1G2BlockWriteResultType MapBlockWriteResult(LlrpTagAccessResultCode? result) => result switch
    {
        LlrpTagAccessResultCode.Success => V101Enumerations.C1G2BlockWriteResultType.Success,
        LlrpTagAccessResultCode.NoResponseFromTag => V101Enumerations.C1G2BlockWriteResultType.No_Response_From_Tag,
        LlrpTagAccessResultCode.Locked => V101Enumerations.C1G2BlockWriteResultType.Tag_Memory_Locked_Error,
        LlrpTagAccessResultCode.MemoryOverrun => V101Enumerations.C1G2BlockWriteResultType.Tag_Memory_Overrun_Error,
        _ => V101Enumerations.C1G2BlockWriteResultType.Nonspecific_Tag_Error,
    };

    private static V101Enumerations.C1G2LockResultType MapLockResult(LlrpTagAccessResultCode? result) => result switch
    {
        LlrpTagAccessResultCode.Success => V101Enumerations.C1G2LockResultType.Success,
        LlrpTagAccessResultCode.NoResponseFromTag => V101Enumerations.C1G2LockResultType.No_Response_From_Tag,
        _ => V101Enumerations.C1G2LockResultType.Nonspecific_Tag_Error,
    };

    private static V101Enumerations.C1G2KillResultType MapKillResult(LlrpTagAccessResultCode? result) => result switch
    {
        LlrpTagAccessResultCode.Success => V101Enumerations.C1G2KillResultType.Success,
        LlrpTagAccessResultCode.NoResponseFromTag => V101Enumerations.C1G2KillResultType.No_Response_From_Tag,
        LlrpTagAccessResultCode.IncorrectPassword => V101Enumerations.C1G2KillResultType.Zero_Kill_Password_Error,
        _ => V101Enumerations.C1G2KillResultType.Nonspecific_Tag_Error,
    };

    private static V101Enumerations.C1G2BlockEraseResultType MapBlockEraseResult(LlrpTagAccessResultCode? result) => result switch
    {
        LlrpTagAccessResultCode.Success => V101Enumerations.C1G2BlockEraseResultType.Success,
        LlrpTagAccessResultCode.NoResponseFromTag => V101Enumerations.C1G2BlockEraseResultType.No_Response_From_Tag,
        LlrpTagAccessResultCode.Locked => V101Enumerations.C1G2BlockEraseResultType.Tag_Memory_Locked_Error,
        LlrpTagAccessResultCode.MemoryOverrun => V101Enumerations.C1G2BlockEraseResultType.Tag_Memory_Overrun_Error,
        _ => V101Enumerations.C1G2BlockEraseResultType.Nonspecific_Tag_Error,
    };

    private static byte[] PackBits(IReadOnlyList<bool> bits)
    {
        var packed = new byte[(bits.Count + 7) / 8];
        for (int index = 0; index < bits.Count; index++)
        {
            if (bits[index])
            {
                packed[index / 8] |= (byte)(1 << (7 - index % 8));
            }
        }

        return packed;
    }

    private bool MatchesTag(V101Choices.IAirProtocolTagSpec tagSpec, LlrpDeviceTag tag)
    {
        if (tagSpec is not V101Parameters.C1G2TagSpec c1g2)
        {
            return false;
        }

        foreach (V101Parameters.C1G2TargetTag target in c1g2.C1G2TargetTagItems)
        {
            bool match = MatchesTarget(target, tag);
            if (match != target.Match)
            {
                return false;
            }
        }

        return true;
    }

    private bool MatchesTarget(V101Parameters.C1G2TargetTag target, LlrpDeviceTag tag)
    {
        if (!_state.Inventory.TryGetMemoryBytes(tag.ElectronicProductCode.Span, target.MB, out ReadOnlyMemory<byte> bytes))
        {
            return false;
        }

        bool[] memoryBits = ToBits(bytes.Span);
        if (target.Pointer + target.TagMask.Count > memoryBits.Length || target.TagMask.Count != target.TagData.Count)
        {
            return false;
        }

        for (int index = 0; index < target.TagMask.Count; index++)
        {
            if (target.TagMask[index] && memoryBits[target.Pointer + index] != target.TagData[index])
            {
                return false;
            }
        }

        return true;
    }

    private V101Parameters.TagReportData BuildTagReport(
        uint roSpecId,
        uint? accessSpecId,
        LlrpDeviceTag tag,
        IReadOnlyList<ILlrpParameter> results,
        V101Parameters.TagReportContentSelector? selector = null,
        ushort inventoryParameterSpecId = 1)
    {
        selector ??= FullTagReportSelector();
        C1G2MemorySelection memorySelection = GetMemorySelection(selector);
        DateTimeOffset firstSeen = tag.FirstSeenUtc;
        DateTimeOffset lastSeen = tag.LastSeenUtc ?? firstSeen;
        IReadOnlyList<V101Choices.IAirProtocolTagData> tagData = [];
        if (memorySelection.IncludeCrc || memorySelection.IncludePcBits)
        {
            tagData = [
                .. (memorySelection.IncludeCrc
                    ? [new V101Parameters.C1G2_CRC(tag.Crc ?? CalculateCrc(tag.ElectronicProductCode.Span))]
                    : Array.Empty<V101Choices.IAirProtocolTagData>()),
                .. (memorySelection.IncludePcBits
                    ? [new V101Parameters.C1G2_PC(tag.PcBits ?? CalculatePcBits(tag.ElectronicProductCode.Length))]
                    : Array.Empty<V101Choices.IAirProtocolTagData>()),
            ];
        }

        return new V101Parameters.TagReportData(
            BuildEpcParameter(tag),
            selector.EnableROSpecID ? new V101Parameters.ROSpecID(roSpecId) : null,
            selector.EnableSpecIndex ? new V101Parameters.SpecIndex(1) : null,
            selector.EnableInventoryParameterSpecID ? new V101Parameters.InventoryParameterSpecID(inventoryParameterSpecId) : null,
            selector.EnableAntennaID ? new V101Parameters.AntennaID(tag.AntennaId) : null,
            selector.EnablePeakRSSI ? new V101Parameters.PeakRSSI(checked((sbyte)tag.PeakRssi)) : null,
            selector.EnableChannelIndex ? new V101Parameters.ChannelIndex(tag.ChannelIndex) : null,
            selector.EnableFirstSeenTimestamp ? new V101Parameters.FirstSeenTimestampUTC(ToMicroseconds(firstSeen)) : null,
            null,
            selector.EnableLastSeenTimestamp ? new V101Parameters.LastSeenTimestampUTC(ToMicroseconds(lastSeen)) : null,
            null,
            selector.EnableTagSeenCount ? new V101Parameters.TagSeenCount(checked((ushort)Math.Min(tag.SeenCount, ushort.MaxValue))) : null,
            tagData,
            accessSpecId is uint id && (selector.EnableAccessSpecID || results.Count > 0)
                ? new V101Parameters.AccessSpecID(id)
                : null,
            results,
            []);
    }

    private static V101Parameters.TagReportContentSelector FullTagReportSelector() =>
        new(
            EnableROSpecID: true,
            EnableSpecIndex: true,
            EnableInventoryParameterSpecID: true,
            EnableAntennaID: true,
            EnableChannelIndex: true,
            EnablePeakRSSI: true,
            EnableFirstSeenTimestamp: true,
            EnableLastSeenTimestamp: true,
            EnableTagSeenCount: true,
            EnableAccessSpecID: true,
            AirProtocolEPCMemorySelectorItems: [new V101Parameters.C1G2EPCMemorySelector(true, true)]);

    private static C1G2MemorySelection GetMemorySelection(V101Parameters.TagReportContentSelector selector) =>
        selector.AirProtocolEPCMemorySelectorItems.OfType<V101Parameters.C1G2EPCMemorySelector>()
            .Select(static item => new C1G2MemorySelection(item.EnableCRC, item.EnablePCBits))
            .FirstOrDefault();

    private static ulong ToMicroseconds(DateTimeOffset timestamp)
    {
        TimeSpan offset = timestamp.ToUniversalTime() - DateTimeOffset.UnixEpoch;
        return checked((ulong)(offset.Ticks / TimeSpan.TicksPerMicrosecond));
    }

    private static ushort CalculatePcBits(int epcByteLength) =>
        checked((ushort)(Math.Clamp(epcByteLength / 2, 0, 31) << 11));

    private static ushort CalculateCrc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
            }
        }

        return crc;
    }

    private readonly record struct C1G2MemorySelection(bool IncludeCrc, bool IncludePcBits);

    private static V101Choices.IEPCParameter BuildEpcParameter(LlrpDeviceTag tag) =>
        tag.ElectronicProductCode.Length == 12
            ? new V101Parameters.EPC_96(tag.ElectronicProductCode)
            : new V101Parameters.EPCData(ToBits(tag.ElectronicProductCode.Span));

    private static byte[] ReaderIdBytes(ulong readerId)
    {
        var bytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, readerId);
        return bytes;
    }

    private uint NextAsyncMessageId() => unchecked((uint)Interlocked.Increment(ref _nextAsyncMessageId));

    private int _nextAsyncMessageId;

    private static bool[] ToBits(ReadOnlySpan<byte> bytes)
    {
        var bits = new bool[bytes.Length * 8];
        for (int index = 0; index < bits.Length; index++)
        {
            bits[index] = (bytes[index / 8] & (1 << (7 - (index % 8)))) != 0;
        }

        return bits;
    }

    private static V101Parameters.LLRPStatus Status(V101Enumerations.StatusCode code, string description) =>
        new(code, description, null, null);

    private static V101Parameters.LLRPStatus MissingRoSpec(uint roSpecId) =>
        Status(V101Enumerations.StatusCode.M_ParameterError, $"ROSpec {roSpecId} does not exist.");

    private static V101Parameters.LLRPStatus MissingAccessSpec(uint accessSpecId) =>
        Status(V101Enumerations.StatusCode.M_ParameterError, $"AccessSpec {accessSpecId} does not exist.");
}
