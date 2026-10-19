// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Layout;
using Termina.Rendering;

namespace Termina.Tests.Layout;

/// <summary>
/// A child can send invalidation from a thread that is not the render loop, for example an R3 timer
/// thread. The render loop subscribes to the parent stream, leaves it, and changes the parent at the same
/// time. A teardown thread can dispose the parent while the render loop changes it. The parent must not
/// throw, must forward exactly once while it is observed, and must hold no child subscription when nothing
/// observes it or after it is disposed.
/// </summary>
public sealed class ChildInvalidationConcurrencyTests
{
    private const int ObservationCycles = 5_000;
    private const int AttachCycles = 1_000;
    private static readonly Size Available = new(20, 5);
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    public static TheoryData<string> AllWrappers => ChildInvalidationWrappers.AllWrappers();

    public static TheoryData<string> AttachableWrappers => ChildInvalidationWrappers.AttachableWrappers();

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void ObservationChurnDuringOffLoopInvalidation_KeepsOneSubscriptionAndForwardsOnce(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        var forwarded = new ForwardCounter();
        wrapper.OnActivate();
        ChildInvalidationWrappers.Layout(wrapper, Available);

        using var invalidator = new OffLoopInvalidator(() => child);
        invalidator.Start(HangGuard);
        for (var cycle = 0; cycle < ObservationCycles; cycle++)
        {
            using var churn = Observe(wrapper, forwarded);
            wrapper.OnDeactivate();
            wrapper.OnActivate();
            ChildInvalidationWrappers.Layout(wrapper, Available);
        }
        invalidator.Stop(HangGuard);

        Assert.Null(invalidator.Failure);
        Assert.True(child.IsActive);
        Assert.Equal(0, child.SubscriptionCount);

        var forwarding = Observe(wrapper, forwarded);
        Assert.Equal(1, child.SubscriptionCount);
        AssertForwardsExactlyOnce(child, forwarded);

        forwarding.Dispose();

        Assert.Equal(0, child.SubscriptionCount);
    }

