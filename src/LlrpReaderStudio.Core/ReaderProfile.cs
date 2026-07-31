namespace LlrpReaderStudio.Core;

public sealed record ReaderProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Reader";
    public required string Host { get; init; }
    public int Port { get; init; } = 5084;
    public bool EnableImpinjExtensions { get; init; } = true;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);
        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "The LLRP port must be from 1 through 65535.");
        }
    }
}

public enum StudioReaderState
{
    Disconnected,
    Connecting,
    Connected,
    Inventorying,
    Faulted,
}

public sealed record ReaderStatus(
    ReaderProfile Profile,
    StudioReaderState State,
    string? Model,
    string? Firmware,
    string? Error);
