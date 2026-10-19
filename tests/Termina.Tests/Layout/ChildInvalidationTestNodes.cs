// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// A long-lived child node that counts the open subscriptions to its <see cref="Invalidated"/> stream.
/// </summary>
internal sealed class TrackingInvalidatingNode : LayoutNode, IInvalidatingNode
{
    private readonly Subject<Unit> _source = new();

    public TrackingInvalidatingNode(string text = "shared")
    {
        Text = text;
        Invalidated = Observable.Create<Unit>(observer =>
        {
            SubscriptionCount++;
            var sourceSubscription = _source.Subscribe(observer);
            return Disposable.Create(() =>
            {
                sourceSubscription.Dispose();
                SubscriptionCount--;
            });
        });
    }

    public Observable<Unit> Invalidated { get; }

    public int SubscriptionCount { get; private set; }

    public bool IsActive { get; private set; }

    public int ActivationCount { get; private set; }

    public string Text { get; private set; }

    public void Change(string text)
    {
        Text = text;
        Invalidate();
    }

    public void Invalidate() => _source.OnNext(Unit.Default);

    public override void OnActivate()
    {
        IsActive = true;
        ActivationCount++;
        base.OnActivate();
    }

    public override void OnDeactivate()
    {
        IsActive = false;
        base.OnDeactivate();
    }

    public override Size Measure(Size available) =>
        new(Math.Min(available.Width, DisplayWidth.GetColumnCount(Text)), Math.Min(available.Height, 1));

    public override void Render(IRenderContext context, Rect bounds) =>
        context.WriteAt(bounds.X, bounds.Y, DisplayWidth.TruncateToColumns(Text, bounds.Width));
}

internal enum WrapperStep
{
    Only,
}

/// <summary>
/// Builds each framework node that forwards the invalidation of a child that the caller supplies.
/// </summary>
internal static class ChildInvalidationWrappers
{
    public const string Stack = nameof(StackLayout);
    public const string Vertical = nameof(VerticalLayout);
    public const string Horizontal = nameof(HorizontalLayout);
    public const string Panel = nameof(PanelNode);
    public const string Scrollable = nameof(ScrollableContainerNode);
    public const string Grid = nameof(GridNode);
    public const string Dynamic = nameof(DynamicLayoutNode);
    public const string KeyedDynamic = "KeyedDynamicLayoutNode";
    public const string Reactive = nameof(ReactiveLayoutNode);
    public const string ReactiveTransform = "ReactiveLayoutNodeOfT";
    public const string Wizard = "WizardNode";

    public static readonly string[] All =
    [
        Stack, Vertical, Horizontal, Panel, Scrollable, Grid, Dynamic, KeyedDynamic, Reactive, ReactiveTransform, Wizard,
    ];

    public static readonly string[] Attachable = [Stack, Vertical, Horizontal, Panel, Scrollable, Grid];

    public static readonly string[] Replaceable = [Panel, Scrollable, Grid];

    public static TheoryData<string> AllWrappers() => new(All);

    public static TheoryData<string> AttachableWrappers() => new(Attachable);

    public static TheoryData<string> ReplaceableWrappers() => new(Replaceable);

    /// <summary>
    /// Measures and renders the node, as one frame of the render loop does. A wizard builds its content
    /// only when it renders.
    /// </summary>
    public static void Layout(ILayoutNode node, Size available)
    {
        var terminal = new VirtualTerminal(available.Width, available.Height);
        node.Measure(available);
        node.Render(
            new RegionRenderContext(terminal, 0, 0, available.Width, available.Height),
            new Rect(0, 0, available.Width, available.Height));
    }

    public static LayoutNode Wrap(string wrapper, ILayoutNode child) => wrapper switch
    {
        Stack => new StackLayout([child]),
        Vertical => new VerticalLayout([child]),
        Horizontal => new HorizontalLayout([child]),
        Panel => new PanelNode().WithContent(child),
        Scrollable => new ScrollableContainerNode().WithContent(child),
        Grid => new GridNode().AddRow(child),
        Dynamic => new DynamicLayoutNode(() => child),
        KeyedDynamic => new KeyedDynamicLayoutNode<int>(() => 0, _ => child),
        Reactive => new ReactiveLayoutNode(Observable.Never<ILayoutNode>(), child),
        ReactiveTransform => new ReactiveLayoutNode<int>(Observable.Return(0), _ => child),
        Wizard => new WizardNode<WrapperStep>().WithStep(WrapperStep.Only, "Step", () => child),
        _ => throw new ArgumentOutOfRangeException(nameof(wrapper), wrapper, "Unknown wrapper."),
    };

    public static LayoutNode CreateEmpty(string wrapper) => wrapper switch
    {
        Stack => new StackLayout([]),
        Vertical => new VerticalLayout([]),
        Horizontal => new HorizontalLayout([]),
        Panel => new PanelNode(),
        Scrollable => new ScrollableContainerNode(),
        Grid => new GridNode(),
        _ => throw new ArgumentOutOfRangeException(nameof(wrapper), wrapper, "The wrapper has no attach API."),
    };

    public static void Attach(LayoutNode wrapper, ILayoutNode child)
    {
        switch (wrapper)
        {
            case StackLayout stack:
                stack.WithChild(child);
                break;
            case VerticalLayout vertical:
                vertical.WithChild(child);
                break;
            case HorizontalLayout horizontal:
                horizontal.WithChild(child);
                break;
            case PanelNode panel:
                panel.WithContent(child);
                break;
            case ScrollableContainerNode scrollable:
                scrollable.WithContent(child);
                break;
            case GridNode grid:
                grid.SetCell(0, 0, child);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(wrapper), wrapper.GetType().Name, "The wrapper has no attach API.");
        }
    }
}
