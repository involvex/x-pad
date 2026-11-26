using System;
using Avalonia.Controls;
using Avalonia.Threading;
using x_pad.Services.Ai;

namespace x_pad.Views;

public partial class AiDebugPanel : UserControl
{
    private readonly AiDebugLog _log;

    public AiDebugPanel() : this(AiDebugLog.Shared)
    {
    }

    public AiDebugPanel(AiDebugLog log)
    {
        _log = log;
        InitializeComponent();
        ClearButton.Click += (_, _) => _log.Clear();
        AttachedToVisualTree += (_, _) =>
        {
            _log.Changed += OnLogChanged;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => _log.Changed -= OnLogChanged;
    }

    private void OnLogChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);

    private void Refresh()
    {
        if (LogBox == null)
            return;
        LogBox.Text = _log.GetText();
        HintText.Text = _log.Enabled
            ? "Logging AI requests (keys redacted)."
            : "Debug mode is off — enable it in Preferences → AI.";
    }
}
