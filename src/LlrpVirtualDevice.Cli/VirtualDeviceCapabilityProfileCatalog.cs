using LlrpDevice.Virtual.Hosting;

namespace LlrpVirtualDevice.Cli;

internal static class VirtualDeviceCapabilityProfileCatalog
{
    public static IReadOnlyList<VirtualDeviceProfileInfo> All => VirtualDeviceProfiles.All;

    public static VirtualDeviceProfileInfo Get(string id) => VirtualDeviceProfiles.Get(id);
}
