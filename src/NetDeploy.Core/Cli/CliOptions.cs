namespace NetDeploy.Core.Cli;

/// <summary>A deploy asked for on the command line: NetDeploy.exe deploy &lt;package&gt; [options].</summary>
public sealed record CliOptions
{
    public const string Usage = """
        Usage: NetDeploy.exe deploy <package> [options]

        Deploys a package (a .zip, or a folder with a deploy.json) without opening the window.

          --install <name>      The install to add or update, for a package with an install block.
          --param <Name=Value>  A parameter's value, or a step input's. Repeat it for each one. A value not given here
                                is read from the environment variable NETDEPLOY_<Name>, then from its default. Use the
                                environment for secrets, so they stay out of the command line.
          --dry-run             Shows what would change, and changes nothing.
          --yes                 Answers yes to the package's questions. Without it, a question stops the deploy.
          --no-rollback         Leaves a failed deploy as it is, instead of restoring its backup.

        Config files are merged as the window merges them before anyone changes a row. A setting that still needs a
        value or a choice stops the deploy before any config file is written: set it in the server's config, such as
        an appsettings.<Environment>.json, or deploy once from the window to choose.

        Exit codes: 0 finished, 1 failed or stopped, 2 a mistake in the command or the package.
        """;

    public required string Package { get; init; }
    public string Instance { get; init; } = "";
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool DryRun { get; init; }
    public bool Yes { get; init; }
    public bool NoRollback { get; init; }

    /// <summary>True when the arguments are a command, rather than a package for the window to open.</summary>
    public static bool IsCommand(IReadOnlyList<string> args) =>
        args is [var first, ..] && first.ToLowerInvariant() is "deploy" or "help" or "--help" or "-h" or "/?";

    /// <summary>Reads the arguments. Returns null for help, and throws CliException for a mistake in them.</summary>
    public static CliOptions? Parse(string[] args)
    {
        if (!IsCommand(args))
            throw new CliException("The only command is deploy.");
        if (!args[0].Equals("deploy", StringComparison.OrdinalIgnoreCase))
            return null;

        var rest = args[1..];
        string? package = null, instance = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool dryRun = false, yes = false, noRollback = false;
        for (var i = 0; i < rest.Length; i++)
        {
            switch (rest[i])
            {
                case "--install":
                    instance = Next(rest, ref i);
                    break;
                case "--param":
                    var pair = Next(rest, ref i);
                    var equals = pair.IndexOf('=');
                    if (equals <= 0)
                        throw new CliException($"--param takes Name=Value, not \"{pair}\".");
                    values[pair[..equals]] = pair[(equals + 1)..];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--yes":
                    yes = true;
                    break;
                case "--no-rollback":
                    noRollback = true;
                    break;
                case var option when option.StartsWith("--", StringComparison.Ordinal):
                    throw new CliException($"There is no option {option}.");
                case var path when package is null:
                    package = path;
                    break;
                default:
                    throw new CliException($"Only one package can be deployed at a time, but \"{rest[i]}\" is another.");
            }
        }

        return new CliOptions
        {
            Package = package ?? throw new CliException("Name the package to deploy."),
            Instance = instance ?? "",
            Values = values,
            DryRun = dryRun,
            Yes = yes,
            NoRollback = noRollback,
        };
    }

    /// <summary>The value given for a parameter or input, from --param or else the environment; null when neither has one.</summary>
    public string? ValueOf(string name) => Values.GetValueOrDefault(name) ?? Environment.GetEnvironmentVariable("NETDEPLOY_" + name);

    private static string Next(string[] args, ref int i) =>
        ++i < args.Length ? args[i] : throw new CliException($"{args[i - 1]} needs a value after it.");
}
