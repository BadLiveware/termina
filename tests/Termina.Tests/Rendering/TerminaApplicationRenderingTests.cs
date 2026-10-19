// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Time.Testing;
using R3;
using Termina.Input;
using Termina.Layout;
using Termina.Notifications;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Rendering;

public sealed class TerminaApplicationRenderingTests : IDisposable
{
    private const int TerminalWidth = 40;
    private const int TerminalHeight = 10;
    private const int RedrawCount = 32;
    private const char PageFill = 'P';
    private const char OtherPageFill = 'Q';
    private const string ToastText = "toast";
    private const string ToastIcon = "*";
    private const string FirstRoute = "/first";
    private const string SecondRoute = "/second";

    // The toast panel is 3 rows high. With ToastPosition.TopLeft it starts at column 1, row 1.
    // The text row is row 2. The text starts at column 2 with " * toast ".
    private const int PanelLeft = 1;
    private const int PanelTop = 1;
    private const int PanelHeight = 3;
    private const int TextRow = 2;
    private const int TextColumn = 5;
    private static readonly int PanelWidth = $" {ToastIcon} {ToastText} ".Length + 2;

    private readonly List<IDisposable> _disposables = [];

    public TerminaApplicationRenderingTests()
    {
        FirstPage.Root = null;
        SecondPage.Root = null;
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();

        FirstPage.Root = null;
        SecondPage.Root = null;
    }

    [Fact]
    public void Redraws_with_a_toast_keep_one_subscription_on_the_page_root()
    {
        var harness = CreateHarness();
        harness.Toasts.Show(ToastText, TopLeft);

        for (var index = 0; index < RedrawCount; index++)
            harness.Redraw();

        Assert.Equal(1, harness.FirstRoot.SubscriptionCount);
        Assert.Equal(RedrawCount, harness.FirstRoot.RenderCount);
        AssertToastDrawn(harness.Terminal);
    }

    [Fact]
    public void Page_root_subscription_is_released_when_the_application_is_disposed()
    {
        var harness = CreateHarness();
        harness.Toasts.Show(ToastText, TopLeft);
        harness.Redraw();

        harness.DisposeApp();

        Assert.Equal(0, harness.FirstRoot.SubscriptionCount);
    }

    [Fact]
    public void Toast_is_drawn_over_the_page_cells_and_the_page_is_intact_outside_the_toast()
    {
        var harness = CreateHarness();
        harness.Toasts.Show(ToastText, TopLeft);

        harness.Redraw();

        AssertToastDrawn(harness.Terminal);
        AssertPageFillOutsidePanel(harness.Terminal, PageFill);
    }

    [Fact]
    public void Toast_color_and_background_are_applied_to_the_overlapping_cells()
    {
        var harness = CreateHarness();
        harness.Toasts.Show(ToastText, new ToastOptions(Position: ToastPosition.TopLeft, Color: Color.Red, Icon: ToastIcon));

        harness.Redraw();

        Assert.Equal(Color.Red, harness.Terminal.GetForeground(PanelLeft, PanelTop));
        Assert.Equal(Color.White, harness.Terminal.GetForeground(TextColumn, TextRow));
        Assert.Equal(Color.Black, harness.Terminal.GetBackground(TextColumn, TextRow));
    }

    [Fact]
    public void Toast_expiry_reveals_the_page_cells_under_the_toast()
    {
        var harness = CreateHarness();
        var duration = TimeSpan.FromSeconds(3);
        harness.Toasts.Show(ToastText, new ToastOptions(Duration: duration, Position: ToastPosition.TopLeft, Icon: ToastIcon));
        harness.Redraw();
        AssertToastDrawn(harness.Terminal);

        harness.Time.Advance(duration - TimeSpan.FromMilliseconds(1));
        harness.Process();
        AssertToastDrawn(harness.Terminal);

        harness.Time.Advance(TimeSpan.FromMilliseconds(1));
        harness.Process();

        AssertAllCellsAre(harness.Terminal, PageFill);
    }

