using System.Buffers.Binary;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using V101Choices = LlrpNet.Protocol.Choices.V1_0_1;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V101Registry = LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpVirtualReader;

internal interface IVirtualReaderVersionProfile : IVirtualReaderMessageHandler
{
    public LlrpProtocolVersion Version { get; }

    public ILlrpMessage CreateError(uint messageId, ushort statusCode, string description);

    public ILlrpMessage CreateKeepalive(uint messageId);

    public ILlrpMessage CreateReaderEventNotification(uint messageId);

    public IReadOnlyList<ILlrpMessage> BuildInventoryReports(uint roSpecId);
}

/// <summary>
/// Handles the standard LLRP 1.0.1 device messages and owns the canonical resource-state transitions.
/// </summary>
internal sealed class VirtualReaderStandard101Handler : IVirtualReaderVersionProfile
{
    private readonly VirtualReaderDeviceState _state;

    public VirtualReaderStandard101Handler(VirtualReaderDeviceState state)
    {
        _state = state;
    }

    public string Name => "standard-llrp-1.0.1";

    public LlrpProtocolVersion Version => LlrpProtocolVersion.Version101;

    public bool CanHandle(LlrpProtocolVersion version, ILlrpMessage message) => version == Version;

    public ValueTask<VirtualReaderDispatchResult> HandleAsync(
        VirtualReaderRequestContext context,
        ILlrpMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VirtualReaderDispatchResult result = message switch
        {
            V101Messages.GET_READER_CAPABILITIES request => Response(Capabilities(request.MessageId)),
            V101Messages.GET_READER_CONFIG request => Response(GetReaderConfig(request)),
            V101Messages.SET_READER_CONFIG request => Response(SetReaderConfig(request)),
            V101Messages.ADD_ROSPEC request => Response(AddRoSpec(request)),
            V101Messages.GET_ROSPECS request => Response(GetRoSpecs(request)),
            V101Messages.DELETE_ROSPEC request => Response(DeleteRoSpec(request)),
            V101Messages.ENABLE_ROSPEC request => Response(EnableRoSpec(request)),
            V101Messages.DISABLE_ROSPEC request => Response(DisableRoSpec(request)),
            V101Messages.START_ROSPEC request => Response(StartRoSpec(request)),
            V101Messages.STOP_ROSPEC request => Response(StopRoSpec(request)),
            V101Messages.ADD_ACCESSSPEC request => Response(AddAccessSpec(request)),
            V101Messages.GET_ACCESSSPECS request => Response(GetAccessSpecs(request)),
            V101Messages.DELETE_ACCESSSPEC request => Response(DeleteAccessSpec(request)),
            V101Messages.ENABLE_ACCESSSPEC request => EnableAccessSpec(request),
            V101Messages.DISABLE_ACCESSSPEC request => Response(DisableAccessSpec(request)),
            V101Messages.KEEPALIVE request => Response(new V101Messages.KEEPALIVE_ACK(request.MessageId)),
            V101Messages.KEEPALIVE_ACK => new VirtualReaderDispatchResult(null, []),
            V101Messages.CLOSE_CONNECTION request => new(
                new V101Messages.CLOSE_CONNECTION_RESPONSE(request.MessageId, Status(V101Enumerations.StatusCode.M_Success, string.Empty)),
                [],
                CloseConnection: true),
            V101Messages.ENABLE_EVENTS_AND_REPORTS => new VirtualReaderDispatchResult(null, []),
            _ => Response(CreateError(message.MessageId, (ushort)V101Enumerations.StatusCode.M_UnsupportedMessage,
                "The virtual reader does not implement this LLRP 1.0.1 message.")),
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

    public ILlrpMessage CreateReaderEventNotification(uint messageId)
    {
        ulong microseconds = checked((ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000));
        var data = new V101Parameters.ReaderEventNotificationData(
            new V101Parameters.UTCTimestamp(microseconds),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new V101Parameters.ConnectionAttemptEvent(V101Enumerations.ConnectionAttemptStatusType.Success),
            null,
            []);
        return new V101Messages.READER_EVENT_NOTIFICATION(messageId, data);
    }

    public IReadOnlyList<ILlrpMessage> BuildInventoryReports(uint roSpecId)
    {
        if (!_state.TryGetRoSpec(roSpecId, out V101Parameters.ROSpec? roSpec) ||
            roSpec is null ||
            roSpec.CurrentState != V101Enumerations.ROSpecState.Active)
        {
            return [];
        }

        VirtualTag[] tags = _state.TagSource.GetTags().ToArray();
        if (tags.Length == 0)
        {
            return [];
        }

        var tagReports = tags
            .Select(tag => BuildTagReport(roSpecId, null, tag, []))
            .ToArray();
        return [new V101Messages.RO_ACCESS_REPORT(NextAsyncMessageId(), tagReports, [], [])];
    }

    private static VirtualReaderDispatchResult Response(ILlrpMessage response) =>
        VirtualReaderDispatchResult.FromResponse(response);

    private V101Messages.GET_READER_CAPABILITIES_RESPONSE Capabilities(uint messageId) =>
        new(
            messageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty),
            BuildGeneralDeviceCapabilities(),
            null,
            _state.Options.UseStrictStandardInventoryProfile ? StrictRegulatoryCapabilities() : null,
            _state.Options.UseStrictStandardInventoryProfile
                ? new V101Parameters.C1G2LLRPCapabilities(false, false, 0)
                : null,
            []);

    private V101Parameters.GeneralDeviceCapabilities BuildGeneralDeviceCapabilities()
    {
        VirtualReaderCapabilities capabilities = _state.Options.Capabilities;
        return new V101Parameters.GeneralDeviceCapabilities(
            capabilities.MaxNumberOfAntennas,
            capabilities.CanSetAntennaProperties,
            capabilities.HasUtcClockCapability,
            capabilities.ManufacturerId,
            capabilities.ModelId,
            capabilities.FirmwareVersion,
            [new V101Parameters.ReceiveSensitivityTableEntry(1, 0)],
            [],
            new V101Parameters.GPIOCapabilities(0, 0),
            [new V101Parameters.PerAntennaAirProtocol(1, [V101Enumerations.AirProtocols.Unspecified])]);
    }

    private static V101Parameters.RegulatoryCapabilities StrictRegulatoryCapabilities() => new(
        CountryCode: 840,
        CommunicationsStandard: V101Enumerations.CommunicationsStandard.US_FCC_Part_15,
        UHFBandCapabilities: new V101Parameters.UHFBandCapabilities(
            [new V101Parameters.TransmitPowerLevelTableEntry(20, 2000)],
            new V101Parameters.FrequencyInformation(
                Hopping: true,
                [new V101Parameters.FrequencyHopTable(1, [902_750])],
                FixedFrequencyTable: null),
            [
                new V101Parameters.C1G2UHFRFModeTable(
                [
                    new V101Parameters.C1G2UHFRFModeTableEntry(
                        ModeIdentifier: 20,
                        DRValue: V101Enumerations.C1G2DRValue.DRV_64_3,
                        EPCHAGTCConformance: true,
                        MValue: V101Enumerations.C1G2MValue.MV_4,
                        ForwardLinkModulation: V101Enumerations.C1G2ForwardLinkModulation.PR_ASK,
                        SpectralMaskIndicator: V101Enumerations.C1G2SpectralMaskIndicator.DI,
                        BDRValue: 64_000,
                        PIEValue: 2_000,
                        MinTariValue: 12_500,
                        MaxTariValue: 23_000,
                        StepTariValue: 2_100),
                ]),
            ]),
        CustomItems: []);

    private V101Messages.GET_READER_CONFIG_RESPONSE GetReaderConfig(V101Messages.GET_READER_CONFIG request)
    {
        IReadOnlyList<V101Parameters.AntennaProperties> properties = Enumerable
            .Range(1, _state.Options.Capabilities.MaxNumberOfAntennas)
            .Select(static id => new V101Parameters.AntennaProperties(true, checked((ushort)id), 0))
            .ToArray();
        IReadOnlyList<V101Parameters.GPIPortCurrentState> gpis = Enumerable
            .Range(1, 4)
            .Select(static id => new V101Parameters.GPIPortCurrentState(
                checked((ushort)id),
                true,
                V101Enumerations.GPIPortState.Low))
            .ToArray();

        return new V101Messages.GET_READER_CONFIG_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty),
            new V101Parameters.Identification(
                V101Enumerations.IdentificationType.EPC,
                ReaderIdBytes(_state.Options.ReaderId)),
            properties,
            _state.GetAntennaConfigurations(),
            _state.GetReaderEventNotificationSpec(),
            _state.GetRoReportSpec(),
            _state.GetAccessReportSpec(),
            null,
            _state.GetKeepaliveSpec(),
            gpis,
            _state.GetGpoWriteData(),
            _state.GetEventsAndReports(),
            []);
    }

    private V101Messages.SET_READER_CONFIG_RESPONSE SetReaderConfig(V101Messages.SET_READER_CONFIG request)
    {
        _state.SetConfiguration(
            request.ResetToFactoryDefault,
            request.AntennaConfigurationItems,
            request.ReaderEventNotificationSpec,
            request.ROReportSpec,
            request.AccessReportSpec,
            request.KeepaliveSpec,
            request.GPOWriteDataItems,
            request.EventsAndReports);
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

        _ = deletedAll;
        return new V101Messages.DELETE_ROSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private V101Messages.ENABLE_ROSPEC_RESPONSE EnableRoSpec(V101Messages.ENABLE_ROSPEC request)
    {
        if (!_state.TryGetRoSpec(request.ROSpecID, out V101Parameters.ROSpec? roSpec) || roSpec is null)
        {
            return new V101Messages.ENABLE_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
        }

        if (roSpec.CurrentState != V101Enumerations.ROSpecState.Disabled)
        {
            return new V101Messages.ENABLE_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "ROSpec is already enabled."));
        }

        _state.TryUpdateRoSpec(
            request.ROSpecID,
            static current => current with { CurrentState = V101Enumerations.ROSpecState.Inactive });
        return new V101Messages.ENABLE_ROSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private V101Messages.DISABLE_ROSPEC_RESPONSE DisableRoSpec(V101Messages.DISABLE_ROSPEC request)
    {
        if (!_state.TryGetRoSpec(request.ROSpecID, out V101Parameters.ROSpec? roSpec) || roSpec is null)
        {
            return new V101Messages.DISABLE_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
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
        return new V101Messages.DISABLE_ROSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private V101Messages.START_ROSPEC_RESPONSE StartRoSpec(V101Messages.START_ROSPEC request)
    {
        if (!_state.TryGetRoSpec(request.ROSpecID, out V101Parameters.ROSpec? roSpec) || roSpec is null)
        {
            return new V101Messages.START_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
        }

        if (roSpec.CurrentState != V101Enumerations.ROSpecState.Inactive)
        {
            return new V101Messages.START_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "ROSpec must be inactive before it can be started."));
        }

        _state.TryUpdateRoSpec(
            request.ROSpecID,
            static current => current with { CurrentState = V101Enumerations.ROSpecState.Active });
        return new V101Messages.START_ROSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private V101Messages.STOP_ROSPEC_RESPONSE StopRoSpec(V101Messages.STOP_ROSPEC request)
    {
        if (!_state.TryGetRoSpec(request.ROSpecID, out V101Parameters.ROSpec? roSpec) || roSpec is null)
        {
            return new V101Messages.STOP_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
        }

        if (roSpec.CurrentState != V101Enumerations.ROSpecState.Active)
        {
            return new V101Messages.STOP_ROSPEC_RESPONSE(
                request.MessageId,
                Status(V101Enumerations.StatusCode.M_ParameterError, "Only an active ROSpec can be stopped."));
        }

        _state.TryUpdateRoSpec(
            request.ROSpecID,
            static current => current with { CurrentState = V101Enumerations.ROSpecState.Inactive });
        return new V101Messages.STOP_ROSPEC_RESPONSE(
            request.MessageId,
            Status(V101Enumerations.StatusCode.M_Success, string.Empty));
    }

    private V101Messages.ADD_ACCESSSPEC_RESPONSE AddAccessSpec(V101Messages.ADD_ACCESSSPEC request)
    {
        if (!_state.TryAddAccessSpec(request.AccessSpec))
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

    private VirtualReaderDispatchResult EnableAccessSpec(V101Messages.ENABLE_ACCESSSPEC request)
    {
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
        return VirtualReaderDispatchResult.WithMessages(response, BuildAccessReport(accessSpec with
        {
            CurrentState = V101Enumerations.AccessSpecState.Active,
        }));
    }

    private V101Messages.DISABLE_ACCESSSPEC_RESPONSE DisableAccessSpec(V101Messages.DISABLE_ACCESSSPEC request)
    {
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
        VirtualTag tag = _state.TagSource.GetTags().FirstOrDefault() ?? new VirtualTag
        {
            ElectronicProductCode = Convert.FromHexString("E28011710000020D056E9BEE"),
        };
        bool selected = MatchesTag(accessSpec.AccessCommand.AirProtocolTagSpec, tag);
        IReadOnlyList<ILlrpParameter> results = accessSpec.AccessCommand.AccessCommandOpSpecItems
            .Select(operation => BuildOperationResult(operation, selected, tag))
            .ToArray();
        return new V101Messages.RO_ACCESS_REPORT(
            NextAsyncMessageId(),
            [BuildTagReport(accessSpec.ROSpecID, accessSpec.AccessSpecID, tag, results)],
            [],
            []);
    }

    private ILlrpParameter BuildOperationResult(ILlrpParameter operation, bool selected, VirtualTag tag)
    {
        if (!selected)
        {
            return operation switch
            {
                V101Parameters.C1G2Read read => new V101Parameters.C1G2ReadOpSpecResult(
                    V101Enumerations.C1G2ReadResultType.No_Response_From_Tag,
                    read.OpSpecID,
                    []),
                V101Parameters.C1G2Write write => new V101Parameters.C1G2WriteOpSpecResult(
                    V101Enumerations.C1G2WriteResultType.No_Response_From_Tag,
                    write.OpSpecID,
                    0),
                _ => throw new NotSupportedException($"Virtual reader does not implement access operation {operation.GetType().Name}."),
            };
        }

        return operation switch
        {
            V101Parameters.C1G2Read read => Read(read, tag),
            V101Parameters.C1G2Write write => Write(write, tag),
            _ => throw new NotSupportedException($"Virtual reader does not implement access operation {operation.GetType().Name}."),
        };
    }

    private V101Parameters.C1G2ReadOpSpecResult Read(V101Parameters.C1G2Read operation, VirtualTag tag)
    {
        bool success = _state.TagSource.TryReadWords(
            tag.ElectronicProductCode.Span,
            operation.MB,
            operation.WordPointer,
            operation.WordCount,
            out IReadOnlyList<ushort> words);
        return success
            ? new V101Parameters.C1G2ReadOpSpecResult(V101Enumerations.C1G2ReadResultType.Success, operation.OpSpecID, words)
            : new V101Parameters.C1G2ReadOpSpecResult(V101Enumerations.C1G2ReadResultType.Nonspecific_Tag_Error, operation.OpSpecID, []);
    }

    private V101Parameters.C1G2WriteOpSpecResult Write(V101Parameters.C1G2Write operation, VirtualTag tag)
    {
        bool success = _state.TagSource.TryWriteWords(
            tag.ElectronicProductCode.Span,
            operation.MB,
            operation.WordPointer,
            operation.WriteData);
        return success
            ? new V101Parameters.C1G2WriteOpSpecResult(
                V101Enumerations.C1G2WriteResultType.Success,
                operation.OpSpecID,
                checked((ushort)operation.WriteData.Count))
            : new V101Parameters.C1G2WriteOpSpecResult(
                V101Enumerations.C1G2WriteResultType.Tag_Memory_Overrun_Error,
                operation.OpSpecID,
                0);
    }

    private bool MatchesTag(V101Choices.IAirProtocolTagSpec tagSpec, VirtualTag tag)
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

    private bool MatchesTarget(V101Parameters.C1G2TargetTag target, VirtualTag tag)
    {
        if (!_state.TagSource.TryGetMemoryBytes(tag.ElectronicProductCode.Span, target.MB, out ReadOnlyMemory<byte> bytes))
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
        VirtualTag tag,
        IReadOnlyList<ILlrpParameter> results) =>
        new(
            BuildEpcParameter(tag),
            new V101Parameters.ROSpecID(roSpecId),
            null,
            new V101Parameters.InventoryParameterSpecID(1),
            new V101Parameters.AntennaID(tag.AntennaId),
            new V101Parameters.PeakRSSI(checked((sbyte)tag.PeakRssi)),
            new V101Parameters.ChannelIndex(tag.ChannelIndex),
            new V101Parameters.FirstSeenTimestampUTC(
                checked((ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000))),
            null,
            null,
            null,
            new V101Parameters.TagSeenCount(1),
            [],
            accessSpecId is uint id ? new V101Parameters.AccessSpecID(id) : null,
            results,
            []);

    private static V101Choices.IEPCParameter BuildEpcParameter(VirtualTag tag) =>
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
