using Avalonia;
using Avalonia.Threading;

namespace ChildNotes.UiDesignCheck;

internal static class HeadlessBootstrap
{
    private static Thread? _uiThread;
    private static DispatcherFrame? _mainFrame;
    private static readonly ManualResetEventSlim _ready = new();
    private static Exception? _initException;

    public static void Start()
    {
        _uiThread = new Thread(RunUiThread)
        {
            IsBackground = true,
            Name = "AvaloniaHeadlessUI"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        _ready.Wait();

        if (_initException is not null)
            throw new InvalidOperationException("Avalonia 无头引擎初始化失败", _initException);
    }

    public static T InvokeOnUi<T>(Func<T> action)
    {
        if (_uiThread is null)
            throw new InvalidOperationException("无头引擎未启动。");

        var tcs = new TaskCompletionSource<T>();
        Dispatcher.UIThread.Post(() =>
        {
            try { tcs.SetResult(action()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, DispatcherPriority.Normal);

        return tcs.Task.GetAwaiter().GetResult();
    }

    public static void Shutdown()
    {
        if (_mainFrame is not null)
        {
            Dispatcher.UIThread.Post(() => _mainFrame.Continue = false, DispatcherPriority.Send);
        }
    }

    private static void RunUiThread()
    {
        try
        {
            HeadlessApp.BuildAvaloniaApp().SetupWithoutStarting();
        }
        catch (Exception ex)
        {
            _initException = ex;
            _ready.Set();
            return;
        }

        _ready.Set();

        _mainFrame = new DispatcherFrame();
        Dispatcher.UIThread.PushFrame(_mainFrame);
    }
}