    [Fact]
    public void A_second_toast_replaces_the_first_and_restores_the_page_under_the_first()
    {
        var harness = CreateHarness();
        harness.Toasts.Show(ToastText, new ToastOptions(Position: ToastPosition.TopLeft, Icon: ToastIcon));
        harness.Redraw();

        harness.Toasts.Show(ToastText, new ToastOptions(Position: ToastPosition.BottomRight, Icon: ToastIcon));
        harness.Process();

        var bottomRightLeft = TerminalWidth - PanelWidth - 1;
        var bottomRightTop = TerminalHeight - PanelHeight - 1;
        Assert.Equal('╭', harness.Terminal.GetChar(bottomRightLeft, bottomRightTop));
        Assert.Equal(PageFill, harness.Terminal.GetChar(PanelLeft, PanelTop));
        Assert.Equal(PageFill, harness.Terminal.GetChar(TextColumn, TextRow));
    }

    [Fact]
    public void Navigation_while_a_toast_is_visible_moves_the_page_subscription_and_keeps_the_toast()
    {
        var harness = CreateHarness();
        harness.Toasts.Show(ToastText, TopLeft);
        harness.Redraw();

        harness.App.NavigateTo(SecondRoute);
        harness.Process();
        harness.Redraw();

        Assert.Equal(0, harness.FirstRoot.SubscriptionCount);
        Assert.Equal(1, harness.SecondRoot.SubscriptionCount);
        Assert.Equal(SecondRoute, harness.App.CurrentPath);
        AssertToastDrawn(harness.Terminal);
        AssertPageFillOutsidePanel(harness.Terminal, OtherPageFill);
    }

    [Fact]
    public void Redraws_after_navigation_keep_one_subscription_on_the_new_page_root()
    {
        var harness = CreateHarness();
        harness.Toasts.Show(ToastText, TopLeft);
        harness.App.NavigateTo(SecondRoute);
        harness.Process();

        for (var index = 0; index < RedrawCount; index++)
            harness.Redraw();

        Assert.Equal(1, harness.SecondRoot.SubscriptionCount);
        Assert.Equal(0, harness.FirstRoot.SubscriptionCount);
    }

    [Fact]
    public void Page_renders_without_an_overlay_when_no_toast_service_is_registered()
    {
        var harness = CreateHarness(registerToastService: false);

        harness.Redraw();

        AssertAllCellsAre(harness.Terminal, PageFill);
        Assert.Equal(1, harness.FirstRoot.SubscriptionCount);
    }

    [Fact]
    public void Page_renders_without_an_overlay_when_the_toast_service_shows_nothing()
    {
        var harness = CreateHarness();

        harness.Redraw();

        AssertAllCellsAre(harness.Terminal, PageFill);
    }

    [Fact]
    public void A_toast_change_requests_a_redraw_through_the_event_loop()
    {
        var harness = CreateHarness();
        harness.Process();
        var rendersBefore = harness.FirstRoot.RenderCount;

        harness.Toasts.Show(ToastText, TopLeft);
        harness.Process();

        Assert.Equal(rendersBefore + 1, harness.FirstRoot.RenderCount);
        AssertToastDrawn(harness.Terminal);
    }

    [Fact]
    public void Toast_is_skipped_when_the_terminal_is_too_short_for_the_panel()
    {
        var harness = CreateHarness(height: PanelHeight - 1);
        harness.Toasts.Show(ToastText, TopLeft);

        harness.Redraw();

        AssertAllCellsAre(harness.Terminal, PageFill);
    }

    private static ToastOptions TopLeft =>
        new(Position: ToastPosition.TopLeft, Icon: ToastIcon);

    private Harness CreateHarness(bool registerToastService = true, int height = TerminalHeight)
    {
        var time = new FakeTimeProvider();
        var toasts = new ToastService(time);
        var firstRoot = new FillNode(PageFill);
        var secondRoot = new FillNode(OtherPageFill);
        FirstPage.Root = firstRoot;
        SecondPage.Root = secondRoot;
        var terminal = new VirtualTerminal(TerminalWidth, height);
        var services = new TestServiceProvider(registerToastService ? toasts : null);
        var app = new TerminaApplication(terminal, services);
        app.RegisterRoute<FirstPage, TestViewModel>(FirstRoute);
        app.RegisterRoute<SecondPage, TestViewModel>(SecondRoute);
        app.NavigateTo(FirstRoute);
        var harness = new Harness(app, terminal, toasts, time, firstRoot, secondRoot);
        _disposables.Add(Disposable.Create(harness.DisposeApp));
        _disposables.Add(toasts);
        harness.Process();
        return harness;
    }

