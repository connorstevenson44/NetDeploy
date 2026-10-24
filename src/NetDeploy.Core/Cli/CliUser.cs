using System.Globalization;
using NetDeploy.Core.Config;

namespace NetDeploy.Core.Cli;

/// <summary>
/// Answers a deploy's questions from the command line, since nobody is there to ask: inputs from --param, the
/// environment or their defaults, questions from --yes, and config merges as planned. What can't be answered that
/// way stops the deploy with a message that says what to pass.
/// </summary>
internal sealed class CliUser(CliOptions options, IDeployLog log) : IUserInteraction
{
    /// <summary>Settings a dry run found still needing a value or a choice, where a deploy would stop.</summary>
    public int OpenSettings { get; private set; }

    public Task<bool> AskYesNoAsync(string question, CancellationToken cancellationToken)
    {
        if (!options.Yes)
            throw new OperationCanceledException($"The package asks \"{question}\" Run it again with --yes to answer yes.");
        log.Info($"{question} Yes (--yes)");
        return Task.FromResult(true);
    }

    public Task<IReadOnlyDictionary<string, string>?> AskInputsAsync(
        string title, IReadOnlyDictionary<string, InputField> fields, CancellationToken cancellationToken)
    {
        var answers = fields.ToDictionary(field => field.Key, field => options.ValueOf(field.Key) ?? DefaultOf(field.Value));
        var problems = answers.Select(answer => Problem(answer.Key, fields[answer.Key], answer.Value)).OfType<string>().ToList();
        if (problems.Count > 0)
            throw new OperationCanceledException($"{title} asks for inputs: {string.Join(" ", problems)}");
        return Task.FromResult<IReadOnlyDictionary<string, string>?>(answers);
    }

    public Task<bool> ReviewConfigsAsync(IReadOnlyList<ConfigFileUpdate> files, bool readOnly, CancellationToken cancellationToken)
    {
        var open = files.SelectMany(file => file.Plan.Rows.Where(row => row.Choice is null).Select(row => $"{file.FileName} {row.Path}: {row.Reason}")).ToList();
        foreach (var line in open)
            log.Error(line);
        if (open.Count == 0)
            return Task.FromResult(true);

        // A dry run goes on, to show the rest of what would happen, and fails at the end.
        if (readOnly)
        {
            OpenSettings += open.Count;
            return Task.FromResult(true);
        }
        throw new OperationCanceledException($"{Count(open.Count)} a value or a choice, so no config file was written. "
            + "Set them in the server's config, or deploy once from the window to choose.");
    }

    public Task<CleanupDecision> ConfirmCleanupAsync(string title, string explanation, int number, int total, CancellationToken cancellationToken) =>
        Task.FromResult(CleanupDecision.Cancel);

    internal static string Count(int settings) => settings == 1 ? "1 config setting needs" : $"{settings} config settings need";

    /// <summary>The value a field starts with in the window when it has no default.</summary>
    internal static string DefaultOf(InputField field) => field.Default ?? field.Type switch
    {
        InputType.YesNo => "false",
        InputType.Choice => field.Options?.FirstOrDefault() ?? "",
        _ => "",
    };

    /// <summary>Why a value can't be used for a parameter or input, or null when it can.</summary>
    internal static string? Problem(string name, InputField field, string value)
    {
        var label = field.Label is { } text && !text.Equals(name, StringComparison.OrdinalIgnoreCase) ? $"{text} ({name})" : name;
        if (value.Length == 0)
            return field.Optional ? null : $"{label} needs a value: pass --param {name}=<value>.";
        return field.Type switch
        {
            InputType.Number when !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) => $"{label} must be a number.",
            InputType.YesNo when !bool.TryParse(value, out _) => $"{label} must be true or false.",
            InputType.Choice when field.Options is { } choices && !choices.Contains(value, StringComparer.OrdinalIgnoreCase) =>
                $"{label} must be one of {string.Join(", ", choices)}.",
            _ => null,
        };
    }
}
