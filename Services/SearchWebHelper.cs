using System;
using System.Diagnostics;

namespace x_pad.Services;

public static class SearchWebHelper
{
    public static string BuildGoogleSearchUrl(string query)
    {
        var q = Uri.EscapeDataString(query.Trim());
        return "https://www.google.com/search?q=" + q;
    }

    public static bool TryOpen(string query, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(query))
        {
            error = "Nothing to search";
            return false;
        }

        try
        {
            var url = BuildGoogleSearchUrl(query);
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
