namespace x_pad.Editor;

/// <summary>Detects whether a buffer prefers tab or space indentation.</summary>
public static class IndentStyle
{
    /// <summary>
    /// Resolves insert-spaces for a buffer. More tab-indented lines than space-indented
    /// lines means tabs. No indented lines, or a tie, keeps <paramref name="preferInsertSpaces"/>.
    /// </summary>
    public static bool ResolveInsertSpaces(string? text, bool preferInsertSpaces)
    {
        if (string.IsNullOrEmpty(text))
            return preferInsertSpaces;

        var tabLines = 0;
        var spaceLines = 0;
        var start = 0;

        while (start <= text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
                end = text.Length;

            var lineEnd = end;
            if (lineEnd > start && text[lineEnd - 1] == '\r')
                lineEnd--;

            ClassifyLeadingWhitespace(text.AsSpan(start, lineEnd - start), ref tabLines, ref spaceLines);

            if (end >= text.Length)
                break;
            start = end + 1;
        }

        if (tabLines == 0 && spaceLines == 0)
            return preferInsertSpaces;
        if (tabLines == spaceLines)
            return preferInsertSpaces;
        return spaceLines > tabLines;
    }

    public static string ToDisplay(bool insertSpaces) => insertSpaces ? "Spaces" : "Tabs";

    private static void ClassifyLeadingWhitespace(ReadOnlySpan<char> line, ref int tabLines, ref int spaceLines)
    {
        if (line.IsEmpty)
            return;

        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;

        if (i == 0)
            return;

        // First indent character decides the line's style (VS Code / EditorConfig-like).
        if (line[0] == '\t')
            tabLines++;
        else
            spaceLines++;
    }
}
