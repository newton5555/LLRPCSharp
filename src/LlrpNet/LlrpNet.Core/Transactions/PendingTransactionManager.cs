using System.Collections.Concurrent;

namespace LlrpNet.Core.Transactions;

/// <summary>
/// Correlates response values with pending requests by LLRP message identifier.
/// </summary>
/// <typeparam name="T">The response value type.</typeparam>
/// <remarks>
/// This type is thread-safe. Every registered transaction is completed at most once, and task
/// continuations are always scheduled asynchronously.
/// </remarks>
public sealed class PendingTransactionManager<T> : IDisposable
{
    private static readonly TimeSpan MaximumTimerTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
    private readonly ConcurrentDictionary<uint, PendingTransaction> pending = new();
    private readonly Dictionary<uint, long> retiredIdentifiers = [];
    private readonly Queue<RetiredIdentifier> retiredIdentifierExpirations = new();
    private readonly object lifecycleGate = new();
    private readonly long identifierReuseQuarantineTimestampTicks;
    private bool disposed;

    /// <summary>
    /// Initializes a transaction manager with an optional timed-out/cancelled identifier reuse quarantine.
    /// </summary>
    /// <param name="identifierReuseQuarantine">
    /// The duration for which a timed-out or cancelled identifier remains unavailable. The default disables
    /// quarantine; a session supplies a non-zero production safety window.
    /// </param>
    public PendingTransactionManager(TimeSpan identifierReuseQuarantine = default)
    {
        if (identifierReuseQuarantine < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(identifierReuseQuarantine),
                identifierReuseQuarantine,
                "The identifier reuse quarantine cannot be negative.");
        }

