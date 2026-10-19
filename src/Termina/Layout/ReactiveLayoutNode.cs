// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Rendering;

namespace Termina.Layout;

/// <summary>
/// A layout node that updates its content based on an observable stream.
/// When the observable emits, the child node is replaced and invalidation is signaled.
/// </summary>
public sealed class ReactiveLayoutNode : LayoutNode, IInvalidatingNode
{
    private readonly Observable<ILayoutNode> _source;
    private IDisposable? _subscription;
    private readonly ChildInvalidationLink _childInvalidation = new();
    private ILayoutNode _currentChild;
    private Size _lastMeasuredSize;
    private bool _isActive = false;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _childInvalidation.Invalidated;

    /// <summary>
    /// Create a reactive layout node from an observable of layout nodes.
    /// </summary>
    public ReactiveLayoutNode(Observable<ILayoutNode> source, ILayoutNode? initialChild = null)
    {
        _source = source;
        _currentChild = initialChild ?? new EmptyNode();
        _childInvalidation.Attach(_currentChild);
        _subscription = source.Subscribe(ReplaceChild);
    }

    private void ReplaceChild(ILayoutNode node)
    {
        // Deactivate old child instead of disposing (active/inactive pattern)
        if (_currentChild is IActivatableNode oldChild)
        {
            oldChild.OnDeactivate();
        }
        _childInvalidation.Replace(_currentChild, node);
        _currentChild = node;
        ApplyRuntimeContextToChild(node);

        // Activate the new child if we're currently active
        if (_isActive && node is IActivatableNode newChildNode)
        {
            newChildNode.OnActivate();
        }

        RuntimeContext?.NotifyLayoutStructureChanged();
        _childInvalidation.Emit();
    }

    /// <inheritdoc />
    internal override IEnumerable<ILayoutNode> GetChildNodes() => [_currentChild];

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        var childSize = _currentChild.Measure(available);

        // Apply our own constraints
        var width = WidthConstraint.Compute(available.Width, childSize.Width, available.Width);
        var height = HeightConstraint.Compute(available.Height, childSize.Height, available.Height);

        _lastMeasuredSize = new Size(width, height);
        return _lastMeasuredSize;
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        _currentChild.Render(context, bounds);
    }

    /// <inheritdoc />
    public override void OnActivate()
    {
        // Recreate the source subscription before the node becomes active. A source that replays its
        // current value then only swaps the child, and the child is activated one time below.
        _subscription ??= _source.Subscribe(ReplaceChild);

        _isActive = true;

        // Activate current child if it's a LayoutNode
        if (_currentChild is IActivatableNode childNode)
        {
            childNode.OnActivate();
        }

        base.OnActivate();
    }

    /// <inheritdoc />
    public override void OnDeactivate()
    {
        _isActive = false;

        // Deactivate current child if it's a LayoutNode
        if (_currentChild is IActivatableNode childNode)
        {
            childNode.OnDeactivate();
        }

        // Dispose subscription to pause updates
        _subscription?.Dispose();
        _subscription = null;

        base.OnDeactivate();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _subscription?.Dispose();
        _childInvalidation.Dispose();
        _currentChild.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// A layout node that updates based on an observable of values, transformed to layout nodes.
/// </summary>
public sealed class ReactiveLayoutNode<T> : LayoutNode, IInvalidatingNode
{
    private readonly Observable<T> _source;
    private readonly Func<T, ILayoutNode> _transform;
    private IDisposable? _subscription;
    private readonly ChildInvalidationLink _childInvalidation = new();
    private ILayoutNode _currentChild;
    private bool _isActive = false;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _childInvalidation.Invalidated;

    /// <summary>
    /// Create a reactive layout node from an observable with a transform function.
    /// </summary>
    public ReactiveLayoutNode(Observable<T> source, Func<T, ILayoutNode> transform)
    {
        _source = source;
        _transform = transform;
        _currentChild = new EmptyNode();
        _subscription = source.Subscribe(ReplaceChild);
    }

    private void ReplaceChild(T value)
    {
        // Deactivate old child instead of disposing (active/inactive pattern)
        if (_currentChild is IActivatableNode oldChild)
        {
            oldChild.OnDeactivate();
        }
        var newChild = _transform(value);
        _childInvalidation.Replace(_currentChild, newChild);
        _currentChild = newChild;
        ApplyRuntimeContextToChild(newChild);

        // Activate the new child if we're currently active
        if (_isActive && newChild is IActivatableNode newChildNode)
        {
            newChildNode.OnActivate();
        }

        RuntimeContext?.NotifyLayoutStructureChanged();
        _childInvalidation.Emit();
    }

    /// <inheritdoc />
    internal override IEnumerable<ILayoutNode> GetChildNodes() => [_currentChild];

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        var childSize = _currentChild.Measure(available);

        var width = WidthConstraint.Compute(available.Width, childSize.Width, available.Width);
        var height = HeightConstraint.Compute(available.Height, childSize.Height, available.Height);

        return new Size(width, height);
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        _currentChild.Render(context, bounds);
    }

    /// <inheritdoc />
    public override void OnActivate()
    {
        // Recreate the source subscription before the node becomes active. A source that replays its
        // current value then only swaps the child, and the child is activated one time below.
        _subscription ??= _source.Subscribe(ReplaceChild);

        _isActive = true;

        // Activate current child if it's a LayoutNode
        if (_currentChild is IActivatableNode childNode)
        {
            childNode.OnActivate();
        }

        base.OnActivate();
    }

    /// <inheritdoc />
    public override void OnDeactivate()
    {
        _isActive = false;

        // Deactivate current child if it's a LayoutNode
        if (_currentChild is IActivatableNode childNode)
        {
            childNode.OnDeactivate();
        }

        // Dispose subscription to pause updates
        _subscription?.Dispose();
        _subscription = null;

        base.OnDeactivate();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _subscription?.Dispose();
        _childInvalidation.Dispose();
        _currentChild.Dispose();
        base.Dispose();
    }
}
