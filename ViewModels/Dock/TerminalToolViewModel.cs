using System;
using Dock.Model.Avalonia.Controls;
using x_pad.Views;

namespace x_pad.ViewModels.Dock;

/// <summary>
/// Holds a single <see cref="TerminalPanel"/> instance so ConPTY survives show/hide and re-templates.
/// </summary>
public sealed class TerminalToolViewModel : Tool
{
    public TerminalPanel Panel { get; } = new() { Name = "TerminalHost" };

    public TerminalToolViewModel()
    {
        Id = "Terminal";
        Title = "Terminal";
        CanClose = true;
        CanPin = true;
        CanFloat = true;
        // Dock renders Tool.Content. Application data templates are not used for tools.
        Content = new Func<IServiceProvider, object>(_ => Panel);
    }
}
