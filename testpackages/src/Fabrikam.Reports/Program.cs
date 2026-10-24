// Fabrikam Reports: a .NET Framework app that builds a report from the rows in its data folder, in each format a
// plugin in plugins\ provides. Every setting comes from Fabrikam.Reports.exe.config.
//   Fabrikam.Reports.exe --init   creates the data folder with sample rows, unless it has some already
//   Fabrikam.Reports.exe --run    writes one report per format to the output folder
//   Fabrikam.Reports.exe --check  prints the settings and plugins, and fails when a required setting is missing
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;

internal static class Program
{
    private static readonly string Home = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string Version = typeof(Program).Assembly.GetName().Version.ToString(3);

    private static int Main(string[] args)
    {
        var settings = ConfigurationManager.AppSettings;
        var dataFolder = Path.Combine(Home, settings["DataFolder"] ?? "data");
        var rowsFile = Path.Combine(dataFolder, "sales.csv");

        if (args.Contains("--init"))
        {
            Directory.CreateDirectory(dataFolder);
            if (File.Exists(rowsFile))
            {
                Console.WriteLine($"{rowsFile} is already there with {File.ReadAllLines(rowsFile).Length - 1} rows; left as it is.");
                return 0;
            }
            File.WriteAllLines(rowsFile, new[] { "Region,Month,Sales", "North,2026-07,1200", "South,2026-07,950", "North,2026-08,1340", "South,2026-08,1010", "West,2026-08,610" });
            Console.WriteLine($"Created {rowsFile} with 5 rows.");
            return 0;
        }

        var plugins = LoadPlugins();
        if (args.Contains("--check"))
        {
            Console.WriteLine($"Fabrikam Reports {Version}");
            foreach (var key in settings.AllKeys.OrderBy(key => key))
                Console.WriteLine($"  {key} = {(key.Contains("Password") ? "****" : settings[key])}");
            foreach (ConnectionStringSettings connection in ConfigurationManager.ConnectionStrings)
                Console.WriteLine($"  connection {connection.Name} = {connection.ConnectionString}");
            Console.WriteLine($"  plugins: {string.Join(", ", plugins.Keys)}");
            var missing = new[] { "ReportTitle", "OutputFolder", "DataFolder" }.Where(key => string.IsNullOrEmpty(settings[key])).ToList();
            missing.ForEach(key => Console.WriteLine($"  MISSING {key}"));
            Console.WriteLine(missing.Count == 0 ? "Check passed." : "Check failed.");
            return missing.Count == 0 ? 0 : 1;
        }

        if (!File.Exists(rowsFile))
        {
            Console.WriteLine($"No data yet: run Fabrikam.Reports.exe --init first.");
            return 2;
        }
        var rows = File.ReadAllLines(rowsFile).Skip(1).Select(line => line.Split(',')).ToList();
        var output = Directory.CreateDirectory(Path.Combine(Home, settings["OutputFolder"] ?? "reports")).FullName;
        var title = $"{settings["ReportTitle"]} ({Version})";
        foreach (var plugin in plugins)
        {
            var file = Path.Combine(output, $"report-{DateTime.Now:yyyyMMdd-HHmmss}.{plugin.Key}");
            File.WriteAllText(file, plugin.Value(title, rows));
            Console.WriteLine($"Wrote {file}");
        }
        File.AppendAllText(Path.Combine(dataFolder, "history.log"), $"{DateTime.Now:s} {Version} {rows.Count} rows, {string.Join("+", plugins.Keys)}{Environment.NewLine}");
        return 0;
    }

    /// <summary>Each plugin dll's format, such as csv, and its Render method.</summary>
    private static Dictionary<string, Func<string, IList<string[]>, string>> LoadPlugins()
    {
        var plugins = new Dictionary<string, Func<string, IList<string[]>, string>>();
        var folder = Path.Combine(Home, "plugins");
        if (!Directory.Exists(folder))
            return plugins;
        foreach (var dll in Directory.GetFiles(folder, "*.dll").OrderBy(name => name))
        {
            var type = Assembly.LoadFrom(dll).GetType("Plugin");
            var format = (string)type.GetField("Format").GetValue(null);
            var render = type.GetMethod("Render");
            plugins[format] = (title, rows) => (string)render.Invoke(null, new object[] { title, rows });
        }
        return plugins;
    }
}
