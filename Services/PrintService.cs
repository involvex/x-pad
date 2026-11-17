using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.Versioning;

namespace x_pad.Services;

[SupportedOSPlatform("windows")]
public sealed class PrintService
{
    public IReadOnlyList<string> GetPrinters()
    {
        try
        {
            return PrinterSettings.InstalledPrinters.Cast<string>().ToList();
        }
        catch
        {
            return [];
        }
    }

    public void PrintPlainText(
        string documentName,
        string text,
        string? printerName = null,
        string fontFamily = "Consolas",
        float fontSize = 10f)
    {
        using var document = new PrintDocument();
        document.DocumentName = string.IsNullOrWhiteSpace(documentName) ? "X-Pad" : documentName;
        if (!string.IsNullOrWhiteSpace(printerName))
            document.PrinterSettings.PrinterName = printerName;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var index = 0;

        document.PrintPage += (_, e) =>
        {
            if (e.Graphics == null)
            {
                e.HasMorePages = false;
                return;
            }

            using var font = new Font(fontFamily, fontSize);
            var lineHeight = font.GetHeight(e.Graphics);
            var y = (float)e.MarginBounds.Top;
            var left = (float)e.MarginBounds.Left;
            var bottom = (float)e.MarginBounds.Bottom;

            while (index < lines.Length)
            {
                if (y + lineHeight > bottom)
                {
                    e.HasMorePages = true;
                    return;
                }

                e.Graphics.DrawString(lines[index], font, Brushes.Black, left, y);
                y += lineHeight;
                index++;
            }

            e.HasMorePages = false;
        };

        document.Print();
    }
}
