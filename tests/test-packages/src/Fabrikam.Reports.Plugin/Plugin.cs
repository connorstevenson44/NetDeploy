// A report format plugin. Fabrikam.Reports loads every dll in plugins\ and calls Plugin.Render.
using System.Collections.Generic;
using System.Linq;

public static class Plugin
{
#if FORMAT_PDF
    public const string Format = "pdf";
    public static string Render(string title, IList<string[]> rows) =>
        $"%PDF-1.4 (pretend)\n{title}\n" + string.Join("\n", rows.Select(row => string.Join("    ", row)));
#elif FORMAT_XLSX
    public const string Format = "xlsx";
    public static string Render(string title, IList<string[]> rows) =>
        $"<sheet name=\"{title}\">\n" + string.Join("\n", rows.Select(row => "  <row>" + string.Concat(row.Select(cell => $"<c>{cell}</c>")) + "</row>")) + "\n</sheet>";
#else
    public const string Format = "csv";
    public static string Render(string title, IList<string[]> rows) =>
        $"# {title}\n" + string.Join("\n", rows.Select(row => string.Join(",", row)));
#endif
}
