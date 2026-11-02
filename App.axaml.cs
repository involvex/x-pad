using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using x_pad.ViewModels;
using x_pad.Views;

namespace x_pad;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(args),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}