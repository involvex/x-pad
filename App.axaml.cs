using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using x_pad.Services;
using x_pad.ViewModels;
using x_pad.Views;

namespace x_pad;

public partial class App : Application
{
    public static SingleInstanceService? SingleInstance { get; set; }
    public static TrayService? Tray { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            var args = desktop.Args ?? Array.Empty<string>();
            var vm = new MainWindowViewModel(args);
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            try
            {
                Tray = new TrayService();
                Tray.Attach(vm);
            }
            catch
            {
                // Tray not available on this desktop environment.
            }

            desktop.Exit += (_, _) =>
            {
                Tray?.Dispose();
                Tray = null;
            };

            if (SingleInstance != null)
            {
                SingleInstance.FilesReceived += paths =>
                    Dispatcher.UIThread.Post(() => RouteExternalFiles(desktop, paths));
                SingleInstance.StartListening();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RouteExternalFiles(IClassicDesktopStyleApplicationLifetime desktop, string[] paths)
    {
        var target = desktop.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsActive)
                     ?? desktop.Windows.OfType<MainWindow>().FirstOrDefault()
                     ?? desktop.MainWindow as MainWindow;
        if (target?.DataContext is MainWindowViewModel vm)
            vm.OpenFilesFromExternal(paths);
    }
}
