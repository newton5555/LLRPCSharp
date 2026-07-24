using LlrpSdk;

namespace LlrpCli;

internal static class ProtocolVersionPolicyParser
{
    public static bool TryParse(string? value, out LlrpProtocolVersionPolicy policy)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? "auto";
        policy = normalized switch
        {
            "" or "auto" => LlrpProtocolVersionPolicy.Auto,
            "1" or "1.0.1" or "1.0" or "101" => LlrpProtocolVersionPolicy.Force101,
            "2" or "1.1" or "11" => LlrpProtocolVersionPolicy.Force11,
            _ => default,
        };

        return normalized is "" or "auto" or "1" or "1.0.1" or "1.0" or "101" or "2" or "1.1" or "11";
    }
}
