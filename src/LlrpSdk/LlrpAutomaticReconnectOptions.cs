namespace LlrpSdk;

/// <summary>Configures bounded retry after an unexpected connected-session failure.</summary>
/// <remarks>
/// Automatic reconnect is disabled unless an instance is supplied to <see cref="LlrpReaderBuilder"/>. It restores
/// the transport session and standard capability metadata; it deliberately does not recreate managed ROSpec or
/// AccessSpec resources because their remote state must be reconciled explicitly.
/// </remarks>
public sealed class LlrpAutomaticReconnectOptions
{
    /// <summary>Creates a bounded exponential-backoff reconnect policy.</summary>
    /// <param name="maximumAttempts">The number of reconnect attempts after each unexpected disconnect.</param>
    /// <param name="initialDelay">The delay before the first attempt.</param>
    /// <param name="maximumDelay">The upper bound on a retry delay.</param>
    public LlrpAutomaticReconnectOptions(
        int maximumAttempts = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maximumDelay = null)
    {
        MaximumAttempts = maximumAttempts;
        InitialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        MaximumDelay = maximumDelay ?? TimeSpan.FromSeconds(10);

        if (MaximumAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts), maximumAttempts, "The maximum attempts must be positive.");
        }
        if (InitialDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDelay), InitialDelay, "The initial delay must be positive.");
        }
        if (MaximumDelay < InitialDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelay), MaximumDelay, "The maximum delay cannot be less than the initial delay.");
        }
    }

    /// <summary>Gets the maximum reconnect attempts following one unexpected failure.</summary>
    public int MaximumAttempts { get; }

    /// <summary>Gets the delay before the first reconnect attempt.</summary>
    public TimeSpan InitialDelay { get; }

    /// <summary>Gets the maximum exponential-backoff delay.</summary>
    public TimeSpan MaximumDelay { get; }

    internal TimeSpan GetDelay(int attempt)
    {
        double multiplier = Math.Pow(2, attempt - 1);
        double milliseconds = Math.Min(MaximumDelay.TotalMilliseconds, InitialDelay.TotalMilliseconds * multiplier);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
