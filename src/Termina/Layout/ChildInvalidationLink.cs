// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;

namespace Termina.Layout;

/// <summary>
/// The <see cref="IInvalidatingNode.Invalidated"/> stream of a parent node. The stream forwards the
/// invalidation of the attached children only while it has a subscriber.
/// </summary>
/// <remarks>
/// <para>
/// The first subscriber connects the stream to each attached child. When the last subscriber leaves,
/// the stream releases each child subscription. A parent that nothing observes thus holds no
/// subscription to its children, and a child that lives longer than the parent does not keep the
/// parent reachable.
/// </para>
/// <para>
/// A teardown thread can dispose the parent while the render loop changes it. The lock makes attach,
/// detach, the first subscribe, the last unsubscribe, and dispose exclusive. Child subscriptions are
/// created under the lock, so that no subscription can be created after dispose. They are released
/// after the lock is exited.
/// </para>
/// </remarks>
internal sealed class ChildInvalidationLink : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Subject<Unit> _output = new();
    private readonly List<ChildEntry> _entries = new();
    private readonly Action _onChildInvalidated;
    private int _observerCount;
    private bool _disposed;

    /// <summary>
    /// Creates a link. Each child invalidation calls <paramref name="onChildInvalidated"/>, which decides
    /// what to emit through <see cref="Emit"/>. Without a callback, the link forwards each child invalidation.
    /// </summary>
    public ChildInvalidationLink(Action? onChildInvalidated = null)
    {
        _onChildInvalidated = onChildInvalidated ?? Emit;
        Invalidated = Observable.Create<Unit>(Observe);
    }

    /// <summary>
    /// The invalidation stream of the parent node.
    /// </summary>
    public Observable<Unit> Invalidated { get; }

    /// <summary>
    /// Emits an invalidation of the parent node itself.
    /// </summary>
    public void Emit() => _output.OnNext(Unit.Default);

    /// <summary>
    /// Attaches a child. An observed link subscribes to the child at once.
    /// </summary>
    public void Attach(ILayoutNode child)
    {
        if (child is not IInvalidatingNode invalidating)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            var entry = new ChildEntry(invalidating);
            _entries.Add(entry);
            Connect(entry);
        }
    }

    /// <summary>
    /// Detaches one attachment of a child, and releases its subscription at once.
    /// </summary>
    public void Detach(ILayoutNode child)
    {
        IDisposable? subscription;
        lock (_gate)
        {
            var index = _entries.FindIndex(entry => ReferenceEquals(entry.Child, child));
            if (index < 0)
                return;

            var entry = _entries[index];
            _entries.RemoveAt(index);
            entry.IsDetached = true;
            subscription = entry.TakeSubscription();
        }

        subscription?.Dispose();
    }

    /// <summary>
    /// Detaches <paramref name="oldChild"/> and attaches <paramref name="newChild"/>.
    /// </summary>
    public void Replace(ILayoutNode oldChild, ILayoutNode newChild)
    {
        Detach(oldChild);
        Attach(newChild);
    }

    /// <summary>
    /// Releases each child subscription, and completes <see cref="Invalidated"/>.
    /// </summary>
    public void Dispose()
    {
        IDisposable[] subscriptions;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            subscriptions = TakeSubscriptions();
            _entries.Clear();
        }

        foreach (var subscription in subscriptions)
            subscription.Dispose();

        // Complete the subject, but do not dispose it. R3 throws ObjectDisposedException from OnNext on a
        // disposed subject, and a child on another thread can still deliver an invalidation that it captured
        // before the release. OnNext on a completed subject does nothing.
        _output.OnCompleted();
    }

    private IDisposable Observe(Observer<Unit> observer)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                observer.OnCompleted();
                return Disposable.Empty;
            }

            var outputSubscription = _output.Subscribe(observer);
            _observerCount++;
            if (_observerCount == 1)
                ConnectFirstObserver(outputSubscription);

            return Disposable.Create(() =>
            {
                outputSubscription.Dispose();
                Unobserve();
            });
        }
    }

    /// <summary>
    /// Connects each entry for the first observer. A child can throw from its subscribe call, for example
    /// a child that was disposed while it was still attached. The link then undoes the observation, so that
    /// the next observer connects the children again, and the exception reaches the caller.
    /// </summary>
    private void ConnectFirstObserver(IDisposable outputSubscription)
    {
        try
        {
            foreach (var entry in _entries.ToArray())
                Connect(entry);
        }
        catch
        {
            _observerCount--;
            outputSubscription.Dispose();
            foreach (var subscription in TakeSubscriptions())
                subscription.Dispose();
            throw;
        }
    }

    private void Unobserve()
    {
        IDisposable[] subscriptions;
        lock (_gate)
        {
            _observerCount--;
            if (_observerCount > 0 || _disposed)
                return;

            subscriptions = TakeSubscriptions();
        }

        foreach (var subscription in subscriptions)
            subscription.Dispose();
    }

    /// <summary>
    /// Subscribes to the child of <paramref name="entry"/> if the link needs the subscription. A child
    /// can change the link from inside its own subscribe call on this thread, so the need is checked again
    /// after the call.
    /// </summary>
    private void Connect(ChildEntry entry)
    {
        if (!NeedsSubscription(entry))
            return;

        var subscription = entry.Child.Invalidated.Subscribe(_ => _onChildInvalidated());
        if (NeedsSubscription(entry))
        {
            entry.Subscription = subscription;
            return;
        }

        subscription.Dispose();
    }

    private bool NeedsSubscription(ChildEntry entry) =>
        !_disposed && _observerCount > 0 && !entry.IsDetached && entry.Subscription is null;

    private IDisposable[] TakeSubscriptions()
    {
        var subscriptions = new List<IDisposable>();
        foreach (var entry in _entries)
        {
            if (entry.TakeSubscription() is { } subscription)
                subscriptions.Add(subscription);
        }

        return [.. subscriptions];
    }

    private sealed class ChildEntry(IInvalidatingNode child)
    {
        public IInvalidatingNode Child { get; } = child;

        public IDisposable? Subscription { get; set; }

        public bool IsDetached { get; set; }

        public IDisposable? TakeSubscription()
        {
            var subscription = Subscription;
            Subscription = null;
            return subscription;
        }
    }
}
