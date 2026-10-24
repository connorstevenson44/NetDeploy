using System.Diagnostics;
using System.Text.Json.Serialization;

namespace NetDeploy.Core.Steps;

public enum ScheduledTaskAction { CreateOrUpdate, Enable, Disable, Run, Delete }

public enum ScheduleKind { Daily, Hourly, EveryMinutes, OnStart }

public sealed record TaskSchedule
{
    public required ScheduleKind Kind { get; init; }

    /// <summary>Start time for daily tasks, as HH:mm.</summary>
    public string? Time { get; init; }

    /// <summary>Interval for everyMinutes tasks.</summary>
    public int? Minutes { get; init; }

    internal IEnumerable<string> ToArguments() => Kind switch
    {
        ScheduleKind.Daily => ["/SC", "DAILY", "/ST", Time ?? "00:00"],
        ScheduleKind.Hourly => ["/SC", "HOURLY"],
        ScheduleKind.EveryMinutes => ["/SC", "MINUTE", "/MO", (Minutes ?? 15).ToString()],
        _ => ["/SC", "ONSTART"],
    };
}

/// <summary>Manages a Windows scheduled task through schtasks.exe.</summary>
public sealed class ScheduledTaskStep : DeployStep
{
    public required ScheduledTaskAction Action { get; init; }

    /// <summary>The task's name, optionally with a folder, such as "\MyApps\Nightly Cleanup".</summary>
    [JsonPropertyName("task")]
    public required string TaskName { get; init; }

    /// <summary>Task XML exported from Task Scheduler, inside the package. Used instead of program and schedule.</summary>
    public string? Definition { get; init; }

    public string? Program { get; init; }
    public string? Arguments { get; init; }
    public TaskSchedule? Schedule { get; init; }
    public string? RunAs { get; init; }
    public string? Password { get; init; }

    public override string Describe() => Action switch
    {
        ScheduledTaskAction.CreateOrUpdate => $"Set up scheduled task {TaskName}",
        _ => $"{Action} scheduled task {TaskName}",
    };

    public override string Explain() => Action switch
    {
        ScheduledTaskAction.Delete => $"Deletes the scheduled task \"{TaskName}\": schtasks /Delete /TN \"{TaskName}\" /F.",
        ScheduledTaskAction.CreateOrUpdate => $"Creates the scheduled task \"{TaskName}\", or updates it if it is already there.",
        _ => $"{Action}s the scheduled task \"{TaskName}\".",
    };

    protected internal override IEnumerable<DeployStep> DefaultCleanup() =>
        Action == ScheduledTaskAction.CreateOrUpdate ? [new ScheduledTaskStep { Action = ScheduledTaskAction.Delete, TaskName = TaskName }] : [];

    public override async Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        var name = context.Resolve(TaskName);
        if (Action == ScheduledTaskAction.Delete && !await ExistsAsync(name, cancellationToken))
        {
            context.Log.Info($"Scheduled task {name} does not exist.");
            return;
        }

        if (context.DryRun)
        {
            context.Log.Info($"Would {(Action == ScheduledTaskAction.CreateOrUpdate ? "create or update" : Action.ToString().ToLowerInvariant())} scheduled task {name}");
            return;
        }

        string[] arguments = Action switch
        {
            ScheduledTaskAction.CreateOrUpdate => ["/Create", "/TN", name, .. CreateArguments(context), "/F"],
            ScheduledTaskAction.Enable => ["/Change", "/TN", name, "/ENABLE"],
            ScheduledTaskAction.Disable => ["/Change", "/TN", name, "/DISABLE"],
            ScheduledTaskAction.Run => ["/Run", "/TN", name],
            _ => ["/Delete", "/TN", name, "/F"],
        };

        var result = await RunSchtasksAsync(arguments, context.Log, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"schtasks.exe exited with code {result.ExitCode}.");
    }

    private IEnumerable<string> CreateArguments(StepContext context)
    {
        if (Definition is not null)
        {
            yield return "/XML";
            yield return context.ResolvePath(Definition);
        }
        else
        {
            yield return "/TR";
            yield return $"\"{context.Resolve(Program!)}\" {context.Resolve(Arguments ?? "")}".TrimEnd();
            foreach (var argument in Schedule!.ToArguments())
                yield return argument;
        }

        if (RunAs is not null)
        {
            yield return "/RU";
            yield return context.Resolve(RunAs);
            if (Password is not null)
            {
                yield return "/RP";
                yield return context.Resolve(Password);
            }
        }
    }

    private static async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken) =>
        (await RunSchtasksAsync(["/Query", "/TN", name], NullLog.Instance, cancellationToken)).ExitCode == 0;

    private static Task<ProcessResult> RunSchtasksAsync(IEnumerable<string> arguments, IDeployLog log, CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync(new ProcessStartInfo("schtasks.exe", arguments), log, TimeSpan.FromMinutes(1), cancellationToken);

    protected override IEnumerable<string> ValidateOptions()
    {
        if (Action == ScheduledTaskAction.CreateOrUpdate && Definition is null && (Program is null || Schedule is null))
            yield return "Creating a scheduled task needs 'definition', or 'program' and 'schedule'.";
    }

    private sealed class NullLog : IDeployLog
    {
        public static readonly NullLog Instance = new();
        public void Write(LogLevel level, string message) { }
    }
}
