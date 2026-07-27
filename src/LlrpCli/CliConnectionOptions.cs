using Spectre.Console;
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

namespace LlrpCli;

internal sealed record CliConnectionOptions(
    string Host,
    int Port,
    LlrpProtocolVersionPolicy ProtocolVersionPolicy,
    VendorExtensionMode VendorMode)
{
    public static bool TryCreate(
        string host,
        int port,
        string? llrpVersion,
        string? vendor,
        out CliConnectionOptions options,
        out string error)
    {
        options = default!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(host))
        {
            error = "Host is required.";
            return false;
        }

        if (!ProtocolVersionPolicyParser.TryParse(llrpVersion, out LlrpProtocolVersionPolicy policy))
        {
            error = "LLRP version must be auto, 1.0.1, or 1.1.";
            return false;
        }

        if (!VendorExtensionModeParser.TryParse(vendor, out VendorExtensionMode vendorMode))
        {
            error = "Vendor mode must be auto, impinj, or none.";
            return false;
        }

        options = new CliConnectionOptions(host, port, policy, vendorMode);
        return true;
    }

    public LlrpReaderBuilder CreateReaderBuilder()
    {
        var builder = LlrpReader.CreateBuilder(Host)
            .WithPort(Port)
            .WithProtocolVersionPolicy(ProtocolVersionPolicy);

        if (VendorMode != VendorExtensionMode.None)
        {
            builder.UseImpinj();
        }

        return builder;
    }

    public void RenderVendorMode(IAnsiConsole console)
    {
        console.MarkupLine(VendorMode == VendorExtensionMode.None
            ? "[grey]Vendor extensions:[/] [yellow]disabled (pure standard LLRP mode)[/]"
            : "[grey]Vendor extensions:[/] [springgreen2]Impinj enabled[/]");
    }
}
