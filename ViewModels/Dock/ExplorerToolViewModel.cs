using Dock.Model.Avalonia.Controls;
using x_pad.ViewModels;

namespace x_pad.ViewModels.Dock;

public sealed class ExplorerToolViewModel : Tool
{
    public FileTreeViewModel? FileTree { get; set; }

    public ExplorerToolViewModel()
    {
        Id = "Explorer";
        Title = "Explorer";
        CanClose = true;
        CanPin = true;
        CanFloat = true;
    }
}
