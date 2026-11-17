using System;
using System.Collections.Generic;
using AvaloniaEdit.Document;

namespace x_pad.Editor;

public static class InvertSelection
{
    /// <summary>
    /// Returns complementary ranges for a single contiguous selection.
    /// Empty selection → whole document. Full selection → empty list (nothing left).
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> ComputeRanges(
        int documentLength,
        int selectionStart,
        int selectionLength)
    {
        if (documentLength <= 0)
            return [];

        selectionStart = Math.Clamp(selectionStart, 0, documentLength);
        selectionLength = Math.Clamp(selectionLength, 0, documentLength - selectionStart);

        if (selectionLength == 0)
            return [(0, documentLength)];

        var ranges = new List<(int, int)>(2);
        if (selectionStart > 0)
            ranges.Add((0, selectionStart));
        var end = selectionStart + selectionLength;
        if (end < documentLength)
            ranges.Add((end, documentLength - end));
        return ranges;
    }

    public static IReadOnlyList<(int Start, int Length)> ComputeRanges(TextDocument document, int selectionStart, int selectionLength) =>
        ComputeRanges(document.TextLength, selectionStart, selectionLength);
}
