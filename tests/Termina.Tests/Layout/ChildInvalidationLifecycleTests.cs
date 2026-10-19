// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using R3;
using Termina.Extensions;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// A parent node subscribes to the invalidation of a child only while something subscribes to the parent's
/// own <see cref="IInvalidatingNode.Invalidated"/> stream. Activation starts and stops the children, but it
/// does not decide the forwarding. A parent that nothing observes must not stay reachable from a child that
/// lives longer than the parent.
/// </summary>
public sealed class ChildInvalidationLifecycleTests
{
    private const int FactoryEmissions = 10;
    private const int FrameCount = 10;
    private static readonly Size Available = new(20, 5);

    public static TheoryData<string> AllWrappers => ChildInvalidationWrappers.AllWrappers();

    public static TheoryData<string> AttachableWrappers => ChildInvalidationWrappers.AttachableWrappers();

    public static TheoryData<string> ReplaceableWrappers => ChildInvalidationWrappers.ReplaceableWrappers();

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void ObservedActiveWrapper_ActivatesChildAndForwardsInvalidation(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        var forwarded = 0;
        using var forwarding = Observe(wrapper, () => forwarded++);

        wrapper.OnActivate();
        ChildInvalidationWrappers.Layout(wrapper, Available);
        forwarded = 0;
        child.Change("changed");

        Assert.True(child.IsActive);
        Assert.Equal(1, child.SubscriptionCount);
        Assert.Equal(1, forwarded);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void UnobservedWrapper_HoldsNoChildSubscription(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);

        ChildInvalidationWrappers.Layout(wrapper, Available);
        Assert.Equal(0, child.SubscriptionCount);

        wrapper.OnActivate();
        ChildInvalidationWrappers.Layout(wrapper, Available);

        Assert.True(child.IsActive);
        Assert.Equal(0, child.SubscriptionCount);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void ObservedInactiveWrapper_ForwardsInvalidation(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        var forwarded = 0;
        using var forwarding = Observe(wrapper, () => forwarded++);

        ChildInvalidationWrappers.Layout(wrapper, Available);
        forwarded = 0;
        child.Change("changed");

        Assert.False(child.IsActive);
        Assert.Equal(1, child.SubscriptionCount);
        Assert.Equal(1, forwarded);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void DeactivatedObservedWrapper_DeactivatesChildAndStillForwards(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        var forwarded = 0;
        using var forwarding = Observe(wrapper, () => forwarded++);
        wrapper.OnActivate();
        ChildInvalidationWrappers.Layout(wrapper, Available);

        wrapper.OnDeactivate();
        forwarded = 0;
        child.Change("changed");

        Assert.False(child.IsActive);
        Assert.Equal(1, child.SubscriptionCount);
        Assert.Equal(1, forwarded);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void LastObserverLeaves_ReleasesChildSubscription(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        var forwarded = 0;
        var forwarding = Observe(wrapper, () => forwarded++);
        wrapper.OnActivate();
        ChildInvalidationWrappers.Layout(wrapper, Available);

        forwarding.Dispose();
        forwarded = 0;
        child.Change("changed");

        Assert.True(child.IsActive);
        Assert.Equal(0, child.SubscriptionCount);
        Assert.Equal(0, forwarded);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void ReobservedWrapper_ForwardsExactlyOnce(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        wrapper.OnActivate();
        ChildInvalidationWrappers.Layout(wrapper, Available);
        Observe(wrapper, () => { }).Dispose();

        var forwarded = 0;
        using var forwarding = Observe(wrapper, () => forwarded++);
        ChildInvalidationWrappers.Layout(wrapper, Available);
        forwarded = 0;
        child.Change("changed");

        Assert.Equal(1, child.SubscriptionCount);
        Assert.Equal(1, forwarded);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void SecondObserverLeaves_FirstObserverStillForwards(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        var first = 0;
        var second = 0;
        using var firstForwarding = Observe(wrapper, () => first++);
        var secondForwarding = Observe(wrapper, () => second++);
        ChildInvalidationWrappers.Layout(wrapper, Available);
        Assert.Equal(1, child.SubscriptionCount);

        secondForwarding.Dispose();
        first = 0;
        second = 0;
        child.Change("changed");

        Assert.Equal(1, child.SubscriptionCount);
        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void RepeatedActivation_HoldsOneChildSubscription(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        using var forwarding = Observe(wrapper, () => { });

        for (var cycle = 0; cycle < 3; cycle++)
        {
            wrapper.OnActivate();
            wrapper.OnActivate();
            ChildInvalidationWrappers.Layout(wrapper, Available);
            wrapper.OnDeactivate();
            wrapper.OnDeactivate();
        }

        wrapper.OnActivate();
        ChildInvalidationWrappers.Layout(wrapper, Available);

        Assert.Equal(1, child.SubscriptionCount);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void DisposedWrapper_ReleasesChildSubscriptionAndCompletesStream(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        var completed = false;
        using var forwarding = ((IInvalidatingNode)wrapper).Invalidated.Subscribe(_ => { }, _ => completed = true);
        wrapper.OnActivate();
        ChildInvalidationWrappers.Layout(wrapper, Available);

        wrapper.Dispose();

        Assert.True(completed);
        Assert.Equal(0, child.SubscriptionCount);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void ReactiveFactory_RetiredWrappersReleaseReusedChild(string wrapperName)
    {
        var shared = new TrackingInvalidatingNode();
        var source = new Subject<int>();
        var node = source
            .Select<int, ILayoutNode>(_ => ChildInvalidationWrappers.Wrap(wrapperName, shared))
            .AsLayout();
        using var forwarding = Observe(node, () => { });
        node.OnActivate();

        for (var emission = 0; emission < FactoryEmissions; emission++)
        {
            source.OnNext(emission);
            ChildInvalidationWrappers.Layout(node, Available);
        }

        Assert.True(shared.IsActive);
        Assert.Equal(1, shared.SubscriptionCount);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void ReactiveFactory_RetiredWrappersAreCollectable(string wrapperName)
    {
        var shared = new TrackingInvalidatingNode();

        var (node, forwarding, wrappers) = EmitFactoryWrappers(wrapperName, shared);
        CollectGarbage();

        Assert.Equal(FactoryEmissions, wrappers.Count);
        Assert.All(wrappers.Take(FactoryEmissions - 1), retired => Assert.False(retired.IsAlive));
        Assert.True(wrappers[^1].IsAlive);
        Assert.Equal(1, shared.SubscriptionCount);
        GC.KeepAlive(node);
        GC.KeepAlive(shared);
        forwarding.Dispose();
    }

    [Theory]
    [MemberData(nameof(AttachableWrappers))]
    public void ChildAttachedWhileObserved_IsSubscribedAtOnce(string wrapperName)
    {
        var wrapper = ChildInvalidationWrappers.CreateEmpty(wrapperName);
        var forwarded = 0;
        using var forwarding = Observe(wrapper, () => forwarded++);
        var child = new TrackingInvalidatingNode();

        ChildInvalidationWrappers.Attach(wrapper, child);
        forwarded = 0;
        child.Change("changed");

        Assert.Equal(1, child.SubscriptionCount);
        Assert.Equal(1, forwarded);
    }

    [Theory]
    [MemberData(nameof(AttachableWrappers))]
    public void ChildAttachedWhileUnobserved_IsSubscribedByFirstObserver(string wrapperName)
    {
        var wrapper = ChildInvalidationWrappers.CreateEmpty(wrapperName);
        var child = new TrackingInvalidatingNode();

        ChildInvalidationWrappers.Attach(wrapper, child);
        Assert.Equal(0, child.SubscriptionCount);

        using var forwarding = Observe(wrapper, () => { });

        Assert.Equal(1, child.SubscriptionCount);
    }

    [Theory]
    [MemberData(nameof(ReplaceableWrappers))]
    public void ReplacedContent_IsReleasedAtOnce(string wrapperName)
    {
        var wrapper = ChildInvalidationWrappers.CreateEmpty(wrapperName);
        var forwarded = 0;
        using var forwarding = Observe(wrapper, () => forwarded++);
        var first = new TrackingInvalidatingNode();
        var second = new TrackingInvalidatingNode();
        ChildInvalidationWrappers.Attach(wrapper, first);

        ChildInvalidationWrappers.Attach(wrapper, second);
        forwarded = 0;
        first.Change("first");

        Assert.Equal(0, first.SubscriptionCount);
        Assert.Equal(1, second.SubscriptionCount);
        Assert.Equal(0, forwarded);
    }

    [Theory]
    [MemberData(nameof(AttachableWrappers))]
    public void NestedContainerInsideObservedWrapper_ForwardsGrandchildInvalidation(string wrapperName)
    {
        var grandchild = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, new VerticalLayout([grandchild]));
        var forwarded = 0;
        using var forwarding = Observe(wrapper, () => forwarded++);
        wrapper.OnActivate();

        grandchild.Change("changed");

        Assert.True(grandchild.IsActive);
        Assert.Equal(1, grandchild.SubscriptionCount);
        Assert.Equal(1, forwarded);
    }

    [Fact]
    public void PerFrameWrapper_RenderedButNeverObserved_CreatesNoChildSubscription()
    {
        var root = new TrackingInvalidatingNode("root");
        var overlay = new TrackingInvalidatingNode("overlay");
        var terminal = new VirtualTerminal(Available.Width, Available.Height);
        var context = new RegionRenderContext(terminal, 0, 0, Available.Width, Available.Height);
        var bounds = new Rect(0, 0, Available.Width, Available.Height);

        for (var frame = 0; frame < FrameCount; frame++)
        {
            var frameRoot = new StackLayout([root, new DeferredNode(() => overlay)]);
            frameRoot.Measure(Available);
            frameRoot.Render(context, bounds);
        }

        Assert.Equal(0, root.SubscriptionCount);
        Assert.Equal(0, overlay.SubscriptionCount);
    }

    [Fact]
    public void FirstObserverThatFailsToConnect_LeavesNoSubscriptionAndNextObserverConnects()
    {
        var healthy = new TrackingInvalidatingNode();
        var failing = new FailingSubscribeNode();
        var container = new VerticalLayout([healthy, failing]);

        Assert.Throws<ObjectDisposedException>(() => container.Invalidated.Subscribe(_ => { }));
        Assert.Equal(0, healthy.SubscriptionCount);

        failing.Fails = false;
        var forwarded = 0;
        using var forwarding = Observe(container, () => forwarded++);
        healthy.Invalidate();

        Assert.Equal(1, healthy.SubscriptionCount);
        Assert.Equal(1, forwarded);
    }

    private static IDisposable Observe(ILayoutNode wrapper, Action onInvalidated) =>
        ((IInvalidatingNode)wrapper).Invalidated.Subscribe(_ => onInvalidated());

    /// <summary>
    /// Builds and retires the wrappers in a frame of its own, so no local variable keeps a retired
    /// wrapper reachable when the caller collects garbage in a Debug build.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ILayoutNode Node, IDisposable Forwarding, IReadOnlyList<WeakReference> Wrappers) EmitFactoryWrappers(
        string wrapperName,
        TrackingInvalidatingNode shared)
    {
        var wrappers = new List<WeakReference>();
        var source = new Subject<int>();
        var node = source
            .Select<int, ILayoutNode>(_ =>
            {
                var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, shared);
                wrappers.Add(new WeakReference(wrapper));
                return wrapper;
            })
            .AsLayout();
        var forwarding = Observe(node, () => { });
        node.OnActivate();

        for (var emission = 0; emission < FactoryEmissions; emission++)
        {
            source.OnNext(emission);
            ChildInvalidationWrappers.Layout(node, Available);
        }

        return (node, forwarding, wrappers);
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// A child whose subscribe call throws, as the R3 subject of a disposed node does.
    /// </summary>
    private sealed class FailingSubscribeNode : LayoutNode, IInvalidatingNode
    {
        public FailingSubscribeNode()
        {
            Invalidated = Observable.Create<Unit>(_ =>
            {
                if (Fails)
                    throw new ObjectDisposedException(nameof(FailingSubscribeNode));

                return Disposable.Empty;
            });
        }

        public bool Fails { get; set; } = true;

        public Observable<Unit> Invalidated { get; }

        public override Size Measure(Size available) => Size.Zero;

        public override void Render(IRenderContext context, Rect bounds)
        {
        }
    }
}
