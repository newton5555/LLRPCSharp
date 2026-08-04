using Microsoft.Extensions.Configuration;

namespace LlrpSdk.Hardware.Tests;

public sealed record HardwareTestConfig
{
    public bool Enabled { get; init; }
    public TargetReaderConfig TargetReader { get; init; } = new();
}

public sealed record TargetReaderConfig
{
    public string Ip { get; init; } = "192.168.1.100";
    public int Port { get; init; } = 5084;
    public string Vendor { get; init; } = "Standard";
    public IReadOnlyList<ushort> Antennas { get; init; } = [];
    public bool SupportsImpinjExtensions { get; init; }
}

public static class HardwareTestEnvironment
{
    public static HardwareTestConfig Config { get; }

    static HardwareTestEnvironment()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "LLRP_HW_TEST_")
            .Build();

        Config = configuration.GetSection("HardwareTest").Get<HardwareTestConfig>() ?? new HardwareTestConfig();
    }

    public static string? SkipReason => Config.Enabled
        ? null
        : "Hardware tests are disabled. Set 'HardwareTest:Enabled' to true in appsettings.local.json or environment variables.";
}
