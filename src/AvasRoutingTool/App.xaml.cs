using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AvasRoutingTool.Logging;

namespace AvasRoutingTool;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Crash-capture seam: every unhandled exception ends up in app.log before the process dies.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLogger.Error("CRASH", "Unhandled AppDomain exception (terminating=" + args.IsTerminating + ")", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error("CRASH", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("CRASH", "Unhandled UI-thread exception", e.Exception);
    }
}