    private static void AssertToastDrawn(VirtualTerminal terminal)
    {
        Assert.Equal('╭', terminal.GetChar(PanelLeft, PanelTop));
        Assert.Equal('╮', terminal.GetChar(PanelLeft + PanelWidth - 1, PanelTop));
        Assert.Equal('╰', terminal.GetChar(PanelLeft, PanelTop + PanelHeight - 1));
        Assert.Equal('╯', terminal.GetChar(PanelLeft + PanelWidth - 1, PanelTop + PanelHeight - 1));
        Assert.Equal('│', terminal.GetChar(PanelLeft, TextRow));
        Assert.Equal(ToastText, terminal.GetRegion(TextColumn, TextRow, ToastText.Length, 1));
        Assert.Equal(ToastIcon[0], terminal.GetChar(PanelLeft + 2, TextRow));
    }

    private static void AssertPageFillOutsidePanel(VirtualTerminal terminal, char fill)
    {
        for (var y = 0; y < terminal.Height; y++)
        {
            for (var x = 0; x < terminal.Width; x++)
            {
                var insidePanel = x >= PanelLeft && x < PanelLeft + PanelWidth
                    && y >= PanelTop && y < PanelTop + PanelHeight;
                if (insidePanel)
                    continue;

                Assert.True(terminal.GetChar(x, y) == fill, $"Cell ({x},{y}) is '{terminal.GetChar(x, y)}', expected '{fill}'.");
            }
        }
    }

    private static void AssertAllCellsAre(VirtualTerminal terminal, char fill)
    {
        for (var y = 0; y < terminal.Height; y++)
        {
            for (var x = 0; x < terminal.Width; x++)
                Assert.True(terminal.GetChar(x, y) == fill, $"Cell ({x},{y}) is '{terminal.GetChar(x, y)}', expected '{fill}'.");
        }
    }

    private sealed class Harness(
        TerminaApplication app,
        VirtualTerminal terminal,
        ToastService toasts,
        FakeTimeProvider time,
        FillNode firstRoot,
        FillNode secondRoot)
    {
        public TerminaApplication App { get; } = app;
        public VirtualTerminal Terminal { get; } = terminal;
        public ToastService Toasts { get; } = toasts;
        public FakeTimeProvider Time { get; } = time;
        public FillNode FirstRoot { get; } = firstRoot;
        public FillNode SecondRoot { get; } = secondRoot;
        private bool _appDisposed;

        public void DisposeApp()
        {
            if (_appDisposed)
                return;

            _appDisposed = true;
            App.Dispose();
        }

        public void Process() => App.ProcessQueuedEvents(CancellationToken.None);

        public void Redraw()
        {
            App.RequestRedraw();
            Process();
        }
    }

    private sealed class FirstPage : ReactivePage<TestViewModel>
    {
        public static FillNode? Root { get; set; }

        public override ILayoutNode BuildLayout() => Root ?? (ILayoutNode)new EmptyNode();
    }

    private sealed class SecondPage : ReactivePage<TestViewModel>
    {
        public static FillNode? Root { get; set; }

        public override ILayoutNode BuildLayout() => Root ?? (ILayoutNode)new EmptyNode();
    }

    private sealed class TestViewModel : ReactiveViewModel;

    private sealed class FillNode : LayoutNode, IInvalidatingNode
    {
        private readonly char _fill;
        private readonly Subject<Unit> _source = new();
        private readonly Observable<Unit> _invalidated;

        public FillNode(char fill)
        {
            _fill = fill;
            _invalidated = Observable.Create<Unit>(observer =>
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

        public Observable<Unit> Invalidated => _invalidated;

        public int SubscriptionCount { get; private set; }

        public int RenderCount { get; private set; }

        public override Size Measure(Size available) => available;

        public override void Render(IRenderContext context, Rect bounds)
        {
            RenderCount++;
            var line = new string(_fill, bounds.Width);
            for (var y = 0; y < bounds.Height; y++)
                context.WriteAt(0, y, line);
        }
    }

    private sealed class TestServiceProvider(IToastService? toastService) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType switch
        {
            _ when serviceType == typeof(IToastService) => toastService,
            _ when serviceType == typeof(IEnumerable<IInputSource>) => Array.Empty<IInputSource>(),
            _ => null,
        };
    }
}
