using System.Collections.Generic;
using System.Linq;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpSdk;

/// <summary>LLRP 1.0.1 implementation of the SDK protocol-adapter boundary.</summary>
internal sealed class Llrp101ProtocolAdapter : ILlrpProtocolAdapter
{
    public LlrpProtocolVersion Version => LlrpProtocolVersion.Version101;

    public void RegisterStandardCodecs(LlrpCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Llrp101StandardModule.Register(registry);
    }

    public async Task<ReaderIdentity> FetchIdentityAsync(
        LlrpReader reader,
        uint messageId,
        CancellationToken cancellationToken)
    {
        GET_READER_CAPABILITIES_RESPONSE response = await reader
            .TransactDuringInitializationAsync<GET_READER_CAPABILITIES_RESPONSE>(
                new GET_READER_CAPABILITIES(messageId, GetReaderCapabilitiesRequestedData.General_Device_Capabilities, []),
                cancellationToken,
                MatchesCapabilitiesResponse)
            .ConfigureAwait(false);
        EnsureSuccess("GET_READER_CAPABILITIES", response.LLRPStatus);

        GeneralDeviceCapabilities general = response.GeneralDeviceCapabilities ??
            throw new LlrpReaderInitializationException(
                "A successful LLRP 1.0.1 GET_READER_CAPABILITIES(General_Device_Capabilities) response must contain " +
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
                new GET_READER_CAPABILITIES(messageId, GetReaderCapabilitiesRequestedData.All, []),
                cancellationToken,
                MatchesCapabilitiesResponse)
            .ConfigureAwait(false);
        EnsureSuccess("GET_READER_CAPABILITIES", response.LLRPStatus);

        GeneralDeviceCapabilities general = response.GeneralDeviceCapabilities ??
            throw new LlrpReaderInitializationException(
                "A successful LLRP 1.0.1 GET_READER_CAPABILITIES(All) response must contain " +
                "exactly one GeneralDeviceCapabilities parameter.");
        ILlrpParameter[] generalParameters =
        [
            .. general.ReceiveSensitivityTableEntryItems,
            .. general.PerAntennaReceiveSensitivityRangeItems,
            general.GPIOCapabilities,
            .. general.PerAntennaAirProtocolItems,
        ];
        return new ReaderCapabilities(
            general.MaxNumberOfAntennaSupported,
            general.CanSetAntennaProperties,
            general.HasUTCClockCapability,
            generalParameters,
            response,
            response.CustomItems);
    }

    public ILlrpParameter CompileInventory(
        ReaderSettings settings,
        IReadOnlyList<ILlrpParameter> roReportSpecCustomItems) =>
        Llrp101InventoryCompiler.Compile(settings, roReportSpecCustomItems);

    public ILlrpParameter CompileTagAccess(uint accessSpecId, uint roSpecId, TagAccessRequest request) =>
        Llrp101TagAccessCompiler.Compile(accessSpecId, roSpecId, request);

    public IReadOnlyList<TranslatedTagReport> TranslateTagReports(ILlrpMessage message) =>
        message is RO_ACCESS_REPORT report ? Llrp101TagReportTranslator.Translate(report) : [];

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
            "The supplied ROSpec must be generated for LLRP 1.0.1 parameter type 177.",
            nameof(parameter));

    private static AccessSpec RequireAccessSpec(ILlrpParameter parameter) => parameter as AccessSpec ??
        throw new ArgumentException(
            "The supplied AccessSpec must be generated for LLRP 1.0.1 parameter type 207.",
            nameof(parameter));

    private static void EnsureSuccess(string operation, LLRPStatus status)
    {
        if (status.StatusCode != StatusCode.M_Success)
        {
            throw new LlrpReaderOperationException(
                operation,
                checked((ushort)status.StatusCode),
                status.ErrorDescription,
                status);
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
                    RequestedData: GetReaderConfigRequestedData.All,
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
            bool GetState(NotificationEventType type) =>
                response.ReaderEventNotificationSpec.EventNotificationStateItems
                    .FirstOrDefault(e => e.EventType == type)?.NotificationState ?? false;

            events = new EventNotificationConfiguration
            {
                HoppingEventEnabled = GetState(NotificationEventType.Upon_Hopping_To_Next_Channel),
                GpiEventEnabled = GetState(NotificationEventType.GPI_Event),
                RoSpecEventEnabled = GetState(NotificationEventType.ROSpec_Event),
                ReportBufferWarningEnabled = GetState(NotificationEventType.Report_Buffer_Fill_Warning),
                ReaderExceptionEventEnabled = GetState(NotificationEventType.Reader_Exception_Event),
                RfSurveyEventEnabled = GetState(NotificationEventType.RFSurvey_Event),
                AiSpecEventEnabled = GetState(NotificationEventType.AISpec_Event),
                AntennaEventEnabled = GetState(NotificationEventType.Antenna_Event)
            };
        }

        return new TranslatedReaderConfiguration(
            new ReaderConfiguration
            {
                Keepalive = keepalive,
                Antennas = antennas,
                Gpos = gpos,
                Gpis = gpis,
                Events = events
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
                (global::LlrpNet.Protocol.Enumerations.V1_0_1.KeepaliveTriggerType)(int)configuration.Keepalive.TriggerType,
                configuration.Keepalive.IntervalMs
            );
        }

        var antennaConfigs = new List<AntennaConfiguration>();
        if (configuration.Antennas != null)
        {
            foreach (var item in configuration.Antennas)
            {
                RFTransmitter? rfTransmitter = null;
                if (item.TransmitPowerIndex.HasValue || item.ChannelIndex.HasValue)
                {
                    rfTransmitter = new RFTransmitter(0, item.ChannelIndex ?? 0, item.TransmitPowerIndex ?? 0);
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
                new(NotificationEventType.Upon_Hopping_To_Next_Channel, configuration.Events.HoppingEventEnabled),
                new(NotificationEventType.GPI_Event, configuration.Events.GpiEventEnabled),
                new(NotificationEventType.ROSpec_Event, configuration.Events.RoSpecEventEnabled),
                new(NotificationEventType.Report_Buffer_Fill_Warning, configuration.Events.ReportBufferWarningEnabled),
                new(NotificationEventType.Reader_Exception_Event, configuration.Events.ReaderExceptionEventEnabled),
                new(NotificationEventType.RFSurvey_Event, configuration.Events.RfSurveyEventEnabled),
                new(NotificationEventType.AISpec_Event, configuration.Events.AiSpecEventEnabled),
                new(NotificationEventType.Antenna_Event, configuration.Events.AntennaEventEnabled)
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
            EventsAndReports: null,
            CustomItems: customItems
        );

        SET_READER_CONFIG_RESPONSE response = await reader
            .TransactFromRawProtocolAsync<SET_READER_CONFIG_RESPONSE>(
                message,
                timeout: null,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        EnsureSuccess("SET_READER_CONFIG", response.LLRPStatus);
    }
}
