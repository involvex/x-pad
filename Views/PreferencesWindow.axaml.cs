using System;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm)
            Result = vm.ToSettings(_original);
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
