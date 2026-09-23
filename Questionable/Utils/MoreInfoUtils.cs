using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Questionable.Utils;

internal static class MoreInfoUtils
{
    internal static void SearchConsoleGamesWiki(string term)
    {
        var query = string.Join('&', new[]
        {
            ("search", term),
            ("title", "Special:Search"),
            ("go", "Go")
        }.Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));
        var uri = new UriBuilder("https", "ffxiv.consolegameswiki.com", 443, "mediawiki/index.php", $"?{query}");
        Process.Start(new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true });
    }
}
