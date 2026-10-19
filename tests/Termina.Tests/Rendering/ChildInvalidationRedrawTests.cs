// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using R3;
using Termina.Extensions;
using Termina.Input;
using Termina.Layout;
using Termina.Notifications;
using Termina.Pages;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;
using Termina.Tests.Layout;

namespace Termina.Tests.Rendering;

/// <summary>
/// A change of a child node must redraw an active page, through every framework parent node. A page
/// that builds a new parent for a reused child must not keep the old parents alive through that child.
/// </summary>
public sealed class ChildInvalidationRedrawTests
{
    private const string Route = "/";
    private const string OtherRoute = "/other";
    private const int StepCount = 10;

    public static TheoryData<string> AllWrappers => ChildInvalidationWrappers.AllWrappers();

    [Fact]
    public void ReactiveFactoryPage_KeepsOneSubscriptionOnReusedChildAndRedraws()
    {
        RunPage<FactoryPage>((app, terminal) =>
        {
            var page = FactoryPage.Last!;

            for (var step = 1; step <= StepCount; step++)
            {
                page.CurrentViewModel.Step.Value = step;
                app.ProcessQueuedEvents(CancellationToken.None);
            }

            Assert.Equal(1, page.Shared.SubscriptionCount);
            Assert.Contains($"step {StepCount}", terminal.ToString());

            page.Shared.Change("changed");
            app.ProcessQueuedEvents(CancellationToken.None);

            Assert.Contains("changed", terminal.ToString());
        });
    }

    [Theory]
    [MemberData(nameof(AllWrappers))]
    public void ActivePage_RedrawsAfterChangeOfNestedChild(string wrapperName)
    {
        WrapperPage.WrapperName = wrapperName;
        RunPage<WrapperPage>((app, terminal) =>
        {
            var page = WrapperPage.Last!;
            Assert.Contains("before", terminal.ToString());

            page.Child.Change("after");
            app.ProcessQueuedEvents(CancellationToken.None);

            Assert.Contains("after", terminal.ToString());
        });
    }

    [Fact]
    public void RebuiltPage_WrapperReusedAfterAnAbandonedBuild_RedrawsAfterChildChange()
    {
        RunPage<AlternatingPage>((app, terminal) =>
        {
            var page = AlternatingPage.Last!;

            page.ShowOther();
            app.ProcessQueuedEvents(CancellationToken.None);
            Assert.Contains("other", terminal.ToString());

            page.ShowPanel();
            app.ProcessQueuedEvents(CancellationToken.None);
            page.Child.Change("after");
            app.ProcessQueuedEvents(CancellationToken.None);

            Assert.Contains("after", terminal.ToString());
        });
    }

    [Fact]
    public void PreservedPage_AfterNavigationBack_RedrawsAfterChildChange()
    {
        WrapperPage.WrapperName = ChildInvalidationWrappers.Panel;
        var terminal = new VirtualTerminal(40, 6);
        var app = new TerminaApplication(terminal);
        try
        {
            app.RegisterRoute<WrapperPage, StepViewModel>(Route, NavigationBehavior.PreserveState);
            app.RegisterRoute<OtherPage, StepViewModel>(OtherRoute);
            app.NavigateTo(Route);
            var page = WrapperPage.Last!;
            app.NavigateTo(OtherRoute);
            Assert.Equal(0, page.Child.SubscriptionCount);

            app.NavigateTo(Route);
            app.Post(() => { });
            app.ProcessQueuedEvents(CancellationToken.None);
            page.Child.Change("after");
            app.ProcessQueuedEvents(CancellationToken.None);

            Assert.Same(page, WrapperPage.Last);
            Assert.Equal(1, page.Child.SubscriptionCount);
            Assert.Contains("after", terminal.ToString());
        }
        finally
        {
            app.Dispose();
        }
    }

    [Fact]
    public void ReactiveFactoryPage_RetiredContainersAreCollectable()
    {
        RunPage<FactoryPage>((app, terminal) =>
        {
            var page = FactoryPage.Last!;

            StepFactoryPage(app, page);
            CollectGarbage();

            var containers = page.Containers;
            Assert.Equal(StepCount + 1, containers.Count);
            Assert.All(containers.Take(StepCount), retired => Assert.False(retired.IsAlive));
            Assert.True(containers[^1].IsAlive);
            Assert.Contains($"step {StepCount}", terminal.ToString());
        });
    }

