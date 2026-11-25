using System.Windows;
using System.Windows.Threading;

namespace PeripheralBattery;

public partial class App : Application
{
    private Mutex? _instance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var created = false;
        try
        {
            _instance = new Mutex(true, @"Local\PeripheralBattery.Tray", out created);
        }
        catch (AbandonedMutexException)
        {
            created = true;
        }

        if (!created)
        {
            Shutdown();
            return;
        }

        var demo = e.Args.Any(arg => string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase));
        _controller = new AppController(demo);
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine(e.Exception);
        e.Handled = true;
    }
}
