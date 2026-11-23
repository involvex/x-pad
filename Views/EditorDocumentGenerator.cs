using System;
using Dock.Model.Controls;
using Dock.Model.Core;
using x_pad.Services;
using x_pad.Services.Lsp;
using x_pad.ViewModels;

namespace x_pad.Views;

/// <summary>
/// Builds a new <see cref="DocumentEditor"/> per document. The dock template otherwise
/// hands every tab the same control instance, so closing one leaves the next blank.
/// </summary>
public sealed class EditorDocumentGenerator : Dock.Model.Avalonia.Controls.DockItemContainerGenerator
{
    private readonly SettingsService _settings;
    private readonly LspManager _lsp;

    public EditorDocumentGenerator(SettingsService settings, LspManager lsp)
    {
        _settings = settings;
        _lsp = lsp;
    }

    public override void PrepareDocumentContainer(IItemsSourceDock dock, IDockable container, object item, int index)
    {
        base.PrepareDocumentContainer(dock, container, item, index);
        if (container is not IDocumentContent content || item is not DocumentViewModel vm)
            return;

        content.Content = new Func<IServiceProvider, object>(_ =>
        {
            var editor = new DocumentEditor { DataContext = vm };
            editor.ConfigureServices(_settings, _lsp);
            return editor;
        });
    }
}
