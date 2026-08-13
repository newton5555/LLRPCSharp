using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>
/// Validated, host-neutral input for a standard C1G2 tag access command.
/// </summary>
internal sealed record TagAccessCliRequest(
    ReadOnlyMemory<byte> Epc,
    TagMemoryBank MemoryBank,
    ushort WordPointer,
    ushort AntennaId,
    string AccessPassword,
    TimeSpan? Timeout)
{
    public static TagAccessCliRequest Create(
        string epc,
        string bank,
        ushort wordPointer,
        ushort antennaId,
        string? password,
        uint? timeoutSeconds)
    {
        byte[] epcBytes = ParseHex(epc, "EPC");
        if (epcBytes.Length == 0)
        {
            throw new CliUsageException("EPC must contain at least one byte.");
        }

        TagMemoryBank memoryBank = ParseBank(bank);

        string accessPassword = NormalizeHex32(password, "--password");
        TimeSpan? timeout = timeoutSeconds is null ? null : TimeSpan.FromSeconds(timeoutSeconds.Value);
        return new TagAccessCliRequest(epcBytes, memoryBank, wordPointer, antennaId, accessPassword, timeout);
    }

    public static TagMemoryBank ParseBank(string bank) => bank.ToLowerInvariant() switch
    {
        "reserved" or "0" => TagMemoryBank.Reserved,
        "epc" or "electronicproductcode" or "1" => TagMemoryBank.ElectronicProductCode,
        "tid" or "2" => TagMemoryBank.Tid,
        "user" or "3" => TagMemoryBank.User,
        _ => throw new CliUsageException("Memory bank must be reserved (0), epc (1), tid (2), or user (3)."),
    };

    public ReadTagRequest ToReadRequest(ushort wordCount)
    {
        if (wordCount == 0)
        {
            throw new CliUsageException("--count must be greater than zero.");
        }

        return new ReadTagRequest
        {
            Selection = CreateSelection(),
            AntennaId = AntennaId,
            AccessPassword = AccessPassword,
            MemoryBank = MemoryBank,
            WordPointer = WordPointer,
            WordCount = wordCount,
        };
    }

    public WriteTagRequest ToWriteRequest(IReadOnlyList<ushort> words)
    {
        if (words.Count == 0)
        {
            throw new CliUsageException("--data must contain at least one 16-bit word.");
        }

        return new WriteTagRequest
        {
            Selection = CreateSelection(),
            AntennaId = AntennaId,
            AccessPassword = AccessPassword,
            MemoryBank = MemoryBank,
            WordPointer = WordPointer,
            WriteData = words,
        };
    }

    public KillTagRequest ToKillRequest(string killPassword)
    {
        return new KillTagRequest
        {
            Selection = CreateSelection(),
            AntennaId = AntennaId,
            KillPassword = killPassword,
        };
    }

    public BlockEraseTagRequest ToBlockEraseRequest(ushort wordCount)
    {
        if (wordCount == 0)
        {
            throw new CliUsageException("--count must be greater than zero.");
        }

        return new BlockEraseTagRequest
        {
            Selection = CreateSelection(),
            AntennaId = AntennaId,
            AccessPassword = AccessPassword,
            MemoryBank = MemoryBank,
            WordPointer = WordPointer,
            WordCount = wordCount,
        };
    }

    public static IReadOnlyList<ushort> ParseWords(string hex)
    {
        byte[] bytes = ParseHex(hex, "--data");
        if (bytes.Length == 0 || bytes.Length % 2 != 0)
        {
            throw new CliUsageException("--data must contain complete 16-bit words.");
        }

        return Enumerable.Range(0, bytes.Length / 2)
            .Select(index => (ushort)((bytes[index * 2] << 8) | bytes[(index * 2) + 1]))
            .ToArray();
    }

    public TagSelection CreateSelection() => new()
    {
        MemoryBank = TagMemoryBank.ElectronicProductCode,
        BitPointer = 32,
        BitLength = checked((ushort)(Epc.Length * 8)),
        Mask = Epc,
        Data = Epc,
    };

    public static byte[] ParseHex(string value, string name)
    {
        string normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal);
        if (normalized.Length % 2 != 0)
        {
            throw new CliUsageException($"{name} must be an even-length hexadecimal value.");
        }
        try
        {
            return Convert.FromHexString(normalized);
        }
        catch (FormatException)
        {
            throw new CliUsageException($"{name} must be an even-length hexadecimal value.");
        }
    }

    public static string NormalizeHex32(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "00000000";
        }

        string normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        normalized = normalized
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal);
        if (!uint.TryParse(normalized, System.Globalization.NumberStyles.AllowHexSpecifier, null, out uint parsed))
        {
            throw new CliUsageException($"{name} must be a UInt32 hexadecimal value.");
        }

        return parsed.ToString("X8");
    }
}
