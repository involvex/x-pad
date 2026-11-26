using System;
using Dock.Model.Avalonia.Controls;
using x_pad.Views;

namespace x_pad.ViewModels.Dock;

public sealed class DebugToolViewModel : Tool
{
    public AiDebugPanel Panel { get; } = new() { Name = "AiDebugHost" };

    public DebugToolViewModel()
    {
        Id = "AiDebug";
        Title = "AI Debug";
        CanClose = true;
        CanPin = true;
        CanFloat = true;
        Content = new Func<IServiceProvider, object>(_ => Panel);
    }
}
