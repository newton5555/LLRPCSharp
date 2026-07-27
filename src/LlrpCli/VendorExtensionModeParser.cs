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
        string normalized = value?.Trim().ToLowerInvariant() ?? "auto";
        if (normalized is "" or "auto")
        {
            mode = VendorExtensionMode.Auto;
            return true;
        }

        if (normalized == "impinj")
        {
            mode = VendorExtensionMode.Impinj;
            return true;
        }

        if (normalized == "none")
        {
            mode = VendorExtensionMode.None;
            return true;
        }

        mode = default;
        return false;
    }
}
