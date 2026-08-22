using System.Collections.Generic;
using System.Linq;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_1;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_1;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions;
using LlrpNet.Protocol.Registry.V1_1;
using V11Enumerations = LlrpNet.Protocol.Enumerations.V1_1;

namespace LlrpSdk;

/// <summary>LLRP 1.1 implementation of the SDK protocol-adapter boundary.</summary>
internal sealed class Llrp11ProtocolAdapter : ILlrpProtocolAdapter
{
    public LlrpProtocolVersion Version => LlrpProtocolVersion.Version11;

    public void RegisterStandardCodecs(LlrpCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Llrp11StandardModule.Register(registry);
    }

    public async Task<ReaderIdentity> FetchIdentityAsync(
        LlrpReader reader,
        uint messageId,
        CancellationToken cancellationToken)
    {
        GET_READER_CAPABILITIES_RESPONSE response = await reader
            .TransactDuringInitializationAsync<GET_READER_CAPABILITIES_RESPONSE>(
                new GET_READER_CAPABILITIES(messageId, V11Enumerations.GetReaderCapabilitiesRequestedData.General_Device_Capabilities, []),
                cancellationToken,
                MatchesCapabilitiesResponse)
            .ConfigureAwait(false);
        EnsureSuccess("GET_READER_CAPABILITIES", response.LLRPStatus);

        GeneralDeviceCapabilities general = response.GeneralDeviceCapabilities ??
            throw new LlrpReaderInitializationException(
                "A successful LLRP 1.1 GET_READER_CAPABILITIES(General_Device_Capabilities) response must contain " +
                "exactly one GeneralDeviceCapabilities parameter.");

        return new ReaderIdentity(general.DeviceManufacturerName, general.ModelName, general.ReaderFirmwareVersion);
    }

