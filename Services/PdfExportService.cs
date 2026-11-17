using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace x_pad.Services;

public static class PdfExportService
{
    static PdfExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static void ExportPlainText(string path, string title, string text, string fontFamily = "Consolas", float fontSize = 10f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        text ??= "";
        title = string.IsNullOrWhiteSpace(title) ? "X-Pad" : title;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontFamily(fontFamily).FontSize(fontSize));

                page.Header().Column(col =>
                {
                    col.Item().Text(title).SemiBold().FontSize(12);
                    col.Item().PaddingBottom(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                });

                page.Content().Text(text);

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf(path);
    }
}
