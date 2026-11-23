using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using x_pad.Models;
using x_pad.ViewModels;

namespace x_pad.Views;

public partial class PreferencesWindow : Window
{
    private readonly AppSettings _original;

    public AppSettings? Result { get; private set; }

    public PreferencesWindow() : this(new AppSettings())
    {
    }

    public PreferencesWindow(AppSettings settings, Func<Task>? checkUpdates = null)
    {
        _original = settings;
        InitializeComponent();
        DataContext = new PreferencesViewModel(settings, checkUpdates);
        AddHandler(InputElement.KeyDownEvent, OnShortcutCaptureKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnShortcutCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox box || !box.Classes.Contains("shortcutCapture"))
            return;
        if (box.DataContext is not ShortcutBindingItem item)
            return;

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab)
            return;

        if (e.Key == Key.Escape)
        {
            item.Gesture = item.DefaultGesture;
            e.Handled = true;
            if (DataContext is PreferencesViewModel escVm)
                escVm.ValidateShortcuts();
            return;
        }

        item.Gesture = new KeyGesture(e.Key, e.KeyModifiers).ToString();
        e.Handled = true;
        if (DataContext is PreferencesViewModel vm)
            vm.ValidateShortcuts();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PreferencesViewModel vm)
        {
            Close(false);
            return;
        }

        if (vm.ValidateShortcuts() != null)
            return;

        Result = vm.ToSettings(_original);
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