    [Fact]
    public void DocumentedCustomContainer_RedrawsAfterChildChange()
    {
        RunPage<DocumentedCustomContainerPage>((app, terminal) =>
        {
            var page = DocumentedCustomContainerPage.Last!;
            Assert.Contains("before", terminal.ToString());

            page.Child.Change("after");
            app.ProcessQueuedEvents(CancellationToken.None);

            Assert.Equal(1, page.Child.SubscriptionCount);
            Assert.Contains("after", terminal.ToString());
        });
    }

    [Fact]
    public void CustomWrapperThatDoesNotForwardOnActivate_StillRedrawsAfterNestedChildChange()
    {
        RunPage<NonForwardingWrapperPage>((app, terminal) =>
        {
            var page = NonForwardingWrapperPage.Last!;
            Assert.Contains("before", terminal.ToString());

            page.Child.Change("after");
            app.ProcessQueuedEvents(CancellationToken.None);

            Assert.False(page.Child.IsActive);
            Assert.Equal(1, page.Child.SubscriptionCount);
            Assert.Contains("after", terminal.ToString());
        });
    }

    [Fact]
    public void ToastFrameWrapper_DoesNotSubscribeToPageRootOnEachFrame()
    {
        var terminal = new VirtualTerminal(40, 6);
        var app = new TerminaApplication(terminal, new ToastServiceProvider());
        try
        {
            app.RegisterRoute<RootChildPage, StepViewModel>(Route);
            app.NavigateTo(Route);
            var page = RootChildPage.Last!;

            for (var frame = 0; frame < StepCount; frame++)
            {
                page.Root.Change($"frame {frame}");
                app.ProcessQueuedEvents(CancellationToken.None);
            }

            Assert.Contains($"frame {StepCount - 1}", terminal.ToString());
            Assert.Equal(1, page.Root.SubscriptionCount);
        }
        finally
        {
            app.Dispose();
        }
    }

