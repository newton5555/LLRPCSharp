using LlrpNet.Core.Diagnostics;

namespace LlrpNet.Core.Tests.Diagnostics;

public sealed class LlrpFrameObserverTests
{
    [Fact]
    public void Observation_BorrowsItsBackingMemory()
    {
        byte[] bytes = [0x01, 0x02];
        LlrpFrameObservation observation = CreateObservation(bytes);

        bytes[0] = 0xAA;

        Assert.Equal(0xAA, observation.FrameBytes.Span[0]);
    }

    [Fact]
    public async Task NullObserver_WithCancellation_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NullLlrpFrameObserver.Instance
                .ObserveAsync(CreateObservation(), cancellationSource.Token)
                .AsTask());

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task CompositeObserver_AwaitsObserversInRegistrationOrder()
    {
        var events = new List<string>();
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new CallbackObserver(async (_, _) =>
        {
            events.Add("first-start");
            firstEntered.SetResult(true);
            await releaseFirst.Task;
            events.Add("first-end");
        });
        var second = new CallbackObserver((_, _) =>
        {
            events.Add("second");
            return ValueTask.CompletedTask;
        });
        var composite = new CompositeLlrpFrameObserver(first, second);

        Task observationTask = composite.ObserveAsync(CreateObservation()).AsTask();
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["first-start"], events);

        releaseFirst.SetResult(true);
        await observationTask;

        Assert.Equal(["first-start", "first-end", "second"], events);
    }

    [Fact]
    public async Task CompositeObserver_WhenCancellationOccurs_StopsBeforeNextObserver()
    {
        using var cancellationSource = new CancellationTokenSource();
        bool secondWasInvoked = false;
        var first = new CallbackObserver((_, _) =>
        {
            cancellationSource.Cancel();
            return ValueTask.CompletedTask;
        });
        var second = new CallbackObserver((_, _) =>
        {
            secondWasInvoked = true;
            return ValueTask.CompletedTask;
        });
        var composite = new CompositeLlrpFrameObserver(first, second);

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => composite.ObserveAsync(CreateObservation(), cancellationSource.Token).AsTask());

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.False(secondWasInvoked);
    }

    [Fact]
    public async Task CompositeObserver_WhenObserverThrows_PropagatesAfterInvokingLaterObservers()
    {
        var expected = new InvalidOperationException("Capture failed.");
        bool secondWasInvoked = false;
        var first = new CallbackObserver((_, _) => ValueTask.FromException(expected));
        var second = new CallbackObserver((_, _) =>
        {
            secondWasInvoked = true;
            return ValueTask.CompletedTask;
        });
        var composite = new CompositeLlrpFrameObserver(first, second);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => composite.ObserveAsync(CreateObservation()).AsTask());

        Assert.Same(expected, actual);
        Assert.True(secondWasInvoked);
    }

    [Fact]
    public async Task CompositeObserver_WhenMultipleObserversThrow_AggregatesAfterFullDelivery()
    {
        var firstFailure = new InvalidOperationException("Capture failed.");
        var secondFailure = new IOException("Log failed.");
        bool finalWasInvoked = false;
        var composite = new CompositeLlrpFrameObserver(
            new CallbackObserver((_, _) => ValueTask.FromException(firstFailure)),
            new CallbackObserver((_, _) => ValueTask.FromException(secondFailure)),
            new CallbackObserver((_, _) =>
            {
                finalWasInvoked = true;
                return ValueTask.CompletedTask;
            }));

        AggregateException actual = await Assert.ThrowsAsync<AggregateException>(
            () => composite.ObserveAsync(CreateObservation()).AsTask());

        Assert.Equal([firstFailure, secondFailure], actual.InnerExceptions);
        Assert.True(finalWasInvoked);
    }

    private static LlrpFrameObservation CreateObservation(ReadOnlyMemory<byte> bytes = default)
    {
        return new LlrpFrameObservation(
            LlrpFrameDirection.Receive,
            DateTimeOffset.Parse("2026-07-23T12:34:56+08:00"),
            "reader-1",
            bytes);
    }

    private sealed class CallbackObserver(
        Func<LlrpFrameObservation, CancellationToken, ValueTask> callback) : ILlrpFrameObserver
    {
        public ValueTask ObserveAsync(
            LlrpFrameObservation observation,
            CancellationToken cancellationToken = default)
        {
            return callback(observation, cancellationToken);
        }
    }
}
