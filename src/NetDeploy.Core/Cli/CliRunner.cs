namespace NetDeploy.Core.Cli;

/// <summary>
/// Runs a deploy from the command line, for pipelines and scheduled jobs: the same package, steps, merges, rollback
/// and history as the window, with what the window would ask taken from the command line instead.
/// </summary>
/// <param name="output">Where the log and messages are written: the console.</param>
/// <param name="isAdministrator">Whether this process can run a package that needs administrator rights.</param>
public sealed class CliRunner(TextWriter output, DeployHistory history, bool isAdministrator)
{
    public const int Finished = 0;
    public const int Failed = 1;
    public const int Mistake = 2;

    /// <summary>Runs the command and returns the process's exit code.</summary>
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        CliOptions? options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (CliException ex)
        {
            output.WriteLine(ex.Message);
            output.WriteLine();
            output.WriteLine(CliOptions.Usage);
            return Mistake;
        }
        if (options is null)
        {
            output.WriteLine(CliOptions.Usage);
            return Finished;
        }

        DeployPackage package;
        try
        {
            package = DeployPackage.Open(options.Package);
        }
        catch (Exception ex) when (ex is ManifestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            output.WriteLine($"Can't open {options.Package}: {ex.Message}");
            return Mistake;
        }
        using (package)
            return await DeployAsync(package, options, cancellationToken);
    }

    private async Task<int> DeployAsync(DeployPackage package, CliOptions options, CancellationToken cancellationToken)
    {
        var (values, problems) = Parameters(package, options);
        if (package.Manifest.RequiresAdmin && !options.DryRun && !isAdministrator)
            problems.Add("This package needs administrator rights. Run it from a prompt opened as administrator.");
        if (problems.Count > 0)
        {
            foreach (var problem in problems)
                output.WriteLine(problem);
            return Mistake;
        }
        foreach (var warning in package.Warnings)
            output.WriteLine($"Warning: {warning}");

        using var log = new CliLog(output, LogFiles.Create(package.Manifest.App.Name, options.DryRun ? "dryrun" : "deploy"));
        var user = new CliUser(options, log);
        var deployOptions = DeployOptions.Default with { Instance = options.Instance };
        var outcome = await new DeployRunner(user, log).RunAsync(package, values, options.DryRun, deployOptions, cancellationToken);

        var rolledBack = false;
        if (!options.DryRun && outcome.Result != DeployResult.Succeeded && outcome.Rollback.IsAvailable)
        {
            if (options.NoRollback)
                log.Warn("Left as it is, without rolling back (--no-rollback).");
            else
                rolledBack = RollBack(outcome.Rollback, log);
        }
        if (!options.DryRun && !history.TryAdd(DeployRecord.For(package, deployOptions, outcome, rolledBack, log.FilePath)))
            log.Warn($"This deploy couldn't be added to the history in {history.Folder}.");

        if (user.OpenSettings > 0)
            log.Error($"{CliUser.Count(user.OpenSettings)} a value or a choice, so a deploy would stop at the config review.");
        output.WriteLine($"Log: {log.FilePath}");
        return outcome.Result == DeployResult.Succeeded && user.OpenSettings == 0 ? Finished : Failed;
    }

    /// <summary>
    /// The package's parameters from --param, the environment or their defaults, and what is wrong with the command
    /// for this package, such as a missing install name or a --param it has no use for.
    /// </summary>
    private static (Dictionary<string, string> Values, List<string> Problems) Parameters(DeployPackage package, CliOptions options)
    {
        var manifest = package.Manifest;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        foreach (var (name, field) in manifest.Parameters)
        {
            values[name] = options.ValueOf(name) ?? field.Initial;
            if (CliUser.Problem(name, field, values[name]) is { } problem)
                problems.Add(problem);
        }

        var inputs = Installations.Flatten(manifest.Steps).SelectMany(step => step.Inputs?.Keys ?? Enumerable.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        problems.AddRange(options.Values.Keys.Where(name => !manifest.Parameters.ContainsKey(name) && !inputs.Contains(name))
            .Select(name => $"The package has no parameter or input called {name}."));

        if (manifest.Install is null)
        {
            if (options.Instance.Length > 0)
                problems.Add("This package has no install block, so leave out --install.");
        }
        else if (options.Instance.Length == 0)
        {
            var folder = Placeholders.Resolve(manifest.Install.Folder, values);
            var installs = Path.IsPathFullyQualified(folder) ? Installations.Find(folder, Installations.AppFiles(package)) : [];
            problems.Add(installs.Count == 0
                ? $"Name the install to add with --install <name>. {folder} has none yet."
                : $"Name the install to add or update with --install <name>. {folder} has {string.Join(", ", installs.Select(install => install.Name))}.");
        }
        else if (Installations.CheckName(options.Instance) is { } problem)
        {
            problems.Add($"--install {options.Instance}: {problem}");
        }
        return (values, problems);
    }

    private static bool RollBack(RollbackPlan rollback, IDeployLog log)
    {
        log.Info("Rolling back: restoring the backup and starting the services this deploy stopped.");
        try
        {
            rollback.Restore(log);
            log.Info("Rolled back.");
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Rollback failed: {ex.Message}");
            return false;
        }
    }
}

/// <summary>Writes each line of the log to the console and to the log file.</summary>
internal sealed class CliLog(TextWriter output, StreamWriter file) : IDeployLog, IDisposable
{
    private readonly Lock _gate = new();

    public string FilePath => LogFiles.PathOf(file);

    public void Write(LogLevel level, string message)
    {
        var line = LogFiles.Line(DateTime.Now, level, message);
        lock (_gate)
        {
            file.WriteLine(line);
            output.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_gate)
            file.Dispose();
    }
}
