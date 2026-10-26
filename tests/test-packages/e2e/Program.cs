using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetDeploy.Core.Config;
using NetDeploy.Core.Deployment;
using NetDeploy.Core.Installs;
using NetDeploy.Core.Manifest;

// Deploys, upgrades and removes both test packages on this machine without the UI, and checks each result.
//   dotnet run --project e2e -- <folder with the zips>
var dist = Path.GetFullPath(args[0]);
const string OrdersDir = @"C:\Apps\Contoso.Orders";
const string OrdersData = @"C:\ProgramData\Contoso.Orders";
const string ReportsDir = @"C:\Apps\Fabrikam.Reports";
var mainDir = Path.Combine(OrdersDir, "main");
var reportsDir = Path.Combine(ReportsDir, "main");
var failures = new List<string>();
var ordersValues = new Dictionary<string, string> { ["InstallDir"] = OrdersDir, ["DataDir"] = OrdersData, ["Environment"] = "Staging" };

// ---- Contoso.Orders: two installs side by side, then an upgrade of one of them
var log = await Deploy("Contoso.Orders-1.0.0", ordersValues, "main", dryRun: true);
Expect(!Directory.Exists(OrdersDir), "the dry run creates nothing");

log = await Deploy("Contoso.Orders-1.0.0", ordersValues, "main");
Expect(ServiceStatus("Contoso.Orders-main") == "Running", "main's service runs");
Expect(log.Contains("Applied migration 1.") && log.Contains("Seeded 3 customers and 4 orders."), "the database is created and seeded");
Expect(File.Exists(@$"{OrdersData}\db-main\orders.db"), "the database is outside the install folder");
Expect(Json(mainDir, "appsettings.json")["ConnectionStrings"]?["Orders"]?.ToString() == @$"Data Source={OrdersData}\db-main\orders.db", "the script pointed the service at its database");
Expect(TaskExists(@"\Contoso\Orders Export-main"), "the export task exists");
Expect(log.Contains("Not production (Staging)"), "the else branch ran for Staging");
Expect(log.Contains("Check passed."), "the 1.0.0 settings check passes");

log = await Deploy("Contoso.Orders-1.0.0", With(ordersValues, "Environment", "Production"), "blue");
var blueDir = Path.Combine(OrdersDir, "blue");
Expect(ServiceStatus("Contoso.Orders-blue") == "Running", "blue's service runs");
Expect(File.Exists(Path.Combine(blueDir, "PRODUCTION.txt")), "the production branch ran for blue");
Expect(File.Exists(@$"{OrdersData}\db-blue\orders.db"), "blue has its own database");
Expect(TaskExists(@"\Contoso\Orders Export-blue"), "blue has its own export task");

// Changes made on the server by hand, which the upgrade has to keep.
EditJson(Path.Combine(mainDir, "appsettings.json"), json => json["Orders"]!["Region"] = "west");
EditXml(Path.Combine(mainDir, @"export\Contoso.Orders.Export.exe.config"), "appSettings", "BatchSize", "750");

