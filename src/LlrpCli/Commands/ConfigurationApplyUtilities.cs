using System;
using System.Linq;
using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>Parsed configuration changes for one connected Live Shell session.</summary>
internal sealed class ConfigApplySettings
{
    public string? KeepaliveType { get; init; }
    public uint? KeepaliveInterval { get; init; }
    public ushort? AntennaId { get; init; }
    public ushort? TransmitPower { get; init; }
    public ushort? ReceiverSensitivity { get; init; }
    public ushort? ChannelIndex { get; init; }
    public ushort? GpoPort { get; init; }
    public bool? GpoData { get; init; }
    public bool DryRun { get; init; }
}

internal static class ConfigurationApplyUtilities
{
    public static bool TryValidateRequestedChanges(
        ConfigApplySettings settings,
        ReaderCapabilities? capabilities,
        out string? error)
    {
        bool antennaValueSpecified = settings.TransmitPower.HasValue ||
            settings.ReceiverSensitivity.HasValue ||
            settings.ChannelIndex.HasValue;
        bool keepaliveSpecified = settings.KeepaliveType is not null || settings.KeepaliveInterval.HasValue;
        bool gpoSpecified = settings.GpoPort.HasValue || settings.GpoData.HasValue;

        if (!keepaliveSpecified && !antennaValueSpecified && !gpoSpecified)
        {
            error = "Specify at least one editable setting; config apply never sends an empty write.";
            return false;
        }
        if (settings.KeepaliveType is not null &&
            !settings.KeepaliveType.Equals("none", StringComparison.OrdinalIgnoreCase) &&
            !settings.KeepaliveType.Equals("periodic", StringComparison.OrdinalIgnoreCase))
        {
            error = "--keepalive-type must be none or periodic.";
            return false;
        }
        if (antennaValueSpecified && !settings.AntennaId.HasValue)
        {
            error = "--tx-power, --rx-sens, and --channel require --antenna <ID>.";
            return false;
        }
        if (settings.AntennaId.HasValue && !antennaValueSpecified)
        {
            error = "--antenna <ID> requires at least one of --tx-power, --rx-sens, or --channel.";
            return false;
        }
        if (settings.GpoPort.HasValue != settings.GpoData.HasValue)
        {
            error = "--gpo-port <PORT> and --gpo-data <true|false> must be specified together.";
            return false;
        }
        if (settings.TransmitPower is ushort transmitPower && capabilities?.TxPowers.Count > 0 &&
            !capabilities.TxPowers.Any(item => item.Index == transmitPower))
        {
            error = $"--tx-power {transmitPower} is not a transmit-power index reported by this reader. Run 'caps' to see valid index-to-dBm mappings.";
            return false;
        }
        if (settings.ReceiverSensitivity is ushort receiverSensitivity && capabilities?.RxSensitivities.Count > 0 &&
            !capabilities.RxSensitivities.Any(item => item.Index == receiverSensitivity))
        {
            error = $"--rx-sens {receiverSensitivity} is not a receiver-sensitivity index reported by this reader. Run 'caps' to see valid index-to-dBm mappings.";
            return false;
        }

        error = null;
        return true;
    }

    public static ReaderConfiguration BuildUpdatedConfiguration(
        ConfigApplySettings settings,
        ReaderConfiguration current)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(current);

        KeepaliveConfiguration keepalive = settings.KeepaliveType?.ToLowerInvariant() switch
        {
            "none" => new KeepaliveConfiguration { TriggerType = LlrpSdk.KeepaliveTriggerType.None, IntervalMs = 0 },
            "periodic" => new KeepaliveConfiguration
            {
                TriggerType = LlrpSdk.KeepaliveTriggerType.Periodic,
                IntervalMs = settings.KeepaliveInterval ?? current.Keepalive.IntervalMs
            },
            null when settings.KeepaliveInterval.HasValue => new KeepaliveConfiguration
            {
                TriggerType = LlrpSdk.KeepaliveTriggerType.Periodic,
                IntervalMs = settings.KeepaliveInterval.Value
            },
            null => current.Keepalive,
            _ => throw new CliUsageException("Invalid keepalive type: use none or periodic.")
        };

        var antennas = current.Antennas.ToList();
        if (settings.AntennaId is ushort antennaId)
        {
            int matchIndex = antennas.FindIndex(item => item.AntennaId == antennaId);
            AntennaConfigurationSettings original = matchIndex >= 0
                ? antennas[matchIndex]
                : new AntennaConfigurationSettings { AntennaId = antennaId };
            var updated = new AntennaConfigurationSettings
            {
                AntennaId = antennaId,
                IsConnected = original.IsConnected,
                Gain = original.Gain,
                TransmitPowerIndex = settings.TransmitPower ?? original.TransmitPowerIndex,
                ReceiverSensitivityIndex = settings.ReceiverSensitivity ?? original.ReceiverSensitivityIndex,
                ChannelIndex = settings.ChannelIndex ?? original.ChannelIndex
            };
            if (matchIndex >= 0)
            {
                antennas[matchIndex] = updated;
            }
            else
            {
                antennas.Add(updated);
            }
        }

        var gpos = current.Gpos.ToList();
        if (settings.GpoPort is ushort gpoPort)
        {
            int matchIndex = gpos.FindIndex(item => item.GpoPortNumber == gpoPort);
            var updated = new GpoConfiguration { GpoPortNumber = gpoPort, GpoData = settings.GpoData!.Value };
            if (matchIndex >= 0)
            {
                gpos[matchIndex] = updated;
            }
            else
            {
                gpos.Add(updated);
            }
        }

        return new ReaderConfiguration
        {
            Extensions = current.Extensions,
            Keepalive = keepalive,
            Antennas = antennas,
            Gpos = gpos,
            Gpis = current.Gpis,
            Events = current.Events
        };
    }
}
