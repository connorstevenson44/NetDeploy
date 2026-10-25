// Contoso Orders Export: a .NET Framework tool run nightly by a scheduled task. It writes an export file with the
// settings from its .exe.config, so the result of a config merge is easy to see.
//   Contoso.Orders.Export.exe [--check]
using System;
using System.Configuration;
using System.IO;
using System.Linq;

internal static class Program
{
    private static int Main(string[] args)
    {
        var version = typeof(Program).Assembly.GetName().Version;
        var settings = ConfigurationManager.AppSettings;
        var connection = ConfigurationManager.ConnectionStrings["Orders"]?.ConnectionString ?? "(not set)";
        var lines = settings.AllKeys.OrderBy(key => key).Select(key => $"  {key} = {settings[key]}").ToList();
        lines.Insert(0, $"Contoso Orders Export {version.ToString(3)}");
        lines.Add($"  connection Orders = {connection}");

        if (args.Contains("--check"))
        {
            lines.ForEach(Console.WriteLine);
            var missing = new[] { "ExportFolder", "BatchSize" }.Where(key => string.IsNullOrEmpty(settings[key])).ToList();
            missing.ForEach(key => Console.WriteLine($"  MISSING {key}"));
            Console.WriteLine(missing.Count == 0 ? "Check passed." : "Check failed.");
            return missing.Count == 0 ? 0 : 1;
        }

        var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settings["ExportFolder"] ?? "exports");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"export-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(file, lines);
        Console.WriteLine($"Wrote {file}");
        return 0;
    }
}
