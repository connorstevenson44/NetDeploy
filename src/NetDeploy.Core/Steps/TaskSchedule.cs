using System.Globalization;

namespace NetDeploy.Core.Steps;

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
        ScheduleKind.EveryMinutes => ["/SC", "MINUTE", "/MO", (Minutes ?? 15).ToString(CultureInfo.InvariantCulture)],
        _ => ["/SC", "ONSTART"],
    };
}
