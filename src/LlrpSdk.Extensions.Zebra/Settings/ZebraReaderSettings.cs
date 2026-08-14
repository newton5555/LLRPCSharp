namespace LlrpSdk.Extensions.Zebra;

/// <summary>
/// High-level writable Zebra reader configuration. This is a hand-written extension model, not a generated
/// LLRP parameter: <see cref="Registration.ZebraReaderExtension"/> compiles it to the generated custom
/// parameters (MotoRadioPowerState, MotoPersistenceSaveParams, and so on).
/// </summary>
public sealed record ZebraReaderConfiguration
{
    public const string ExtensionKey = "zebra.configuration";

    /// <summary>Gets the radio power state (true = on, false = off) or null when not requested.</summary>
    public bool? RadioPowerState { get; init; }

    /// <summary>Gets the radio transmit delay value or null when not requested.</summary>
    public byte? RadioTransmitDelay { get; init; }

    /// <summary>Gets the autonomous mode state or null when not requested.</summary>
    public bool? AutonomousModeState { get; init; }

    /// <summary>Gets whether the reader should save its configuration on apply.</summary>
    public bool? SaveConfiguration { get; init; }

    /// <summary>Gets whether the reader should save tag data.</summary>
    public bool? SaveTagData { get; init; }

    /// <summary>Gets whether the reader should save tag event data.</summary>
    public bool? SaveTagEventData { get; init; }

    /// <summary>Gets whether NXP set/reset-quiet custom commands are enabled.</summary>
    public bool? EnableNxpSetAndResetQuietCommands { get; init; }
}