    public async Task<ReaderCapabilities> FetchCapabilitiesAsync(
        LlrpReader reader,
        uint messageId,
        CancellationToken cancellationToken)
    {
        GET_READER_CAPABILITIES_RESPONSE response = await reader
            .TransactDuringInitializationAsync<GET_READER_CAPABILITIES_RESPONSE>(
                new GET_READER_CAPABILITIES(messageId, V11Enumerations.GetReaderCapabilitiesRequestedData.All, []),
                cancellationToken,
                MatchesCapabilitiesResponse)
            .ConfigureAwait(false);
        EnsureSuccess("GET_READER_CAPABILITIES", response.LLRPStatus);

        GeneralDeviceCapabilities general = response.GeneralDeviceCapabilities ??
            throw new LlrpReaderInitializationException(
                "A successful LLRP 1.1 GET_READER_CAPABILITIES(All) response must contain " +
                "exactly one GeneralDeviceCapabilities parameter.");
        ILlrpParameter[] generalParameters =
        [
            .. general.ReceiveSensitivityTableEntryItems,
            .. general.PerAntennaReceiveSensitivityRangeItems,
            general.GPIOCapabilities,
            .. general.PerAntennaAirProtocolItems,
            .. (general.MaximumReceiveSensitivity is null
                ? Array.Empty<ILlrpParameter>()
                : [general.MaximumReceiveSensitivity]),
        ];

        var rxSensitivities = general.ReceiveSensitivityTableEntryItems
            .Select(e => new RxSensitivityEntry(e.Index, e.ReceiveSensitivityValue))
            .ToList();

        var txPowers = new List<TxPowerEntry>();
        var txFrequencies = new List<uint>();
        var hopTables = new List<FrequencyHopTableEntry>();
        var rfModes = new List<C1G2RfModeEntry>();

        if (response.RegulatoryCapabilities?.UHFBandCapabilities is UHFBandCapabilities uhfBand)
        {
            txPowers.AddRange(uhfBand.TransmitPowerLevelTableEntryItems
                .Select(e => new TxPowerEntry(e.Index, e.TransmitPowerValue)));

            if (uhfBand.FrequencyInformation.FixedFrequencyTable is FixedFrequencyTable fixedTable)
            {
                txFrequencies.AddRange(fixedTable.Frequency);
            }

            foreach (FrequencyHopTable hopTable in uhfBand.FrequencyInformation.FrequencyHopTableItems)
            {
                hopTables.Add(new FrequencyHopTableEntry(hopTable.HopTableID, hopTable.Frequency));
            }

            foreach (var airTableChoice in uhfBand.AirProtocolUHFRFModeTableItems)
            {
                if (airTableChoice is C1G2UHFRFModeTable c1g2Table)
                {
                    rfModes.AddRange(c1g2Table.C1G2UHFRFModeTableEntryItems.Select(m => new C1G2RfModeEntry(
                        m.ModeIdentifier,
                        m.DRValue.ToString(),
                        m.EPCHAGTCConformance,
                        (byte)m.MValue,
                        m.ForwardLinkModulation.ToString(),
                        m.SpectralMaskIndicator.ToString(),
                        m.BDRValue,
                        m.PIEValue,
                        m.MinTariValue,
                        m.MaxTariValue,
                        m.StepTariValue)));
                }
            }
        }

        bool isTagAccessAvailable = response.LLRPCapabilities is null || response.LLRPCapabilities.MaxNumAccessSpecs > 0;
        bool canDoStateAware = response.LLRPCapabilities?.CanDoTagInventoryStateAwareSingulation ?? false;
        bool supportsClientRequestOpSpec = response.LLRPCapabilities?.SupportsClientRequestOpSpec ?? false;
        bool canDoRfSurvey = response.LLRPCapabilities?.CanDoRFSurvey ?? false;
        bool isBlockWrite = false;
        bool isBlockErase = false;
        C1G2LLRPCapabilities? c1g2Caps = response.AirProtocolLLRPCapabilities as C1G2LLRPCapabilities;

        if (c1g2Caps is not null)
        {
            isBlockWrite = c1g2Caps.CanSupportBlockWrite;
            isBlockErase = c1g2Caps.CanSupportBlockErase;
        }

        LLRPCapabilities? llrpCaps = response.LLRPCapabilities;
        ReaderResourceLimits resourceLimits = ReaderResourceLimits.FromLlrp(
            llrpCaps?.MaxNumPriorityLevelsSupported,
            llrpCaps?.MaxNumROSpecs,
            llrpCaps?.MaxNumSpecsPerROSpec,
            llrpCaps?.MaxNumInventoryParameterSpecsPerAISpec,
            llrpCaps?.MaxNumAccessSpecs,
            llrpCaps?.MaxNumOpSpecsPerAccessSpec,
            c1g2Caps?.MaxNumSelectFiltersPerQuery);

        return new ReaderCapabilities(
            general.MaxNumberOfAntennaSupported,
            general.CanSetAntennaProperties,
            general.HasUTCClockCapability,
            generalParameters,
            response,
            response.CustomItems,
            txPowers,
            rxSensitivities,
            txFrequencies,
            hopTables,
            rfModes,
            isTagAccessAvailable,
            isBlockWrite,
            isBlockErase,
            canDoStateAware,
            supportsClientRequestOpSpec,
            canDoRfSurvey,
            general.MaximumReceiveSensitivity?.MaximumSensitivityValue,
            resourceLimits);
    }

    public ILlrpParameter CompileInventory(
        InventorySettings settings,
        uint roSpecId,
        InventoryCustomItems customItems,
        bool supportsStateAwareSingulation) =>
        Llrp11InventoryCompiler.Compile(settings, roSpecId, customItems.RoReportSpec, customItems.C1G2InventoryCommand, supportsStateAwareSingulation);

    public ILlrpParameter CompileTagAccess(uint accessSpecId, uint roSpecId, TagAccessRequest request, bool useBlockWrite = false) =>
        Llrp11TagAccessCompiler.Compile(accessSpecId, roSpecId, request, useBlockWrite);

    public ILlrpParameter CompileTagAccessSequence(
        uint accessSpecId,
        uint roSpecId,
        IReadOnlyList<TagAccessRequest> requests,
        bool useBlockWrite = false) =>
        Llrp11TagAccessCompiler.CompileSequence(accessSpecId, roSpecId, requests, useBlockWrite);

    public IReadOnlyList<TranslatedTagReport> TranslateTagReports(ILlrpMessage message) =>
        message is RO_ACCESS_REPORT report ? Llrp11TagReportTranslator.Translate(report) : [];

