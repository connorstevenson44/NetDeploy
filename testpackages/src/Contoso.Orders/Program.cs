// Contoso Orders, a small Windows service for trying NetDeploy.
//   Contoso.Orders.exe                     runs as the service: writes a heartbeat with the settings it reads
//   Contoso.Orders.exe migrate --db <file> creates or upgrades the SQLite database and seeds it
//   Contoso.Orders.exe check               prints the settings and fails when a required one is missing
using Microsoft.Data.Sqlite;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production",
});
var settings = new OrderSettings(builder.Configuration);

switch (args.FirstOrDefault())
{
    case "migrate":
        return Database.Migrate(Option("--db") ?? settings.DatabasePath, args.Contains("--seed"));
    case "check":
        return settings.Check();
}

builder.Services.AddWindowsService();
builder.Services.AddSingleton(settings);
builder.Services.AddHostedService<Heartbeat>();
builder.Build().Run();
return 0;

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

sealed class OrderSettings(IConfiguration config)
{
    public static readonly Version AppVersion = typeof(OrderSettings).Assembly.GetName().Version!;

    // 1.1 renamed Smtp:Server to Smtp:Host and added Payments:ApiKey and Features.
    private static readonly bool IsV11 = AppVersion >= new Version(1, 1);
    private static readonly string SmtpKey = IsV11 ? "Smtp:Host" : "Smtp:Server";

    private string[] Required => IsV11
        ? ["Orders:Region", SmtpKey, "ConnectionStrings:Orders", "Payments:ApiKey"]
        : ["Orders:Region", SmtpKey, "ConnectionStrings:Orders"];

    private string[] Shown =>
    [
        "Logging:LogLevel:Default", "Orders:Region", "Orders:BatchSize", "Orders:HeartbeatSeconds", SmtpKey, "Smtp:Port",
        "Cleanup:KeepDays", "Features:FastCheckout", "Features:Returns", "LegacyMode", "ConnectionStrings:Orders", "Payments:ApiKey",
    ];

    public string DatabasePath => new SqliteConnectionStringBuilder(config["ConnectionStrings:Orders"] ?? "Data Source=orders.db").DataSource;
    public int HeartbeatSeconds => int.TryParse(config["Orders:HeartbeatSeconds"], out var seconds) && seconds > 0 ? seconds : 10;

    public string Summary() =>
        $"{AppVersion.ToString(3)} region {config["Orders:Region"]} | smtp {config[SmtpKey]}:{config["Smtp:Port"]} | batch {config["Orders:BatchSize"]} | {Database.Describe(DatabasePath)}";

    public int Check()
    {
        Console.WriteLine($"Contoso Orders {AppVersion.ToString(3)}");
        foreach (var key in Shown)
            Console.WriteLine($"  {key} = {(key.Contains("ApiKey") && config[key] is { Length: > 0 } ? "****" : config[key] ?? "(not set)")}");
        var missing = Required.Where(key => string.IsNullOrWhiteSpace(config[key]) || config[key]!.StartsWith('{')).ToList();
        foreach (var key in missing)
            Console.WriteLine($"  MISSING {key}");
        Console.WriteLine(missing.Count == 0 ? "Check passed." : "Check failed.");
        return missing.Count == 0 ? 0 : 1;
    }
}

sealed class Heartbeat(OrderSettings settings) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var folder = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "logs")).FullName;
        while (!stoppingToken.IsCancellationRequested)
        {
            await File.AppendAllTextAsync(Path.Combine(folder, "heartbeat.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {settings.Summary()}{Environment.NewLine}", stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(settings.HeartbeatSeconds), stoppingToken);
        }
    }
}

static class Database
{
    // Each step upgrades the schema by one version. 1.1 adds the returns table and a column.
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Email TEXT);
        CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL REFERENCES Customers(Id), Total REAL NOT NULL, PlacedAt TEXT NOT NULL);
        """,
        """
        CREATE TABLE Returns (Id INTEGER PRIMARY KEY, OrderId INTEGER NOT NULL REFERENCES Orders(Id), Reason TEXT);
        ALTER TABLE Orders ADD COLUMN Status TEXT NOT NULL DEFAULT 'Placed';
        """,
    ];

    private static int TargetVersion => OrderSettings.AppVersion >= new Version(1, 1) ? 2 : 1;

    public static int Migrate(string path, bool seed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var connection = Open(path);
        Execute(connection, "CREATE TABLE IF NOT EXISTS SchemaVersion (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL)");
        var current = Convert.ToInt32(Scalar(connection, "SELECT IFNULL(MAX(Version), 0) FROM SchemaVersion"));
        Console.WriteLine($"Database {path} is at schema {current}; this version needs {TargetVersion}.");
        for (var version = current + 1; version <= TargetVersion; version++)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, Migrations[version - 1], transaction);
            Execute(connection, $"INSERT INTO SchemaVersion VALUES ({version}, datetime('now'))", transaction);
            transaction.Commit();
            Console.WriteLine($"Applied migration {version}.");
        }

        if (seed && Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM Customers")) == 0)
        {
            Execute(connection, """
                INSERT INTO Customers (Name, Email) VALUES ('Alice Martin', 'alice@example.com'), ('Bob Singh', 'bob@example.com'), ('Chen Wei', 'chen@example.com');
                INSERT INTO Orders (CustomerId, Total, PlacedAt) VALUES (1, 42.50, datetime('now')), (1, 19.99, datetime('now')), (2, 105.00, datetime('now')), (3, 7.25, datetime('now'));
                """);
            Console.WriteLine("Seeded 3 customers and 4 orders.");
        }
        Console.WriteLine(Describe(path));
        return 0;
    }

    public static string Describe(string path)
    {
        if (!File.Exists(path))
            return "no database yet";
        try
        {
            using var connection = Open(path);
            return $"db schema {Scalar(connection, "SELECT MAX(Version) FROM SchemaVersion")}, {Scalar(connection, "SELECT COUNT(*) FROM Orders")} orders";
        }
        catch (SqliteException ex)
        {
            return $"db error: {ex.Message}";
        }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
