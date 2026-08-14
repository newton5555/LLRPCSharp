using LlrpSdk;
using LlrpSdk.Extensions.Impinj;
using LlrpSdk.Extensions.Seuic;
using LlrpSdk.Extensions.Zebra;
using Spectre.Console;

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
            error = "Vendor mode must be auto, impinj, seuic, zebra, or none.";
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

        switch (VendorMode)
        {
            case VendorExtensionMode.Auto:
                builder.UseImpinj().UseSeuic().UseZebra();
                break;
            case VendorExtensionMode.Impinj:
                builder.UseImpinj();
                break;
            case VendorExtensionMode.Seuic:
                builder.UseSeuic();
                break;
            case VendorExtensionMode.Zebra:
                builder.UseZebra();
                break;
        }

        return builder;
    }

    public void RenderVendorMode(IAnsiConsole console)
    {
        console.MarkupLine(VendorMode switch
        {
            VendorExtensionMode.None => "[grey]Vendor mode:[/] [yellow]disabled (pure standard LLRP mode)[/]",
            VendorExtensionMode.Impinj => "[grey]Vendor mode:[/] [springgreen2]forced Impinj mode[/]",
            VendorExtensionMode.Seuic => "[grey]Vendor mode:[/] [springgreen2]forced Seuic mode[/]",
            VendorExtensionMode.Zebra => "[grey]Vendor mode:[/] [springgreen2]forced Zebra mode[/]",
            _ => "[grey]Vendor mode:[/] [deepskyblue1]auto-detect (match on connect)[/]",
        });
    }
}
