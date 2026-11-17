namespace x_pad.Editor;

/// <summary>Pure auto-indent helpers (testable without AvaloniaEdit UI).</summary>
public static class AutoIndent
{
    public static string GetLeadingWhitespace(string text)
    {
        var i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
            i++;
        return text[..i];
    }

    /// <summary>
    /// Indent to insert on a new line after <paramref name="previousLineText"/>.
    /// Empty string means no indent insertion.
    /// </summary>
    public static string ComputeNextLineIndent(string previousLineText, int tabSize, bool insertSpaces)
    {
        var indent = GetLeadingWhitespace(previousLineText);
        var trimmed = previousLineText.TrimEnd();
        tabSize = Math.Max(1, tabSize);

        if (trimmed.EndsWith('{') || trimmed.EndsWith('[') || trimmed.EndsWith('(') || trimmed.EndsWith(':'))
            indent += insertSpaces ? new string(' ', tabSize) : "\t";

        return indent;
    }
}
