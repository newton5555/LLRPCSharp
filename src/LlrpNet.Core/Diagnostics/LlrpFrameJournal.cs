namespace LlrpNet.Core.Diagnostics;

/// <summary>
/// Keeps a bounded, thread-safe in-memory journal of exact LLRP frames observed at the transport boundary.
/// </summary>
/// <remarks>
/// Every observed frame is copied before this observer completes, so snapshots remain valid after the transport
/// returns or reuses its receive buffer. When capacity is reached, the oldest frame is discarded.
/// </remarks>
public sealed class LlrpFrameJournal : ILlrpFrameObserver
{
    private readonly LlrpCapturedFrame?[] frames;
    private readonly object gate = new();
    private int count;
    private int next;

    /// <summary>Creates a journal that retains at most <paramref name="capacity"/> frames.</summary>
    /// <param name="capacity">The maximum number of frames retained in chronological order.</param>
    public LlrpFrameJournal(int capacity = 256)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The journal capacity must be positive.");
        }

        frames = new LlrpCapturedFrame[capacity];
    }

    /// <summary>Gets the maximum number of frames retained by this journal.</summary>
    public int Capacity => frames.Length;

    /// <summary>Gets the current number of retained frames.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return count;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask ObserveAsync(
        LlrpFrameObservation observation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = new LlrpCapturedFrame(
            observation.Direction,
            observation.Timestamp,
            observation.ConnectionId,
            observation.ConnectionGeneration,
            observation.FrameBytes.ToArray());

        lock (gate)
        {
            frames[next] = captured;
            next = (next + 1) % frames.Length;
            if (count < frames.Length)
            {
                count++;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Returns a chronological snapshot of retained frames.
    /// </summary>
    /// <remarks>Each returned frame owns a separate byte-array copy and may be retained by the caller.</remarks>
    public IReadOnlyList<LlrpCapturedFrame> Snapshot()
    {
        lock (gate)
        {
            var snapshot = new LlrpCapturedFrame[count];
            int first = (next - count + frames.Length) % frames.Length;
            for (int index = 0; index < count; index++)
            {
                LlrpCapturedFrame frame = frames[(first + index) % frames.Length]
                    ?? throw new InvalidOperationException("The frame journal contains an incomplete entry.");
                snapshot[index] = frame with { FrameBytes = frame.FrameBytes.ToArray() };
            }

            return Array.AsReadOnly(snapshot);
        }
    }

    /// <summary>Removes all retained frames.</summary>
    public void Clear()
    {
        lock (gate)
        {
            Array.Clear(frames);
            count = 0;
            next = 0;
        }
    }
}

/// <summary>Represents one durable frame copied by <see cref="LlrpFrameJournal"/>.</summary>
public sealed record LlrpCapturedFrame(
    LlrpFrameDirection Direction,
    DateTimeOffset Timestamp,
    string ConnectionId,
    long ConnectionGeneration,
    byte[] FrameBytes);