var upgrade = With(ordersValues, "PaymentsApiKey", "pk-test-123");
log = await Deploy("Contoso.Orders-1.1.0", upgrade, "main");
Expect(!log.Contains("pk-test-123"), "the API key is masked in the log");
Expect(log.Contains("Applied migration 2.") && !log.Contains("Seeded"), "the upgrade migrates the database without seeding it again");
Expect(log.Split("Check passed.").Length == 3, "both settings checks pass, the second showing the API key and Smtp:Host are set");
Expect(ServiceStatus("Contoso.Orders-main") == "Running", "main runs after the upgrade");
var settings = Json(mainDir, "appsettings.json");
Expect(settings["Orders"]?["Region"]?.ToString() == "west", "the server's region is kept");
Expect(settings["Orders"]?["BatchSize"]?.ToString() == "250", "the package's new batch size is taken");
Expect(settings["Smtp"]?["Host"] is not null && settings["Smtp"]?["Server"] is null, "Smtp:Server became Smtp:Host");
Expect(settings["LegacyMode"] is null, "LegacyMode is gone");
Expect(settings["Payments"]?["ApiKey"]?.ToString() == "pk-test-123", "the script stored the API key");
Expect(settings["Logging"]?["LogLevel"]?["Contoso.Orders"]?.ToString() == "Debug", "takeNew took the package's logging");
Expect(File.Exists(Path.Combine(mainDir, "appsettings.Production.json")), "the override 1.1.0 dropped stays on the server");
Expect(!File.Exists(Path.Combine(mainDir, @"templates\password-reset.html")), "deleteExtra removed the old template");
Expect(File.Exists(Path.Combine(mainDir, @"templates\return-received.html")), "the new template arrived");
Expect(File.Exists(Path.Combine(blueDir, @"templates\password-reset.html")), "the upgrade left blue alone");
var export = XmlSettings(Path.Combine(mainDir, @"export\Contoso.Orders.Export.exe.config"));
Expect(export["BatchSize"] == "750", "the export keeps the server's batch size");
Expect(export["Format"] == "json" && export["Compress"] == "true", "the export takes the new settings");
Expect(!export.ContainsKey("IncludeCancelled") && export.ContainsKey("connection Archive"), "the export follows removed and added entries");
Expect(Run(Path.Combine(mainDir, @"export\Contoso.Orders.Export.exe"), "--check").Output.Contains("BatchSize = 750"), "the export tool reads the merged .exe.config");
Expect(Directory.GetDirectories(mainDir + ".backups").Length == 1, "the upgrade backed up main");

using (var package = DeployPackage.Open(Path.Combine(dist, "Contoso.Orders-1.1.0.zip")))
{
    var found = Installations.Find(OrdersDir, Installations.AppFiles(package));
    Expect(string.Join(", ", found.Select(install => $"{install.Name}={install.Version}")) == "blue=1.0.0, main=1.1.0", "the folder shows blue on 1.0.0 and main on 1.1.0");
}

await Task.Delay(TimeSpan.FromSeconds(12));
var beat = LastLine(Path.Combine(mainDir, @"logs\heartbeat.log"));
Console.WriteLine($"Last heartbeat: {beat}");
Expect(beat.Contains("1.1.0 region west") && beat.Contains("db schema 2, 4 orders"), "the running service reads the merged settings and the upgraded database");

await Remove("Contoso.Orders-1.1.0", OrdersDir, "blue", upgrade);
Expect(ServiceStatus("Contoso.Orders-blue") == "", "removing blue deleted its service");
Expect(!TaskExists(@"\Contoso\Orders Export-blue"), "removing blue deleted its task");
Expect(!Directory.Exists(@$"{OrdersData}\db-blue") && !Directory.Exists(blueDir), "removing blue deleted its database and folder");
Expect(ServiceStatus("Contoso.Orders-main") == "Running" && File.Exists(@$"{OrdersData}\db-main\orders.db"), "removing blue left main alone");

// ---- Fabrikam.Reports: a .NET Framework app with plugins, upgraded with server edits in its .exe.config
var reportsValues = new Dictionary<string, string> { ["InstallDir"] = ReportsDir, ["PublishWeb"] = "false" };
log = await Deploy("Fabrikam.Reports-2.0.0", reportsValues, "main");
Expect(log.Contains("Created") && File.Exists(Path.Combine(reportsDir, @"data\sales.csv")), "--init created the data");
Expect(Directory.GetFiles(Path.Combine(reportsDir, "reports"), "*.pdf").Length == 1, "the report ran with the pdf plugin");
Expect(TaskExists(@"\Fabrikam\Reports hourly"), "the hourly task exists");
Expect(log.Contains("The report site is not published"), "the else branch ran without PublishWeb");

