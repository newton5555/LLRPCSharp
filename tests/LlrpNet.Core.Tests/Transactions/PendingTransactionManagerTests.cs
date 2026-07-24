using LlrpNet.Core.Transactions;

namespace LlrpNet.Core.Tests.Transactions;

public sealed class PendingTransactionManagerTests
{
    [Fact]
    public async Task TryComplete_CompletesRegisteredTransaction()
    {
        using var manager = new PendingTransactionManager<string>();
        Task<string> response = manager.Register(17);

        bool completed = manager.TryComplete(17, "response");

        Assert.True(completed);
        Assert.Equal("response", await response);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void Register_WithDuplicateMessageIdThrows()
    {
        using var manager = new PendingTransactionManager<string>();
        _ = manager.Register(17);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = manager.Register(17);
            });

        Assert.Contains("17", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void TryCompleteAndTryFail_WithUnknownMessageIdReturnFalse()
    {
        using var manager = new PendingTransactionManager<string>();

        Assert.False(manager.TryComplete(999, "response"));
        Assert.False(manager.TryFail(999, new InvalidOperationException()));
    }

    [Fact]
    public async Task TryFail_FaultsRegisteredTransaction()
    {
        using var manager = new PendingTransactionManager<string>();
        Task<string> response = manager.Register(17);
        var failure = new InvalidOperationException("reader rejected request");

        bool failed = manager.TryFail(17, failure);

        Assert.True(failed);
        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await response);
        Assert.Same(failure, actual);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task Register_WithCallerCancellationCancelsAndRemovesTransaction()
    {
        using var manager = new PendingTransactionManager<string>();
        using var cancellation = new CancellationTokenSource();
        Task<string> response = manager.Register(17, cancellation.Token);

        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await response);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, manager.Count);

        Task<string> reused = manager.Register(17);
        Assert.True(manager.TryComplete(17, "reused"));
        Assert.Equal("reused", await reused);
    }

    [Fact]
    public async Task Register_WithAlreadyCanceledTokenReturnsCanceledTask()
    {
        using var manager = new PendingTransactionManager<string>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Task<string> response = manager.Register(17, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await response);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task Register_WhenTimeoutExpiresFaultsAndRemovesTransaction()
    {
        using var manager = new PendingTransactionManager<string>();

        Task<string> response = manager.Register(17, TimeSpan.Zero);

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(async () => await response);
        Assert.Contains("17", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task TimedOutIdentifier_IsQuarantinedUntilSafetyWindowExpires()
    {
        using var manager = new PendingTransactionManager<string>(TimeSpan.FromMilliseconds(50));
        Task<string> timedOut = manager.Register(17, TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<TimeoutException>(async () => await timedOut);
        await Task.Delay(20);

        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = manager.Register(17);
            });
        await Task.Delay(75);

        Task<string> reused = manager.Register(17);
        Assert.True(manager.TryComplete(17, "reused"));
        Assert.Equal("reused", await reused);
    }

    [Fact]
    public void Register_WithInvalidTimeoutThrowsWithoutAddingTransaction()
    {
        using var manager = new PendingTransactionManager<string>();

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = manager.Register(17, TimeSpan.FromMilliseconds(-2));
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = manager.Register(18, TimeSpan.FromDays(100));
            });
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task CompletedTransactionDisposesCancellationRegistration()
    {
        using var manager = new PendingTransactionManager<string>();
        using var oldCancellation = new CancellationTokenSource();
        Task<string> first = manager.Register(17, oldCancellation.Token);
        Assert.True(manager.TryComplete(17, "first"));
        Assert.Equal("first", await first);

        Task<string> reused = manager.Register(17);
        oldCancellation.Cancel();

        Assert.False(reused.IsCompleted);
        Assert.True(manager.TryComplete(17, "second"));
        Assert.Equal("second", await reused);
    }

    [Fact]
    public async Task CompletedTransactionDisposesTimeoutTimer()
    {
        using var manager = new PendingTransactionManager<string>();
        Task<string> first = manager.Register(17, TimeSpan.FromMilliseconds(50));
        Assert.True(manager.TryComplete(17, "first"));
        Assert.Equal("first", await first);

        Task<string> reused = manager.Register(17);
        await Task.Delay(150);

        Assert.False(reused.IsCompleted);
        Assert.True(manager.TryComplete(17, "second"));
        Assert.Equal("second", await reused);
    }

    [Fact]
    public async Task FailAll_FaultsAllPendingTransactionsAndAllowsNewRegistrations()
    {
        using var manager = new PendingTransactionManager<string>();
        Task<string> first = manager.Register(1);
        Task<string> second = manager.Register(2);
        var failure = new IOException("connection closed");

        int failedCount = manager.FailAll(failure);

        Assert.Equal(2, failedCount);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(async () => await first));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(async () => await second));
        Assert.Equal(0, manager.Count);

        Task<string> next = manager.Register(3);
        Assert.True(manager.TryComplete(3, "connected again"));
        Assert.Equal("connected again", await next);
    }

    [Fact]
    public async Task Dispose_FaultsPendingTransactionsAndRejectsRegistration()
    {
        var manager = new PendingTransactionManager<string>();
        Task<string> response = manager.Register(17);

        manager.Dispose();
        manager.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await response);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = manager.Register(18);
        });
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task ConcurrentCompletionOnlyOneCallerWins()
    {
        using var manager = new PendingTransactionManager<int>();
        Task<int> response = manager.Register(17);

        Task<bool>[] attempts = Enumerable.Range(0, 64)
            .Select(value => Task.Run(() => manager.TryComplete(17, value)))
            .ToArray();
        bool[] results = await Task.WhenAll(attempts);

        Assert.Single(results, static result => result);
        int value = await response;
        Assert.InRange(value, 0, 63);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task CompletionCancellationAndFailureRaceCompletesExactlyOnce()
    {
        for (uint messageId = 1; messageId <= 100; messageId++)
        {
            using var manager = new PendingTransactionManager<int>();
            using var cancellation = new CancellationTokenSource();
            Task<int> response = manager.Register(messageId, cancellation.Token);
            var failure = new IOException("connection lost");

            Task cancel = Task.Run(cancellation.Cancel);
            Task<bool> complete = Task.Run(() => manager.TryComplete(messageId, 42));
            Task<bool> fail = Task.Run(() => manager.TryFail(messageId, failure));
            await Task.WhenAll(cancel, complete, fail);
            bool completeWon = await complete;
            bool failWon = await fail;

            try
            {
                _ = await response;
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }

            Assert.InRange(new[] { completeWon, failWon }.Count(static result => result), 0, 1);
            Assert.True(response.IsCompleted);
            Assert.Equal(0, manager.Count);
        }
    }

    [Fact]
    public async Task CompletionDoesNotRunContinuationInline()
    {
        using var manager = new PendingTransactionManager<string>();
        Task<string> response = manager.Register(17);
        using var releaseContinuation = new ManualResetEventSlim();
        Task continuation = response.ContinueWith(
            _ => releaseContinuation.Wait(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        Task<bool> completion = Task.Run(() => manager.TryComplete(17, "response"));
        try
        {
            Assert.True(await completion.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            releaseContinuation.Set();
        }

        await continuation;
    }
}
