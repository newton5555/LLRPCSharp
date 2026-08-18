using LlrpNet.Protocol.Parameters;
using ImpinjEnumerations = LlrpNet.Protocol.Impinj.Enumerations.V1_0_1;
using ImpinjParameters = LlrpNet.Protocol.Impinj.Parameters.V1_0_1;

namespace LlrpDevice.Virtual.Impinj;

/// <summary>Captured R420 vendor parameters used by the virtual profile.</summary>
public static class ImpinjVirtualDeviceDefaults
{
    public static IReadOnlyList<ILlrpParameter> CreateCapabilities() =>
    [
        new ImpinjParameters.ImpinjDetailedVersion(
            "Speedway R420",
            "virtual-r420",
            "6.4.1.240",
            "6.0.3.240",
            "6.2.2.8",
            "290-006-006",
            null,
            [],
            null,
            []),
        new ImpinjParameters.ImpinjFrequencyCapabilities(
            [
                920_625, 920_875, 921_125, 921_375,
                921_625, 921_875, 922_125, 922_375,
                922_625, 922_875, 923_125, 923_375,
                923_625, 923_875, 924_125, 924_375,
            ],
            []),
    ];

    public static IReadOnlyList<ILlrpParameter> CreateReaderConfiguration() =>
    [
        new ImpinjParameters.ImpinjSubRegulatoryRegion(
            ImpinjEnumerations.ImpinjRegulatoryRegion.China_920_925_MHz,
            []),
        new ImpinjParameters.ImpinjReaderTemperature(35, []),
        ..Enumerable.Range(1, 4).Select(static port =>
            (ILlrpParameter)new ImpinjParameters.ImpinjGPIDebounceConfiguration(checked((ushort)port), 0, [])),
        new ImpinjParameters.ImpinjLinkMonitorConfiguration(
            ImpinjEnumerations.ImpinjLinkMonitorMode.Disabled,
            0,
            []),
        new ImpinjParameters.ImpinjReportBufferConfiguration(
            ImpinjEnumerations.ImpinjReportBufferMode.Normal,
            []),
        new ImpinjParameters.ImpinjAccessSpecConfiguration(
            new ImpinjParameters.ImpinjBlockWriteWordCount(1, []),
            new ImpinjParameters.ImpinjOpSpecRetryCount(0, []),
            new ImpinjParameters.ImpinjAccessSpecOrdering(
                ImpinjEnumerations.ImpinjAccessSpecOrderingMode.FIFO,
                []),
            []),
    ];
}