EditXml(Path.Combine(reportsDir, "Fabrikam.Reports.exe.config"), "appSettings", "ReportTitle", "Fabrikam weekly sales");
EditXml(Path.Combine(reportsDir, "Fabrikam.Reports.exe.config"), "appSettings", "KeepReports", "45");
EditXml(Path.Combine(reportsDir, @"wwwroot\web.config"), "appSettings", "AllowDownload", "false");

log = await Deploy("Fabrikam.Reports-2.1.0", With(reportsValues, "PublishWeb", "true"), "main");
Expect(log.Contains("left as it is"), "--init kept the existing data");
Expect(log.Contains("plugins: csv, xlsx") && log.Contains("Check passed."), "the check sees the csv and Excel plugins");
Expect(!File.Exists(Path.Combine(reportsDir, @"plugins\Fabrikam.Reports.Pdf.dll")), "deleteExtra removed the pdf plugin");
Expect(Directory.GetFiles(Path.Combine(reportsDir, "reports"), "*.xlsx").Length == 1, "the report ran with the Excel plugin");
Expect(File.ReadAllLines(Path.Combine(reportsDir, @"data\history.log")).Length == 2, "data/ survived the upgrade");
var reports = XmlSettings(Path.Combine(reportsDir, "Fabrikam.Reports.exe.config"));
Expect(reports["ReportTitle"] == "Fabrikam weekly sales", "the server's report title is kept");
Expect(reports["KeepReports"] == "90", "takeNew took the package's KeepReports");
Expect(reports.ContainsKey("MaxRows") && !reports.ContainsKey("LegacyPdfEngine") && reports.ContainsKey("connection Audit"), "the .exe.config follows added and removed entries");
Expect(File.ReadAllText(Path.Combine(reportsDir, "Fabrikam.Reports.exe.config")).Contains("newVersion=\"13.0.0.0\""), "the binding redirect comes from the new file");
var web = XmlSettings(Path.Combine(reportsDir, @"wwwroot\web.config"));
Expect(web["AllowDownload"] == "false" && web["PageSize"] == "25", "web.config keeps the server's edit and takes the new setting");

await Remove("Fabrikam.Reports-2.1.0", ReportsDir, "main", reportsValues);
Expect(!TaskExists(@"\Fabrikam\Reports hourly"), "removing Reports deleted its task");
Expect(!Directory.Exists(reportsDir) && !Directory.Exists(reportsDir + ".backups"), "removing Reports deleted its folder and backups");

await Remove("Contoso.Orders-1.1.0", OrdersDir, "main", upgrade);
Expect(ServiceStatus("Contoso.Orders-main") == "" && !TaskExists(@"\Contoso\Orders Export-main"), "removing main deleted its service and task");
Expect(!Directory.Exists(mainDir) && !Directory.Exists(@$"{OrdersData}\db-main"), "removing main deleted its folder and database");

Console.WriteLine(failures.Count == 0 ? "\nALL CHECKS PASSED" : $"\n{failures.Count} CHECK(S) FAILED:\n  " + string.Join("\n  ", failures));
return failures.Count == 0 ? 0 : 1;

async Task<string> Deploy(string name, IReadOnlyDictionary<string, string> values, string instance, bool dryRun = false)
{
    Console.WriteLine($"\n===== {(dryRun ? "Dry run" : "Deploy")} {name} {instance}");
    using var package = DeployPackage.Open(Path.Combine(dist, name + ".zip"));
    var output = new ConsoleLog();
    var options = DeployOptions.Default with { Instance = instance };
    var outcome = await new DeployRunner(new TestUser(), output).RunAsync(package, values, dryRun, options, CancellationToken.None);
    Expect(outcome.Result == DeployResult.Succeeded, $"{name} {instance} {(dryRun ? "dry run" : "deploy")} succeeds {outcome.Error?.Message}");
    return output.Text;
}

