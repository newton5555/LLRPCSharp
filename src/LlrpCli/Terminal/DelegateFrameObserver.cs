using LlrpNet.Core.Diagnostics;

namespace LlrpCli.Terminal;

public sealed record CapturedFrame(
    LlrpFrameDirection Direction,
    DateTimeOffset Timestamp,
    byte[] Bytes);

public sealed class DelegateFrameObserver : ILlrpFrameObserver
{
    private readonly Action<CapturedFrame> _onObserve;
    private readonly List<CapturedFrame> _capturedFrames = new();
    private readonly object _lock = new();

    public DelegateFrameObserver(Action<CapturedFrame> onObserve)
    {
        _onObserve = onObserve;
    }

    public IReadOnlyList<CapturedFrame> CapturedFrames
    {
        get
        {
            lock (_lock)
            {
                return _capturedFrames.ToArray();
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _capturedFrames.Clear();
        }
    }

    public ValueTask ObserveAsync(LlrpFrameObservation observation, CancellationToken cancellationToken = default)
    {
        var frame = new CapturedFrame(
            observation.Direction,
            observation.Timestamp,
            observation.FrameBytes.ToArray());

        lock (_lock)
        {
            _capturedFrames.Add(frame);
        }

        try
        {
            _onObserve(frame);
        }
        catch { }

        return ValueTask.CompletedTask;
    }
}
