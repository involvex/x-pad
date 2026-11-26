using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace x_pad.Services.Ai;

/// <summary>Ring-buffer debug log for AI HTTP traffic (API keys redacted).</summary>
public sealed class AiDebugLog
{
    public static AiDebugLog Shared { get; } = new();

    private static readonly Regex BearerRedact =
        new(@"Bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JsonKeyRedact =
        new(@"""(api[_-]?key|authorization)""\s*:\s*""[^""]*""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly object _gate = new();
    private readonly List<string> _lines = new(MaxEntries);
    public const int MaxEntries = 300;

    public bool Enabled { get; set; }

    public event EventHandler? Changed;

    public void Clear()
    {
        lock (_gate)
            _lines.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Write(string category, string message)
    {
        if (!Enabled)
            return;

        var line = $"{DateTime.Now:HH:mm:ss.fff} [{category}] {Redact(message)}";
        lock (_gate)
        {
            _lines.Add(line);
            while (_lines.Count > MaxEntries)
                _lines.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string GetText()
    {
        lock (_gate)
            return string.Join(Environment.NewLine, _lines);
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
            return _lines.ToArray();
    }

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var s = BearerRedact.Replace(text, "Bearer ***");
        s = JsonKeyRedact.Replace(s, "\"$1\":\"***\"");
        return s;
    }

    public static string SummarizeBody(string? json, int maxChars = 1200)
    {
        if (string.IsNullOrEmpty(json))
            return "";
        var s = Redact(json);
        if (s.Length <= maxChars)
            return s;
        return s[..maxChars] + "…";
    }
}
