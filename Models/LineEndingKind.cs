namespace x_pad.Models;

public enum LineEndingKind
{
    Lf,
    Crlf,
    Cr
}

public static class LineEndingKindExtensions
{
    public static string ToDisplay(this LineEndingKind kind) => kind switch
    {
        LineEndingKind.Crlf => "CRLF",
        LineEndingKind.Cr => "CR",
        _ => "LF"
    };

    public static string ToNewLine(this LineEndingKind kind) => kind switch
    {
        LineEndingKind.Crlf => "\r\n",
        LineEndingKind.Cr => "\r",
        _ => "\n"
    };

    public static LineEndingKind Detect(string text)
    {
        if (string.IsNullOrEmpty(text))
            return LineEndingKind.Lf;

        var crlf = 0;
        var lf = 0;
        var cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf >= lf && crlf >= cr && crlf > 0)
            return LineEndingKind.Crlf;
        if (cr > lf && cr > 0)
            return LineEndingKind.Cr;
        return LineEndingKind.Lf;
    }
}