async Task Remove(string name, string installFolder, string instance, IReadOnlyDictionary<string, string> values)
{
    Console.WriteLine($"\n===== Remove {name} {instance}");
    using var package = DeployPackage.Open(Path.Combine(dist, name + ".zip"));
    var install = Installations.Find(installFolder, Installations.AppFiles(package)).Single(found => found.Name == instance);
    var result = await new InstallRemover(new TestUser(), new ConsoleLog()).RemoveAsync(install, package, values, CancellationToken.None);
    Expect(result == DeployResult.Succeeded, $"removing {name} {instance} succeeds");
}

void Expect(bool condition, string what)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {what}");
    if (!condition)
        failures.Add(what);
}

static Dictionary<string, string> With(IReadOnlyDictionary<string, string> values, string name, string value) => new(values) { [name] = value };

static JsonNode Json(string folder, string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, name)))!;

static void EditJson(string file, Action<JsonNode> edit)
{
    var json = JsonNode.Parse(File.ReadAllText(file))!;
    edit(json);
    File.WriteAllText(file, json.ToJsonString(new() { WriteIndented = true }));
}

static void EditXml(string file, string section, string key, string value)
{
    var xml = XDocument.Load(file);
    xml.Root!.Element(section)!.Elements("add").Single(add => (string?)add.Attribute("key") == key).SetAttributeValue("value", value);
    xml.Save(file);
}

/// <summary>appSettings by key, and connection strings as "connection Name".</summary>
static Dictionary<string, string> XmlSettings(string file)
{
    var root = XDocument.Load(file).Root!;
    var settings = root.Element("appSettings")?.Elements("add").ToDictionary(add => (string)add.Attribute("key")!, add => (string)add.Attribute("value")!) ?? [];
    foreach (var add in root.Element("connectionStrings")?.Elements("add") ?? [])
        settings["connection " + (string)add.Attribute("name")!] = (string)add.Attribute("connectionString")!;
    return settings;
}

// The service may be writing the file, so open it the way it allows.
static string LastLine(string file)
{
    using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
    return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[^1];
}

static string ServiceStatus(string name) =>
    Run("powershell.exe", $"-NoProfile -Command \"(Get-Service '{name}' -ErrorAction SilentlyContinue).Status\"").Output.Trim();

static bool TaskExists(string name) => Run("schtasks.exe", $"/Query /TN \"{name}\"").ExitCode == 0;

static (int ExitCode, string Output) Run(string file, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, output);
}

sealed class ConsoleLog : IDeployLog
{
    private readonly StringBuilder _text = new();
    public string Text => _text.ToString();

    public void Write(LogLevel level, string message)
    {
        Console.WriteLine($"  [{level}] {message}");
        _text.AppendLine(message);
    }
}

/// <summary>Answers yes, takes input defaults, keeps the server's value on a conflict, and runs every cleanup step.</summary>
sealed class TestUser : IUserInteraction
{
    public Task<bool> AskYesNoAsync(string question, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  ASK: {question} -> yes");
        return Task.FromResult(true);
    }

    public Task<IReadOnlyDictionary<string, string>?> AskInputsAsync(
        string title, IReadOnlyDictionary<string, InputField> fields, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>?>(fields.ToDictionary(field => field.Key, field => field.Value.Default ?? ""));

    public Task<bool> ReviewConfigsAsync(IReadOnlyList<ConfigFileUpdate> files, bool readOnly, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            Console.WriteLine($"  REVIEW {file.FileName} ({file.Kind}){(readOnly ? " read-only" : "")}");
            foreach (var row in file.Plan.Rows.Where(row => row.IsChanged || row.Choice is null))
                Console.WriteLine($"    {row.Path}: {row.Choice?.ToString() ?? "conflict, keeping the server's value"} ({row.Reason})");
            if (!readOnly)
                foreach (var row in file.Plan.Rows.Where(row => row.Choice is null))
                    row.Choice = MergeChoice.KeepCurrent;
        }
        return Task.FromResult(true);
    }

    public Task<CleanupDecision> ConfirmCleanupAsync(string title, string explanation, int number, int total, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  CLEANUP {number}/{total}: {title} -> run");
        return Task.FromResult(CleanupDecision.Run);
    }
}