    public ManagedRoSpecSnapshot ParseManagedRoSpec(
        LlrpReader reader,
        ILlrpParameter roSpec,
        IReadOnlyList<ILlrpParameter> accessSpecs)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(roSpec);
        ArgumentNullException.ThrowIfNull(accessSpecs);
        ReaderIdentity identity = reader.Identity ??
            throw new InvalidOperationException("Inventory settings query requires initialized reader metadata.");
        ReaderCapabilities capabilities = reader.Capabilities ??
            throw new InvalidOperationException("Inventory settings query requires initialized reader metadata.");
        ParsedManagedRoSpec parsed = Llrp11ManagedRoSpecParser.Parse(
            roSpec as ROSpec ?? throw new ArgumentException(
                "The supplied ROSpec must be generated for LLRP 1.1 parameter type 177.",
                nameof(roSpec)),
            accessSpecs);
        return ManagedInventoryStateAssembler.Assemble(
            parsed,
            identity,
            capabilities,
            reader.NegotiatedVersion,
            reader.Extensions.OfType<IInventorySettingsContributor>());
    }

    public bool IsManagedRoSpec(ILlrpParameter item) =>
        item is ROSpec roSpec && roSpec.ROSpecID == LlrpReader.ManagedInventoryRoSpecId;

    public uint GetRoSpecId(ILlrpParameter item) => (item as ROSpec ?? throw new ArgumentException(
        "The supplied parameter is not an LLRP 1.1 ROSpec.", nameof(item))).ROSpecID;

    public InventoryRuntimeState GetRoSpecRuntimeState(ILlrpParameter item) => (item as ROSpec ?? throw new ArgumentException(
        "The supplied parameter is not an LLRP 1.1 ROSpec.", nameof(item))).CurrentState switch
    {
        ROSpecState.Active => InventoryRuntimeState.Running,
        ROSpecState.Inactive => InventoryRuntimeState.Enabled,
        _ => InventoryRuntimeState.Disabled,
    };

    public bool HasAttachedDataAccessSpec(IReadOnlyList<ILlrpParameter> accessSpecs) =>
        accessSpecs.Any(item => item is AccessSpec spec &&
            spec.AccessSpecID == LlrpReader.ManagedInventoryAttachedDataAccessSpecId);

    public uint GetAccessSpecId(ILlrpParameter item) => (item as AccessSpec ?? throw new ArgumentException(
        "The supplied parameter is not an LLRP 1.1 AccessSpec.", nameof(item))).AccessSpecID;

    public async Task<IReadOnlyList<TranslatedTagReport>> FetchReportsAsync(
        LlrpReader reader,
        uint messageId,
        CancellationToken cancellationToken)
    {
        RO_ACCESS_REPORT report = await reader.TransactAsync<RO_ACCESS_REPORT>(
            new GET_REPORT(messageId), timeout: null, cancellationToken).ConfigureAwait(false);
        return TranslateTagReports(report);
    }

    public async Task AddRoSpecAsync(LlrpReader reader, uint messageId, ILlrpParameter roSpec, CancellationToken cancellationToken)
    {
        ADD_ROSPEC_RESPONSE response = await reader.TransactAsync<ADD_ROSPEC_RESPONSE>(
            new ADD_ROSPEC(messageId, RequireRoSpec(roSpec)), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("ADD_ROSPEC", response.LLRPStatus);
    }

    public async Task DeleteRoSpecAsync(LlrpReader reader, uint messageId, uint roSpecId, CancellationToken cancellationToken)
    {
        DELETE_ROSPEC_RESPONSE response = await reader.TransactAsync<DELETE_ROSPEC_RESPONSE>(
            new DELETE_ROSPEC(messageId, roSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("DELETE_ROSPEC", response.LLRPStatus);
    }

    public async Task EnableRoSpecAsync(LlrpReader reader, uint messageId, uint roSpecId, CancellationToken cancellationToken)
    {
        ENABLE_ROSPEC_RESPONSE response = await reader.TransactAsync<ENABLE_ROSPEC_RESPONSE>(
            new ENABLE_ROSPEC(messageId, roSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("ENABLE_ROSPEC", response.LLRPStatus);
    }

    public async Task DisableRoSpecAsync(LlrpReader reader, uint messageId, uint roSpecId, CancellationToken cancellationToken)
    {
        DISABLE_ROSPEC_RESPONSE response = await reader.TransactAsync<DISABLE_ROSPEC_RESPONSE>(
            new DISABLE_ROSPEC(messageId, roSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("DISABLE_ROSPEC", response.LLRPStatus);
    }

    public async Task StartRoSpecAsync(LlrpReader reader, uint messageId, uint roSpecId, CancellationToken cancellationToken)
    {
        START_ROSPEC_RESPONSE response = await reader.TransactAsync<START_ROSPEC_RESPONSE>(
            new START_ROSPEC(messageId, roSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("START_ROSPEC", response.LLRPStatus);
    }

    public async Task StopRoSpecAsync(LlrpReader reader, uint messageId, uint roSpecId, CancellationToken cancellationToken)
    {
        STOP_ROSPEC_RESPONSE response = await reader.TransactAsync<STOP_ROSPEC_RESPONSE>(
            new STOP_ROSPEC(messageId, roSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("STOP_ROSPEC", response.LLRPStatus);
    }

    public async Task<IReadOnlyList<ILlrpParameter>> GetRoSpecsAsync(
        LlrpReader reader, uint messageId, CancellationToken cancellationToken)
    {
        GET_ROSPECS_RESPONSE response = await reader.TransactAsync<GET_ROSPECS_RESPONSE>(
            new GET_ROSPECS(messageId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("GET_ROSPECS", response.LLRPStatus);
        return Array.AsReadOnly(response.ROSpecItems.Cast<ILlrpParameter>().ToArray());
    }

    public async Task AddAccessSpecAsync(
        LlrpReader reader, uint messageId, ILlrpParameter accessSpec, CancellationToken cancellationToken)
    {
        ADD_ACCESSSPEC_RESPONSE response = await reader.TransactAsync<ADD_ACCESSSPEC_RESPONSE>(
            new ADD_ACCESSSPEC(messageId, RequireAccessSpec(accessSpec)), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("ADD_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task DeleteAccessSpecAsync(
        LlrpReader reader, uint messageId, uint accessSpecId, CancellationToken cancellationToken)
    {
        DELETE_ACCESSSPEC_RESPONSE response = await reader.TransactAsync<DELETE_ACCESSSPEC_RESPONSE>(
            new DELETE_ACCESSSPEC(messageId, accessSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("DELETE_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task EnableAccessSpecAsync(
        LlrpReader reader, uint messageId, uint accessSpecId, CancellationToken cancellationToken)
    {
        ENABLE_ACCESSSPEC_RESPONSE response = await reader.TransactAsync<ENABLE_ACCESSSPEC_RESPONSE>(
            new ENABLE_ACCESSSPEC(messageId, accessSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("ENABLE_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task DisableAccessSpecAsync(
        LlrpReader reader, uint messageId, uint accessSpecId, CancellationToken cancellationToken)
    {
        DISABLE_ACCESSSPEC_RESPONSE response = await reader.TransactAsync<DISABLE_ACCESSSPEC_RESPONSE>(
            new DISABLE_ACCESSSPEC(messageId, accessSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("DISABLE_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task<IReadOnlyList<ILlrpParameter>> GetAccessSpecsAsync(
        LlrpReader reader, uint messageId, CancellationToken cancellationToken)
    {
        GET_ACCESSSPECS_RESPONSE response = await reader.TransactAsync<GET_ACCESSSPECS_RESPONSE>(
            new GET_ACCESSSPECS(messageId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("GET_ACCESSSPECS", response.LLRPStatus);
        return Array.AsReadOnly(response.AccessSpecItems.Cast<ILlrpParameter>().ToArray());
    }

    private static ROSpec RequireRoSpec(ILlrpParameter parameter) => parameter as ROSpec ??
        throw new ArgumentException(
            "The supplied ROSpec must be generated for LLRP 1.1 parameter type 177.",
            nameof(parameter));

    private static AccessSpec RequireAccessSpec(ILlrpParameter parameter) => parameter as AccessSpec ??
        throw new ArgumentException(
            "The supplied AccessSpec must be generated for LLRP 1.1 parameter type 207.",
            nameof(parameter));

    private static void EnsureSuccess(string operation, LLRPStatus status)
    {
        if (status.StatusCode != V11Enumerations.StatusCode.M_Success)
        {
            throw new LlrpReaderOperationException(
                operation,
                checked((ushort)status.StatusCode),
                status.ErrorDescription,
                status,
                Enum.GetName(typeof(StatusCode), (long)status.StatusCode));
        }
    }

    private static bool MatchesCapabilitiesResponse(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame) =>
        header.MessageType is GET_READER_CAPABILITIES_RESPONSE.MessageType or 100;

    private static bool MatchesConfigResponse(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame) =>
        header.MessageType is GET_READER_CONFIG_RESPONSE.MessageType or 100;

    public async Task<TranslatedReaderConfiguration> QueryConfigurationAsync(
        LlrpReader reader,
        uint messageId,
        IReadOnlyList<ILlrpParameter> customItems,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customItems);
        GET_READER_CONFIG_RESPONSE response = await reader
            .TransactAsync<GET_READER_CONFIG_RESPONSE>(
                new GET_READER_CONFIG(
                    messageId,
                    AntennaID: 0,
                    RequestedData: global::LlrpNet.Protocol.Enumerations.V1_1.GetReaderConfigRequestedData.All,
                    GPIPortNum: 0,
                    GPOPortNum: 0,
                    CustomItems: customItems
                ),
                timeout: null,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        EnsureSuccess("GET_READER_CONFIG", response.LLRPStatus);

        var keepalive = response.KeepaliveSpec != null
            ? new KeepaliveConfiguration
            {
                TriggerType = (KeepaliveTriggerType)(int)response.KeepaliveSpec.KeepaliveTriggerType,
                IntervalMs = response.KeepaliveSpec.PeriodicTriggerValue
            }
            : new KeepaliveConfiguration();

        var propsDict = response.AntennaPropertiesItems.ToDictionary(p => p.AntennaID);
        var configDict = response.AntennaConfigurationItems.ToDictionary(c => c.AntennaID);
        var antennaIds = propsDict.Keys.Union(configDict.Keys).OrderBy(id => id).ToList();

        var antennas = new List<AntennaConfigurationSettings>();
        foreach (var id in antennaIds)
        {
            propsDict.TryGetValue(id, out var prop);
            configDict.TryGetValue(id, out var conf);

            antennas.Add(new AntennaConfigurationSettings
            {
                AntennaId = id,
                IsConnected = prop?.AntennaConnected,
                Gain = prop?.AntennaGain,
                TransmitPowerIndex = conf?.RFTransmitter?.TransmitPower,
                HopTableId = conf?.RFTransmitter?.HopTableID,
                ReceiverSensitivityIndex = conf?.RFReceiver?.ReceiverSensitivity,
                ChannelIndex = conf?.RFTransmitter?.ChannelIndex
            });
        }

        var gpos = response.GPOWriteDataItems.Select(g => new GpoConfiguration
        {
            GpoPortNumber = g.GPOPortNumber,
            GpoData = g.GPOData
        }).ToList();

        var gpis = response.GPIPortCurrentStateItems.Select(g => new GpiStatus
        {
            GpiPortNumber = g.GPIPortNum,
            Configured = g.Config,
            State = (GpiState)(int)g.State
        }).ToList();

        var events = new EventNotificationConfiguration();
        if (response.ReaderEventNotificationSpec != null)
        {
            bool GetState(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType type) =>
                response.ReaderEventNotificationSpec!.EventNotificationStateItems
                    .FirstOrDefault(e => e.EventType == type)?.NotificationState ?? false;

            events = new EventNotificationConfiguration
            {
                HoppingEventEnabled = GetState(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.Upon_Hopping_To_Next_Channel),
                GpiEventEnabled = GetState(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.GPI_Event),
                RoSpecEventEnabled = GetState(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.ROSpec_Event),
                ReportBufferWarningEnabled = GetState(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.Report_Buffer_Fill_Warning),
                ReaderExceptionEventEnabled = GetState(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.Reader_Exception_Event),
                RfSurveyEventEnabled = GetState(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.RFSurvey_Event),
                AiSpecEventEnabled = GetState(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.AISpec_Event),
                AntennaEventEnabled = GetState(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.Antenna_Event)
            };
        }

        return new TranslatedReaderConfiguration(
            new ReaderConfiguration
            {
                Keepalive = keepalive,
                Antennas = antennas,
                Gpos = gpos,
                Gpis = gpis,
                Events = events,
                HoldEventsAndReportsUponReconnect = response.EventsAndReports?.HoldEventsAndReportsUponReconnect ?? false,
            },
            response.CustomItems);
    }

    public async Task ApplyConfigurationAsync(
        LlrpReader reader,
        uint messageId,
        ReaderConfiguration configuration,
        IReadOnlyList<ILlrpParameter> customItems,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(customItems);

        KeepaliveSpec? keepaliveSpec = null;
        if (configuration.Keepalive != null)
        {
            keepaliveSpec = new KeepaliveSpec(
                (global::LlrpNet.Protocol.Enumerations.V1_1.KeepaliveTriggerType)(int)configuration.Keepalive.TriggerType,
                configuration.Keepalive.IntervalMs
            );
        }

        var antennaConfigs = new List<AntennaConfiguration>();
        if (configuration.Antennas != null)
        {
            foreach (var item in configuration.Antennas)
            {
                RFTransmitter? rfTransmitter = null;
                if (item.TransmitPowerIndex.HasValue || item.HopTableId.HasValue || item.ChannelIndex.HasValue)
                {
                    rfTransmitter = new RFTransmitter(
                        item.HopTableId ?? 0,
                        item.ChannelIndex ?? 0,
                        item.TransmitPowerIndex ?? 0);
                }

                RFReceiver? rfReceiver = null;
                if (item.ReceiverSensitivityIndex.HasValue)
                {
                    rfReceiver = new RFReceiver(item.ReceiverSensitivityIndex.Value);
                }

                antennaConfigs.Add(new AntennaConfiguration(
                    item.AntennaId,
                    rfReceiver,
                    rfTransmitter,
                    []
                ));
            }
        }

        ReaderEventNotificationSpec? eventNotificationSpec = null;
        if (configuration.Events != null)
        {
            var stateItems = new List<EventNotificationState>
            {
                new(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.Upon_Hopping_To_Next_Channel, configuration.Events.HoppingEventEnabled),
                new(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.GPI_Event, configuration.Events.GpiEventEnabled),
                new(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.ROSpec_Event, configuration.Events.RoSpecEventEnabled),
                new(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.Report_Buffer_Fill_Warning, configuration.Events.ReportBufferWarningEnabled),
                new(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.Reader_Exception_Event, configuration.Events.ReaderExceptionEventEnabled),
                new(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.RFSurvey_Event, configuration.Events.RfSurveyEventEnabled),
                new(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.AISpec_Event, configuration.Events.AiSpecEventEnabled),
                new(global::LlrpNet.Protocol.Enumerations.V1_1.NotificationEventType.Antenna_Event, configuration.Events.AntennaEventEnabled)
            };
            eventNotificationSpec = new ReaderEventNotificationSpec(stateItems);
        }

        var gpoItems = configuration.Gpos?.Select(g => new GPOWriteData(g.GpoPortNumber, g.GpoData)).ToList() ?? [];

        var message = new SET_READER_CONFIG(
            messageId,
            ResetToFactoryDefault: false,
            eventNotificationSpec,
            AntennaPropertiesItems: [],
            AntennaConfigurationItems: antennaConfigs,
            ROReportSpec: null,
            AccessReportSpec: null,
            KeepaliveSpec: keepaliveSpec,
            GPOWriteDataItems: gpoItems,
            GPIPortCurrentStateItems: [],
            EventsAndReports: new EventsAndReports(configuration.HoldEventsAndReportsUponReconnect),
            CustomItems: customItems
        );

        SET_READER_CONFIG_RESPONSE response = await reader
            .TransactAsync<SET_READER_CONFIG_RESPONSE>(
                message,
                timeout: null,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        EnsureSuccess("SET_READER_CONFIG", response.LLRPStatus);
    }
}
