// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using R3;
using Termina.Hosting;
using Termina.Input;
using Termina.Layout;
using Termina.Pages;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Rendering;

/// <summary>
/// The render loop coalesces renders to one per render frame. An event marks the page
/// dirty and requests a frame; the frame renders the page one time. A burst of events
/// inside one frame interval therefore costs one layout pass, and a frame that finds no
/// dirty page costs none.
/// </summary>
public class FrameCoalescedRenderingTests
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    [Fact]
    public void Event_burst_inside_one_frame_interval_renders_one_time()
    {
        var timeProvider = new FakeTimeProvider();
        var app = CreateApp(timeProvider);
        var layout = ActivateCountingPage(app);

        for (var i = 0; i < 5; i++)
            app.Post(() => { });

        DrainEvents(app);
        Assert.Equal(0, layout.RenderCount);

        timeProvider.Advance(FrameInterval);
        DrainEvents(app);

        Assert.Equal(1, layout.RenderCount);
    }

    [Fact]
    public void Frame_without_a_dirty_page_does_not_render()
    {
        var timeProvider = new FakeTimeProvider();
        var app = CreateApp(timeProvider);
        var layout = ActivateCountingPage(app);

        var subject = new Subject<int>();
        using var subscription = subject
            .ObserveOn(app.RenderFrameProvider)
            .Subscribe(_ => { });

        subject.OnNext(1);
        DrainEvents(app);

        timeProvider.Advance(FrameInterval);
        DrainEvents(app);

        Assert.Equal(0, layout.RenderCount);
        Assert.True(app.RenderFrameProvider.GetFrameCount() > 0);
    }

    [Fact]
    public void Single_event_renders_inside_one_frame_interval()
    {
        var timeProvider = new FakeTimeProvider();
        var app = CreateApp(timeProvider);
        var layout = ActivateCountingPage(app);

        app.RequestRedraw();
        DrainEvents(app);

        timeProvider.Advance(FrameInterval - TimeSpan.FromMilliseconds(1));
        DrainEvents(app);
        Assert.Equal(0, layout.RenderCount);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        DrainEvents(app);

        Assert.Equal(1, layout.RenderCount);
    }

    [Fact]
    public void Input_event_marks_the_page_dirty()
    {
        var timeProvider = new FakeTimeProvider();
        var app = CreateApp(timeProvider);
        var layout = ActivateCountingPage(app);

        InvokeProcessEventAndMaybeRender(
            app,
            new KeyPressed(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false)));

        Assert.Equal(0, layout.RenderCount);

        timeProvider.Advance(FrameInterval);
        DrainEvents(app);

        Assert.Equal(1, layout.RenderCount);
    }

    [Fact]
    public void Separate_bursts_render_one_time_each()
    {
        var timeProvider = new FakeTimeProvider();
        var app = CreateApp(timeProvider);
        var layout = ActivateCountingPage(app);

        app.Post(() => { });
        app.Post(() => { });
        DrainEvents(app);
        timeProvider.Advance(FrameInterval);
        DrainEvents(app);

        Assert.Equal(1, layout.RenderCount);

        app.Post(() => { });
        app.Post(() => { });
        DrainEvents(app);
        timeProvider.Advance(FrameInterval);
        DrainEvents(app);

        Assert.Equal(2, layout.RenderCount);
    }

    private static TerminaApplication CreateApp(TimeProvider timeProvider)
    {
        var options = new TerminaRuntimeOptions
        {
            TimeProvider = timeProvider,
            RenderFrameInterval = FrameInterval,
        };

        return new TerminaApplication(new VirtualTerminal(), options);
    }

    private static CountingLayoutNode ActivateCountingPage(TerminaApplication app)
    {
        app.RegisterRoute<CountingPage, IdleViewModel>("/counting");
        app.NavigateTo("/counting");
        DrainEvents(app);

        var page = (IBindablePage)GetCurrentPage(app);
        var layout = (CountingLayoutNode)page.LayoutRoot!;
        layout.Reset();
        return layout;
    }

    private static void DrainEvents(TerminaApplication app)
    {
        var channel = GetEventChannel(app);
        while (channel.Reader.TryRead(out var evt))
            InvokeProcessEventAndMaybeRender(app, evt);
    }

    private static Channel<object> GetEventChannel(TerminaApplication app)
    {
        var field = typeof(TerminaApplication).GetField(
            "_eventChannel", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (Channel<object>)field!.GetValue(app)!;
    }

    private static object GetCurrentPage(TerminaApplication app)
    {
        var field = typeof(TerminaApplication).GetField(
            "_currentPage", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return field!.GetValue(app)!;
    }

    private static void InvokeProcessEventAndMaybeRender(TerminaApplication app, object evt)
    {
        var method = typeof(TerminaApplication).GetMethod(
            "ProcessEventAndMaybeRender", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method!.Invoke(app, [evt]);
    }

    private sealed class IdleViewModel : ReactiveViewModel
    {
    }

    private sealed class CountingPage : ReactivePage<IdleViewModel>
    {
        public override ILayoutNode BuildLayout() => new CountingLayoutNode();
    }

    private sealed class CountingLayoutNode : ILayoutNode
    {
        public int RenderCount { get; private set; }

        public SizeConstraint WidthConstraint => SizeConstraint.AutoSize();

        public SizeConstraint HeightConstraint => SizeConstraint.AutoSize();

        public void Reset() => RenderCount = 0;

        public Size Measure(Size available) => new(0, 0);

        public void Render(IRenderContext context, Rect bounds) => RenderCount++;

        public void Dispose()
        {
        }
    }
}
