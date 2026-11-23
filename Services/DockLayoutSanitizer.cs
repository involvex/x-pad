using System.Collections.Generic;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace x_pad.Services;

/// <summary>
/// Strips ItemsSource-generated documents from a deserialized layout so open tabs
/// stay driven only by <see cref="ViewModels.MainWindowViewModel.Tabs"/>.
/// </summary>
public static class DockLayoutSanitizer
{
    public static void StripItemsSourceDocuments(IRootDock root)
    {
        foreach (var documentDock in EnumerateDockables<DocumentDock>(root))
        {
            if (documentDock.Id != "Documents" && documentDock.ItemsSource == null)
                continue;

            if (documentDock.VisibleDockables is { } visible)
            {
                for (var i = visible.Count - 1; i >= 0; i--)
                {
                    if (visible[i] is IDocument)
                        visible.RemoveAt(i);
                }
            }

            if (documentDock.ActiveDockable is IDocument)
                documentDock.ActiveDockable = null;
        }
    }

    private static IEnumerable<T> EnumerateDockables<T>(IDockable root) where T : class, IDockable
    {
        if (root is T match)
            yield return match;

        if (root is not IDock dock || dock.VisibleDockables == null)
            yield break;

        foreach (var child in dock.VisibleDockables)
        {
            foreach (var nested in EnumerateDockables<T>(child))
                yield return nested;
        }
    }
}