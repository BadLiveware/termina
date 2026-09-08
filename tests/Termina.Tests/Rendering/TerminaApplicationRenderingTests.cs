using R3;
using Termina.Input;
using Termina.Layout;
using Termina.Notifications;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Rendering;

public sealed class TerminaApplicationRenderingTests
{
    [Fact]
    public void RenderCurrentPage_RendersToastWithoutRetainingFrameWrappers()
    {
        var root = new TrackingInvalidatingNode();
        TestPage.Root = root;
        var toastService = new TestToastService();
        var terminal = new VirtualTerminal(40, 10);
        var app = new TerminaApplication(
            terminal,
            new TestServiceProvider(toastService));
        app.RegisterRoute<TestPage, TestViewModel>("/test");
        app.NavigateTo("/test");

        try
        {
            Assert.Equal(1, root.SubscriptionCount);

            toastService.Show("toast", new ToastOptions(Position: ToastPosition.TopLeft));
            for (var index = 0; index < 32; index++)
                InvokeRenderCurrentPage(app);

            Assert.Equal(1, root.SubscriptionCount);
            var output = terminal.ToString();
            Assert.Contains("base", output);
            Assert.Contains("toast", output);
        }
        finally
        {
            app.Dispose();
            TestPage.Root = null;
        }

        Assert.Equal(0, root.SubscriptionCount);
    }

    private static void InvokeRenderCurrentPage(TerminaApplication app)
    {
        var method = typeof(TerminaApplication).GetMethod(
            "RenderCurrentPage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        method!.Invoke(app, []);
    }

    private sealed class TestPage : ReactivePage<TestViewModel>
    {
        public static TrackingInvalidatingNode? Root { get; set; }

        public override ILayoutNode BuildLayout() => Root ?? (ILayoutNode)new EmptyNode();
    }

    private sealed class TestViewModel : ReactiveViewModel;

    private sealed class TrackingInvalidatingNode : LayoutNode, IInvalidatingNode
    {
        private readonly Subject<Unit> _source = new();
        private readonly Observable<Unit> _invalidated;

        public TrackingInvalidatingNode()
        {
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

        public override Size Measure(Size available) => available;

        public override void Render(IRenderContext context, Rect bounds) =>
            context.WriteAt(0, 0, "base");
    }

    private sealed class TestToastService : IToastService
    {
        private readonly Subject<ToastMessage?> _current = new();

        public Observable<ToastMessage?> CurrentToast => _current;

        public void Show(string message, ToastOptions? options = null) =>
            _current.OnNext(new ToastMessage(
                message,
                options?.Position ?? ToastPosition.BottomRight,
                options?.Color,
                options?.Icon));
    }

    private sealed class TestServiceProvider(IToastService toastService) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType switch
        {
            _ when serviceType == typeof(IToastService) => toastService,
            _ when serviceType == typeof(IEnumerable<IInputSource>) => Array.Empty<IInputSource>(),
            _ => null,
        };
    }
}