    [Theory]
    [MemberData(nameof(AttachableWrappers))]
    public void AttachDuringOffLoopInvalidation_ForwardsOnlyFromAttachedChildren(string wrapperName)
    {
        var wrapper = ChildInvalidationWrappers.CreateEmpty(wrapperName);
        var forwarded = new ForwardCounter();
        var forwarding = Observe(wrapper, forwarded);
        wrapper.OnActivate();
        var attached = new List<TrackingInvalidatingNode> { new() };
        ChildInvalidationWrappers.Attach(wrapper, attached[0]);
        TrackingInvalidatingNode latest = attached[0];

        using var invalidator = new OffLoopInvalidator(() => Volatile.Read(ref latest));
        invalidator.Start(HangGuard);
        for (var cycle = 0; cycle < AttachCycles; cycle++)
        {
            var child = new TrackingInvalidatingNode();
            ChildInvalidationWrappers.Attach(wrapper, child);
            attached.Add(child);
            Volatile.Write(ref latest, child);
            ChildInvalidationWrappers.Layout(wrapper, Available);
        }
        invalidator.Stop(HangGuard);

        Assert.Null(invalidator.Failure);
        AssertAttachedChildrenState(wrapper, attached);
        AssertForwardsExactlyOnce(latest, forwarded);

        forwarding.Dispose();

        Assert.All(attached, child => Assert.Equal(0, child.SubscriptionCount));
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public async Task DisposeWhileLastObserverReleasesSubscription_DoesNotThrow(string wrapperName)
    {
        var child = new BlockingUnsubscribeNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        var forwarding = Observe(wrapper, new ForwardCounter());
        ChildInvalidationWrappers.Layout(wrapper, Available);

        await RunWhileUnsubscribeBlocks(child, forwarding.Dispose, wrapper.Dispose);

        Assert.Equal(0, child.SubscriptionCount);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public async Task LastObserverLeavesWhileDisposeReleasesSubscription_DoesNotThrow(string wrapperName)
    {
        var child = new BlockingUnsubscribeNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        var forwarding = Observe(wrapper, new ForwardCounter());
        ChildInvalidationWrappers.Layout(wrapper, Available);

        await RunWhileUnsubscribeBlocks(child, wrapper.Dispose, forwarding.Dispose);

        Assert.Equal(0, child.SubscriptionCount);
    }

    [Fact]
    public async Task ContainerDisposeWhileLastObserverReleasesTwoChildSubscriptions_DoesNotThrow()
    {
        var blocking = new BlockingUnsubscribeNode();
        var other = new TrackingInvalidatingNode();
        var container = new VerticalLayout([blocking, other]);
        var forwarding = Observe(container, new ForwardCounter());

        await RunWhileUnsubscribeBlocks(blocking, forwarding.Dispose, container.Dispose);

        Assert.Equal(0, blocking.SubscriptionCount);
        Assert.Equal(0, other.SubscriptionCount);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public async Task DisposeWhileFirstObserverSubscribes_LeavesNoSubscription(string wrapperName)
    {
        var child = new BlockingSubscribeNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        ChildInvalidationWrappers.Layout(wrapper, Available);
        var completed = false;

        await RunWhileSubscribeBlocks(
            child,
            () => ((IInvalidatingNode)wrapper).Invalidated.Subscribe(_ => { }, _ => completed = true),
            wrapper.Dispose);

        Assert.True(completed);
        Assert.Equal(0, child.SubscriptionCount);
    }

    [Theory]
    [MemberData(nameof(AttachableWrappers))]
    public async Task DisposeWhileObservedParentAttachesChild_LeavesNoSubscription(string wrapperName)
    {
        var wrapper = ChildInvalidationWrappers.CreateEmpty(wrapperName);
        using var forwarding = Observe(wrapper, new ForwardCounter());
        var child = new BlockingSubscribeNode();

        await RunWhileSubscribeBlocks(child, () => ChildInvalidationWrappers.Attach(wrapper, child), wrapper.Dispose);

        Assert.Equal(0, child.SubscriptionCount);
    }

    private static IDisposable Observe(LayoutNode wrapper, ForwardCounter forwarded) =>
        ((IInvalidatingNode)wrapper).Invalidated.Subscribe(_ => forwarded.Increment());

    private static void AssertForwardsExactlyOnce(TrackingInvalidatingNode child, ForwardCounter forwarded)
    {
        forwarded.Reset();
        child.Invalidate();
        Assert.Equal(1, forwarded.Count);
    }

    private static void AssertAttachedChildrenState(LayoutNode wrapper, IReadOnlyList<TrackingInvalidatingNode> attached)
    {
        if (wrapper is ContainerNode)
        {
            Assert.All(attached, child => Assert.Equal(1, child.SubscriptionCount));
            Assert.All(attached, child => Assert.True(child.IsActive));
            return;
        }

        var replaced = attached.Take(attached.Count - 1).ToList();
        Assert.All(replaced, child => Assert.Equal(0, child.SubscriptionCount));
        Assert.All(replaced, child => Assert.False(child.IsActive));
        Assert.Equal(1, attached[^1].SubscriptionCount);
        Assert.True(attached[^1].IsActive);
    }

    /// <summary>
    /// Runs <paramref name="first"/> until it blocks inside the release of the child subscription, then runs
    /// <paramref name="second"/> to completion on another thread, and then lets <paramref name="first"/> finish.
    /// </summary>
    private static async Task RunWhileUnsubscribeBlocks(BlockingUnsubscribeNode child, Action first, Action second)
    {
        child.BlockNextUnsubscribe();
        var firstRun = Task.Run(first);
        await child.UnsubscribeStarted.Task.WaitAsync(HangGuard);

        var secondRun = Task.Run(second);
        try
        {
            await secondRun.WaitAsync(HangGuard);
        }
        finally
        {
            child.AllowUnsubscribeToFinish.TrySetResult();
            await firstRun.WaitAsync(HangGuard);
        }
    }

    /// <summary>
    /// Runs <paramref name="first"/> until it blocks inside the subscription to the child, then starts
    /// <paramref name="second"/> on another thread, lets <paramref name="first"/> finish, and waits for both.
    /// The parent holds its lock while it subscribes, so <paramref name="second"/> can finish only after
    /// <paramref name="first"/>.
    /// </summary>
    private static async Task RunWhileSubscribeBlocks(BlockingSubscribeNode child, Action first, Action second)
    {
        child.BlockNextSubscribe();
        var firstRun = Task.Run(first);
        await child.SubscribeStarted.Task.WaitAsync(HangGuard);

        var secondRun = Task.Run(second);
        child.AllowSubscribeToFinish.TrySetResult();
        await firstRun.WaitAsync(HangGuard);
        await secondRun.WaitAsync(HangGuard);
    }

    private sealed class ForwardCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Increment() => Interlocked.Increment(ref _count);

        public void Reset() => Volatile.Write(ref _count, 0);
    }

    /// <summary>
    /// Sends invalidation from a dedicated thread in a loop until the test stops it.
    /// </summary>
    private sealed class OffLoopInvalidator : IDisposable
    {
        private readonly Func<TrackingInvalidatingNode> _target;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _firstEmission = new();
        private volatile bool _stopRequested;

        public OffLoopInvalidator(Func<TrackingInvalidatingNode> target)
        {
            _target = target;
            _thread = new Thread(Run) { IsBackground = true, Name = nameof(OffLoopInvalidator) };
        }

        public Exception? Failure { get; private set; }

        public void Start(TimeSpan hangGuard)
        {
            _thread.Start();
            if (!_firstEmission.Wait(hangGuard))
                throw new TimeoutException("The invalidation thread did not start.");
        }

        public void Stop(TimeSpan hangGuard)
        {
            _stopRequested = true;
            if (!_thread.Join(hangGuard))
                throw new TimeoutException("The invalidation thread did not stop.");
        }

        public void Dispose()
        {
            _stopRequested = true;
            _thread.Join(HangGuard);
            _firstEmission.Dispose();
        }

        private void Run()
        {
            try
            {
                while (!_stopRequested)
                {
                    _target().Invalidate();
                    _firstEmission.Set();
                }
            }
            catch (Exception exception)
            {
                Failure = exception;
                _firstEmission.Set();
            }
        }
    }

    /// <summary>
    /// A child whose next subscription release, after the test arms it, blocks until the test lets it finish.
    /// </summary>
    private sealed class BlockingUnsubscribeNode : LayoutNode, IInvalidatingNode
    {
        private int _subscriptionCount;
        private int _blockNextUnsubscribe;

        public BlockingUnsubscribeNode()
        {
            Invalidated = Observable.Create<Unit>(_ =>
            {
                Interlocked.Increment(ref _subscriptionCount);
                return Disposable.Create(Unsubscribe);
            });
        }

        public Observable<Unit> Invalidated { get; }

        public int SubscriptionCount => Volatile.Read(ref _subscriptionCount);

        public TaskCompletionSource UnsubscribeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowUnsubscribeToFinish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Size Measure(Size available) => Size.Zero;

        public override void Render(IRenderContext context, Rect bounds)
        {
        }

        public void BlockNextUnsubscribe() => Volatile.Write(ref _blockNextUnsubscribe, 1);

        private void Unsubscribe()
        {
            Interlocked.Decrement(ref _subscriptionCount);
            if (Interlocked.Exchange(ref _blockNextUnsubscribe, 0) == 0)
                return;

            UnsubscribeStarted.TrySetResult();
            if (!AllowUnsubscribeToFinish.Task.Wait(HangGuard))
                throw new TimeoutException("The test did not let the subscription release finish.");
        }
    }

    /// <summary>
    /// A child whose next subscription, after the test arms it, blocks until the test lets it finish.
    /// </summary>
    private sealed class BlockingSubscribeNode : LayoutNode, IInvalidatingNode
    {
        private int _subscriptionCount;
        private int _blockNextSubscribe;

        public BlockingSubscribeNode()
        {
            Invalidated = Observable.Create<Unit>(_ =>
            {
                BlockIfArmed();
                Interlocked.Increment(ref _subscriptionCount);
                return Disposable.Create(() => Interlocked.Decrement(ref _subscriptionCount));
            });
        }

        public Observable<Unit> Invalidated { get; }

        public int SubscriptionCount => Volatile.Read(ref _subscriptionCount);

        public TaskCompletionSource SubscribeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowSubscribeToFinish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Size Measure(Size available) => Size.Zero;

        public override void Render(IRenderContext context, Rect bounds)
        {
        }

        public void BlockNextSubscribe() => Volatile.Write(ref _blockNextSubscribe, 1);

        private void BlockIfArmed()
        {
            if (Interlocked.Exchange(ref _blockNextSubscribe, 0) == 0)
                return;

            SubscribeStarted.TrySetResult();
            if (!AllowSubscribeToFinish.Task.Wait(HangGuard))
                throw new TimeoutException("The test did not let the subscription finish.");
        }
    }
}