        double timestampTicks = identifierReuseQuarantine.TotalSeconds *
            System.Diagnostics.Stopwatch.Frequency;
        identifierReuseQuarantineTimestampTicks = timestampTicks >= long.MaxValue
            ? long.MaxValue
            : (long)Math.Ceiling(timestampTicks);
    }

    /// <summary>
    /// Gets a snapshot of the number of transactions currently awaiting a response.
    /// </summary>
    public int Count => pending.Count;

    /// <summary>
    /// Registers a transaction that has no manager-enforced timeout.
    /// </summary>
    /// <param name="messageId">The message identifier used to correlate the response.</param>
    /// <param name="cancellationToken">A token that cancels this transaction.</param>
    /// <returns>A task that represents the pending response.</returns>
    /// <exception cref="InvalidOperationException">The message identifier is already registered.</exception>
    /// <exception cref="ObjectDisposedException">The manager has been disposed.</exception>
    public Task<T> Register(uint messageId, CancellationToken cancellationToken = default)
    {
        return Register(messageId, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    /// <summary>
    /// Registers a transaction with a timeout and optional caller cancellation.
    /// </summary>
    /// <param name="messageId">The message identifier used to correlate the response.</param>
    /// <param name="timeout">
    /// The maximum time to await a response, or <see cref="Timeout.InfiniteTimeSpan"/> for no timeout.
    /// </param>
    /// <param name="cancellationToken">A token that cancels this transaction.</param>
    /// <returns>A task that represents the pending response.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is negative and is not infinite.</exception>
    /// <exception cref="InvalidOperationException">The message identifier is already registered.</exception>
    /// <exception cref="ObjectDisposedException">The manager has been disposed.</exception>
    public Task<T> Register(
        uint messageId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeout);

        PendingTransactionRegistration registration = RegisterDeferred(
            messageId,
            acceptancePredicate: null,
            cancellationToken);
        try
        {
            registration.ArmTimeout(timeout);
            return registration.ResponseTask;
        }
        catch
        {
            registration.Abandon();
            throw;
        }
    }

    /// <summary>
    /// Registers a transaction whose response timeout will be armed explicitly after its request is committed.
    /// </summary>
    /// <remarks>
    /// This is used by the session so a slow send does not consume the response timeout or allow a request to be
    /// transmitted after that timeout has already elapsed. The optional predicate is evaluated before removal;
    /// a rejected value leaves the transaction pending.
    /// </remarks>
    internal PendingTransactionRegistration RegisterDeferred(
        uint messageId,
        Predicate<T>? acceptancePredicate,
        CancellationToken cancellationToken = default)
    {

        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (IsIdentifierRetired(messageId, out TimeSpan remaining))
            {
                throw new InvalidOperationException(
                    $"Message identifier {messageId} is quarantined for approximately {remaining} " +
                    "after a timed-out or cancelled transaction.");
            }

            var transaction = new PendingTransaction(this, messageId, acceptancePredicate);
            if (!pending.TryAdd(messageId, transaction))
            {
                throw new InvalidOperationException(
                    $"A transaction with message identifier {messageId} is already pending.");
            }

            try
            {
                transaction.ArmCancellation(cancellationToken);
            }
            catch
            {
                pending.TryRemove(KeyValuePair.Create(messageId, transaction));
                transaction.Abandon();
                throw;
            }

            return new PendingTransactionRegistration(this, messageId, transaction);
        }
    }

    /// <summary>
    /// Attempts to complete the transaction registered for a message identifier.
    /// </summary>
    /// <param name="messageId">The response message identifier.</param>
    /// <param name="result">The response value.</param>
    /// <returns><see langword="true"/> when a pending transaction was completed; otherwise, <see langword="false"/>.</returns>
    public bool TryComplete(uint messageId, T result)
    {
        if (!pending.TryGetValue(messageId, out PendingTransaction? transaction)
            || !transaction.Accepts(result)
            || !pending.TryRemove(KeyValuePair.Create(messageId, transaction)))
        {
            return false;
        }

        transaction.Complete(result);
        return true;
    }

    /// <summary>
    /// Attempts to fail the transaction registered for a message identifier.
    /// </summary>
    /// <param name="messageId">The request message identifier.</param>
    /// <param name="exception">The failure reported to the awaiting caller.</param>
    /// <returns><see langword="true"/> when a pending transaction was failed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public bool TryFail(uint messageId, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!pending.TryRemove(messageId, out PendingTransaction? transaction))
        {
            return false;
        }

        transaction.Fail(exception);
        return true;
    }

    private bool TryFail(
        uint messageId,
        PendingTransaction transaction,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!pending.TryRemove(KeyValuePair.Create(messageId, transaction)))
        {
            return false;
        }

        transaction.Fail(exception);
        return true;
    }

    private void Abandon(uint messageId, PendingTransaction transaction)
    {
        if (pending.TryRemove(KeyValuePair.Create(messageId, transaction)))
        {
            transaction.Abandon();
        }
    }

    /// <summary>
    /// Fails every transaction that is pending when this method acquires the manager lifecycle lock.
    /// </summary>
    /// <param name="exception">The failure reported to each awaiting caller.</param>
    /// <returns>The number of transactions removed and failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public int FailAll(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return RemoveAndFailAll(exception, markDisposed: false);
    }

    /// <summary>
    /// Fails all pending transactions and prevents subsequent registrations.
    /// </summary>
    public void Dispose()
    {
        var exception = new ObjectDisposedException(GetType().Name);
        RemoveAndFailAll(exception, markDisposed: true);
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if ((timeout < TimeSpan.Zero || timeout > MaximumTimerTimeout) &&
            timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                $"The timeout must be non-negative, no greater than {MaximumTimerTimeout}, or Timeout.InfiniteTimeSpan.");
        }
    }

    private int RemoveAndFailAll(Exception exception, bool markDisposed)
    {
        List<PendingTransaction> removed = [];

        lock (lifecycleGate)
        {
            if (markDisposed)
            {
                if (disposed)
                {
                    return 0;
                }

                disposed = true;
                retiredIdentifiers.Clear();
                retiredIdentifierExpirations.Clear();
            }

            foreach (KeyValuePair<uint, PendingTransaction> pair in pending)
            {
                if (pending.TryRemove(pair))
                {
                    removed.Add(pair.Value);
                }
            }
        }

        foreach (PendingTransaction transaction in removed)
        {
            transaction.Fail(exception);
        }

        return removed.Count;
    }

    private void Cancel(uint messageId, PendingTransaction transaction, CancellationToken cancellationToken)
    {
        bool removed;
        lock (lifecycleGate)
        {
            removed = pending.TryRemove(KeyValuePair.Create(messageId, transaction));
            if (removed)
            {
                RetireIdentifier(messageId);
            }
        }

        if (removed)
        {
            transaction.Cancel(cancellationToken);
        }
    }

    private void TimeoutTransaction(uint messageId, PendingTransaction transaction)
    {
        bool removed;
        lock (lifecycleGate)
        {
            removed = pending.TryRemove(KeyValuePair.Create(messageId, transaction));
            if (removed)
            {
                RetireIdentifier(messageId);
            }
        }

        if (removed)
        {
            transaction.Fail(new TimeoutException(
                $"The transaction with message identifier {messageId} timed out."));
        }
    }

    /// <summary>
    /// Clears reuse tombstones after the owning connection generation has ended.
    /// </summary>
    internal void ClearRetiredIdentifiers()
    {
        lock (lifecycleGate)
        {
            retiredIdentifiers.Clear();
            retiredIdentifierExpirations.Clear();
        }
    }

    private bool IsIdentifierRetired(uint messageId, out TimeSpan remaining)
    {
        long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        PruneRetiredIdentifiers(nowTicks);
        if (!retiredIdentifiers.TryGetValue(messageId, out long expiresAtTicks))
        {
            remaining = TimeSpan.Zero;
            return false;
        }

        remaining = TimeSpan.FromSeconds(
            (expiresAtTicks - nowTicks) /
            (double)System.Diagnostics.Stopwatch.Frequency);
        return true;
    }

    private void RetireIdentifier(uint messageId)
    {
        if (identifierReuseQuarantineTimestampTicks == 0)
        {
            return;
        }

        long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        long expiresAtTicks = identifierReuseQuarantineTimestampTicks > long.MaxValue - nowTicks
            ? long.MaxValue
            : nowTicks + identifierReuseQuarantineTimestampTicks;
        retiredIdentifiers[messageId] = expiresAtTicks;
        retiredIdentifierExpirations.Enqueue(new RetiredIdentifier(messageId, expiresAtTicks));
    }

    private void PruneRetiredIdentifiers(long nowTicks)
    {
        while (retiredIdentifierExpirations.TryPeek(out RetiredIdentifier retired) &&
               retired.ExpiresAtTicks <= nowTicks)
        {
            retiredIdentifierExpirations.Dequeue();
            if (retiredIdentifiers.TryGetValue(retired.MessageId, out long currentExpiry) &&
                currentExpiry == retired.ExpiresAtTicks)
            {
                retiredIdentifiers.Remove(retired.MessageId);
            }
        }
    }

    private readonly record struct RetiredIdentifier(uint MessageId, long ExpiresAtTicks);

    internal sealed class PendingTransactionRegistration
    {
        private readonly uint messageId;
        private readonly PendingTransactionManager<T> owner;
        private readonly PendingTransaction transaction;

        internal PendingTransactionRegistration(
            PendingTransactionManager<T> owner,
            uint messageId,
            PendingTransaction transaction)
        {
            this.owner = owner;
            this.messageId = messageId;
            this.transaction = transaction;
        }

        public Task<T> ResponseTask => transaction.Task;

        public void ArmTimeout(TimeSpan timeout)
        {
            ValidateTimeout(timeout);
            transaction.ArmTimeout(timeout);
        }

        public bool TryFail(Exception exception)
        {
            return owner.TryFail(messageId, transaction, exception);
        }

        public void Abandon()
        {
            owner.Abandon(messageId, transaction);
        }
    }

    internal sealed class PendingTransaction
    {
        private readonly TaskCompletionSource<T> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object completionGate = new();
        private readonly uint messageId;
        private readonly PendingTransactionManager<T> owner;
        private readonly Predicate<T>? acceptancePredicate;
        private CancellationToken callerCancellationToken;
        private CancellationTokenRegistration cancellationRegistration;
        private Timer? timeoutTimer;
        private bool completed;
        private bool hasCancellationRegistration;
        private bool timeoutArmed;

        public PendingTransaction(
            PendingTransactionManager<T> owner,
            uint messageId,
            Predicate<T>? acceptancePredicate)
        {
            this.owner = owner;
            this.messageId = messageId;
            this.acceptancePredicate = acceptancePredicate;
        }

        public Task<T> Task => completion.Task;

        public bool Accepts(T result)
        {
            return acceptancePredicate?.Invoke(result) ?? true;
        }

        public void ArmCancellation(CancellationToken cancellationToken)
        {
            callerCancellationToken = cancellationToken;
            CancellationTokenRegistration registration = default;
            bool hasRegistration = false;

            try
            {
                if (cancellationToken.CanBeCanceled)
                {
                    hasRegistration = true;
                    registration = cancellationToken.UnsafeRegister(
                        static state => ((PendingTransaction)state!).OnCanceled(),
                        this);
                }
            }
            catch
            {
                if (hasRegistration)
                {
                    registration.Dispose();
                }

                throw;
            }

            lock (completionGate)
            {
                if (!completed)
                {
                    cancellationRegistration = registration;
                    hasCancellationRegistration = hasRegistration;
                    return;
                }
            }

            if (hasRegistration)
            {
                registration.Dispose();
            }
        }

        public void ArmTimeout(TimeSpan timeout)
        {
            lock (completionGate)
            {
                if (timeoutArmed)
                {
                    throw new InvalidOperationException("The transaction response timeout is already armed.");
                }

                timeoutArmed = true;
                if (completed || timeout == Timeout.InfiniteTimeSpan)
                {
                    return;
                }
            }

            Timer? timer = null;
            try
            {
                timer = new Timer(
                    static state => ((PendingTransaction)state!).OnTimedOut(),
                    this,
                    timeout,
                    Timeout.InfiniteTimeSpan);
            }
            catch
            {
                lock (completionGate)
                {
                    if (!completed)
                    {
                        timeoutArmed = false;
                    }
                }

                throw;
            }

            lock (completionGate)
            {
                if (!completed)
                {
                    timeoutTimer = timer;
                    return;
                }
            }

            timer.Dispose();
        }

        public void Complete(T result)
        {
            Finish(() => completion.TrySetResult(result));
        }

        public void Fail(Exception exception)
        {
            Finish(() => completion.TrySetException(exception));
        }

        public void Cancel(CancellationToken cancellationToken)
        {
            Finish(() => completion.TrySetCanceled(cancellationToken));
        }

        public void Abandon()
        {
            Finish(completeTask: null);
        }

        private void OnCanceled()
        {
            owner.Cancel(messageId, this, callerCancellationToken);
        }

        private void OnTimedOut()
        {
            owner.TimeoutTransaction(messageId, this);
        }

        private void Finish(Action? completeTask)
        {
            CancellationTokenRegistration registration = default;
            Timer? timer;
            bool disposeRegistration;

            lock (completionGate)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                registration = cancellationRegistration;
                disposeRegistration = hasCancellationRegistration;
                timer = timeoutTimer;
                hasCancellationRegistration = false;
                timeoutTimer = null;
            }

            timer?.Dispose();
            if (disposeRegistration)
            {
                registration.Dispose();
            }

            completeTask?.Invoke();
        }
    }
}
