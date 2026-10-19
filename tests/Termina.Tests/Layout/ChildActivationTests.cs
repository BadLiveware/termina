// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Layout;
using Termina.Reactive;
using Termina.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// A parent node activates its children when it is activated, and deactivates them when it is deactivated.
/// A child that an active parent attaches, or that replaces the old content, is activated at once.
/// </summary>
public sealed class ChildActivationTests
{
    private const string Route = "/";
    private static readonly Size Available = new(20, 5);

    public static TheoryData<string> AllWrappers => ChildInvalidationWrappers.AllWrappers();

    public static TheoryData<string> AttachableWrappers => ChildInvalidationWrappers.AttachableWrappers();

    [Theory]
    [MemberData(nameof(AttachableWrappers))]
    public void ChildAttachedWhileActive_IsActivated(string wrapperName)
    {
        var wrapper = ChildInvalidationWrappers.CreateEmpty(wrapperName);
        wrapper.OnActivate();
        var child = new TrackingInvalidatingNode();

        ChildInvalidationWrappers.Attach(wrapper, child);

        Assert.True(child.IsActive);
        Assert.Equal(1, child.ActivationCount);
    }

    [Theory]
    [MemberData(nameof(AttachableWrappers))]
    public void ChildAttachedWhileInactive_IsActivatedWithTheParent(string wrapperName)
    {
        var wrapper = ChildInvalidationWrappers.CreateEmpty(wrapperName);
        var child = new TrackingInvalidatingNode();

        ChildInvalidationWrappers.Attach(wrapper, child);
        Assert.False(child.IsActive);

        wrapper.OnActivate();

        Assert.True(child.IsActive);
        Assert.Equal(1, child.ActivationCount);
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void ReactivatedWrapper_ActivatesChildOneTimeEachCycle(string wrapperName)
    {
        var child = new TrackingInvalidatingNode();
        var wrapper = ChildInvalidationWrappers.Wrap(wrapperName, child);
        wrapper.OnActivate();
        ChildInvalidationWrappers.Layout(wrapper, Available);
        wrapper.OnDeactivate();
        Assert.False(child.IsActive);

        wrapper.OnActivate();
        ChildInvalidationWrappers.Layout(wrapper, Available);

        Assert.True(child.IsActive);
        Assert.Equal(2, child.ActivationCount);
    }

    [Fact]
    public void ReactiveLayoutOverReplayingSource_ActivatesChildOneTimeAfterReactivation()
    {
        var child = new TrackingInvalidatingNode();
        using var source = new ReactiveProperty<int>(0);
        var node = new ReactiveLayoutNode<int>(source, _ => child);
        node.OnActivate();
        node.OnDeactivate();

        node.OnActivate();

        Assert.True(child.IsActive);
        Assert.Equal(2, child.ActivationCount);
    }

    [Fact]
    public void SpinnerInsideScrollableContainer_AnimatesOnActivePage()
    {
        var app = new TerminaApplication(new VirtualTerminal(40, 6));
        try
        {
            app.RegisterRoute<ScrollableSpinnerPage, SpinnerViewModel>(Route);
            app.NavigateTo(Route);
            app.Post(() => { });
            app.ProcessQueuedEvents(CancellationToken.None);

            Assert.True(ScrollableSpinnerPage.Last!.Spinner.IsAnimating);
        }
        finally
        {
            app.Dispose();
        }
    }

    private sealed class SpinnerViewModel : ReactiveViewModel
    {
    }

    private sealed class ScrollableSpinnerPage : ReactivePage<SpinnerViewModel>
    {
        public static ScrollableSpinnerPage? Last;

        public ScrollableSpinnerPage() => Last = this;

        public SpinnerNode Spinner { get; } = new();

        public override ILayoutNode BuildLayout() =>
            new ScrollableContainerNode().WithContent(new VerticalLayout([Spinner]));
    }
}
