namespace LlrpNet.Core.Transactions;

/// <summary>
/// Generates non-zero LLRP message identifiers for a single logical channel.
/// </summary>
/// <remarks>
/// The generator is thread-safe. Identifiers wrap from <see cref="uint.MaxValue"/> to one;
/// callers that keep transactions outstanding across a complete identifier cycle must still
/// reject identifiers that are already in use.
/// </remarks>
public sealed class LlrpMessageIdGenerator
{
    private int current;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlrpMessageIdGenerator"/> class.
    /// </summary>
    /// <param name="initialValue">
    /// The last identifier considered issued. The first generated identifier is its non-zero successor.
    /// </param>
    public LlrpMessageIdGenerator(uint initialValue = 0)
    {
        current = unchecked((int)initialValue);
    }

    /// <summary>
    /// Returns the next non-zero message identifier.
    /// </summary>
    /// <returns>A non-zero unsigned 32-bit message identifier.</returns>
    public uint Next()
    {
        uint messageId;
        do
        {
            messageId = unchecked((uint)Interlocked.Increment(ref current));
        }
        while (messageId == 0);

        return messageId;
    }
}