    private static void RunPage<TPage>(Action<TerminaApplication, VirtualTerminal> scenario)
        where TPage : ReactivePage<StepViewModel>, new()
    {
        var terminal = new VirtualTerminal(40, 6);
        var app = new TerminaApplication(terminal);
        try
        {
            app.RegisterRoute<TPage, StepViewModel>(Route);
            app.NavigateTo(Route);
            app.Post(() => { });
            app.ProcessQueuedEvents(CancellationToken.None);
            scenario(app, terminal);
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// Changes the factory step in a frame of its own, so no local variable keeps a retired container
    /// reachable when the caller collects garbage in a Debug build.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StepFactoryPage(TerminaApplication app, FactoryPage page)
    {
        for (var step = 1; step <= StepCount; step++)
        {
            page.CurrentViewModel.Step.Value = step;
            app.ProcessQueuedEvents(CancellationToken.None);
        }
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class StepViewModel : ReactiveViewModel
    {
        public ReactiveProperty<int> Step { get; } = new(0);

        public override void Dispose()
        {
            Step.Dispose();
            base.Dispose();
        }
    }

    private sealed class FactoryPage : ReactivePage<StepViewModel>
    {
        public static FactoryPage? Last;

        public FactoryPage() => Last = this;

        public TrackingInvalidatingNode Shared { get; } = new();

        public List<WeakReference> Containers { get; } = new();

        public StepViewModel CurrentViewModel => ViewModel;

        public override ILayoutNode BuildLayout() =>
            ViewModel.Step
                .Select<int, ILayoutNode>(CreateContainer)
                .AsLayout();

        private VerticalLayout CreateContainer(int step)
        {
            var container = new VerticalLayout([Shared, new TextNode($"step {step}")]);
            Containers.Add(new WeakReference(container));
            return container;
        }
    }

    private sealed class WrapperPage : ReactivePage<StepViewModel>
    {
        public static WrapperPage? Last;
        public static string WrapperName = ChildInvalidationWrappers.Stack;

        public WrapperPage() => Last = this;

        public TrackingInvalidatingNode Child { get; } = new("before");

        public override ILayoutNode BuildLayout() =>
            ChildInvalidationWrappers.Wrap(WrapperName, new VerticalLayout([Child]));
    }

    private sealed class AlternatingPage : ReactivePage<StepViewModel>
    {
        public static AlternatingPage? Last;
        private readonly PanelNode _panel;
        private bool _showPanel = true;

        public AlternatingPage()
        {
            Last = this;
            _panel = new PanelNode().WithContent(Child);
        }

        public TrackingInvalidatingNode Child { get; } = new("before");

        public void ShowOther()
        {
            _showPanel = false;
            InvalidateLayout();
        }

        public void ShowPanel()
        {
            _showPanel = true;
            InvalidateLayout();
        }

        public override ILayoutNode BuildLayout()
        {
            if (_showPanel)
                return _panel;

            return new TextNode("other");
        }
    }

    private sealed class DocumentedCustomContainerPage : ReactivePage<StepViewModel>
    {
        public static DocumentedCustomContainerPage? Last;

        public DocumentedCustomContainerPage() => Last = this;

        public TrackingInvalidatingNode Child { get; } = new("before");

        public override ILayoutNode BuildLayout() => new CustomContainer(new TextNode("header"), Child);
    }

    private sealed class NonForwardingWrapperPage : ReactivePage<StepViewModel>
    {
        public static NonForwardingWrapperPage? Last;

        public NonForwardingWrapperPage() => Last = this;

        public TrackingInvalidatingNode Child { get; } = new("before");

        public override ILayoutNode BuildLayout() => new NonForwardingWrapper(new VerticalLayout([Child]));
    }

    /// <summary>
    /// A wrapper that forwards the <see cref="IInvalidatingNode.Invalidated"/> stream of a built-in container,
    /// but does not forward <see cref="LayoutNode.OnActivate"/>. The built-in container forwards its children
    /// because the page observes it through the wrapper, not because it is active.
    /// </summary>
    private sealed class NonForwardingWrapper : LayoutNode, IInvalidatingNode
    {
        private readonly VerticalLayout _layout;

        public NonForwardingWrapper(VerticalLayout layout) => _layout = layout;

        public Observable<Unit> Invalidated => _layout.Invalidated;

        public override Size Measure(Size available) => _layout.Measure(available);

        public override void Render(IRenderContext context, Rect bounds) => _layout.Render(context, bounds);
    }

    /// <summary>
    /// The custom container example from <c>docs/advanced/custom-components.md</c>, copied as written.
    /// </summary>
    public class CustomContainer : LayoutNode, IInvalidatingNode
    {
        private readonly List<ILayoutNode> _children = new();

        public CustomContainer(params ILayoutNode[] children)
        {
            _children.AddRange(children);
            Invalidated = _children
                .OfType<IInvalidatingNode>()
                .Select(child => child.Invalidated)
                .Merge();
        }

        public Observable<Unit> Invalidated { get; }

        public override Size Measure(Size available)
        {
            // Stack the children from top to bottom
            var width = 0;
            var height = 0;
            foreach (var child in _children)
            {
                var size = child.Measure(new Size(available.Width, Math.Max(0, available.Height - height)));
                width = Math.Max(width, size.Width);
                height += size.Height;
            }
            return new Size(width, height);
        }

        public override void Render(IRenderContext context, Rect bounds)
        {
            var y = bounds.Y;
            foreach (var child in _children)
            {
                var size = child.Measure(new Size(bounds.Width, Math.Max(0, bounds.Bottom - y)));
                child.Render(context, new Rect(bounds.X, y, bounds.Width, size.Height));
                y += size.Height;
            }
        }

        public override void OnActivate()
        {
            // Activate all children
            foreach (var child in _children)
            {
                if (child is IActivatableNode node)
                    node.OnActivate();
            }
            base.OnActivate();
        }

        public override void OnDeactivate()
        {
            // Deactivate all children
            foreach (var child in _children)
            {
                if (child is IActivatableNode node)
                    node.OnDeactivate();
            }
            base.OnDeactivate();
        }

        public override void Dispose()
        {
            // Dispose all children
            foreach (var child in _children)
            {
                child.Dispose();
            }
            base.Dispose();
        }
    }

    private sealed class RootChildPage : ReactivePage<StepViewModel>
    {
        public static RootChildPage? Last;

        public RootChildPage() => Last = this;

        public TrackingInvalidatingNode Root { get; } = new("frame");

        public override ILayoutNode BuildLayout() => Root;
    }

    private sealed class ToastServiceProvider : IServiceProvider
    {
        private readonly IToastService _toastService = new ToastService();

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IToastService))
                return _toastService;

            if (serviceType == typeof(IEnumerable<IInputSource>))
                return Array.Empty<IInputSource>();

            return null;
        }
    }

    private sealed class OtherPage : ReactivePage<StepViewModel>
    {
        public override ILayoutNode BuildLayout() => new TextNode("other");
    }
}
