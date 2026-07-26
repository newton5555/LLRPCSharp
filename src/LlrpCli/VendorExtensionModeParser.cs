namespace LlrpCli;

internal enum VendorExtensionMode
{
    Auto,
    Impinj,
    None,
}

internal static class VendorExtensionModeParser
{
    public static bool TryParse(string? value, out VendorExtensionMode mode)
    {
        if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            mode = VendorExtensionMode.Auto;
            return true;
        }

        if (string.Equals(value, "impinj", StringComparison.OrdinalIgnoreCase))
        {
            mode = VendorExtensionMode.Impinj;
            return true;
        }

        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            mode = VendorExtensionMode.None;
            return true;
        }

        mode = default;
        return false;
    }
}
