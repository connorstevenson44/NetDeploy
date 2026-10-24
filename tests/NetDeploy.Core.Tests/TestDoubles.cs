using NetDeploy.Core.Config;

namespace NetDeploy.Core.Tests;

internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateDirectory(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NetDeployTests", System.IO.Path.GetRandomFileName())).FullName;

    public string File(string relativePath, string content)
    {
        var full = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string Read(string relativePath) => System.IO.File.ReadAllText(Combine(relativePath));

    public bool Exists(string relativePath) => System.IO.File.Exists(Combine(relativePath));

    public string Combine(string relativePath) => System.IO.Path.Combine(Path, relativePath);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal sealed class TestLog : IDeployLog
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public string Text => string.Join(Environment.NewLine, Entries.Select(e => e.Message));

    public void Write(LogLevel level, string message) => Entries.Add((level, message));
}

internal sealed class FakeUser : IUserInteraction
{
    public Queue<bool> YesNoAnswers { get; } = new();
    public List<string> Questions { get; } = [];
    public Dictionary<string, string> InputAnswers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<IReadOnlyDictionary<string, InputField>> InputRequests { get; } = [];
    public bool CancelInputs { get; set; }

    public List<IReadOnlyList<ConfigFileUpdate>> Reviews { get; } = [];
    public Action<IReadOnlyList<ConfigFileUpdate>>? ResolveReview { get; set; }
    public bool CancelReview { get; set; }

    public Task<bool> AskYesNoAsync(string question, CancellationToken cancellationToken)
    {
        Questions.Add(question);
        return Task.FromResult(YesNoAnswers.Dequeue());
    }

    public Task<IReadOnlyDictionary<string, string>?> AskInputsAsync(
        string title, IReadOnlyDictionary<string, InputField> fields, CancellationToken cancellationToken)
    {
        InputRequests.Add(fields);
        IReadOnlyDictionary<string, string>? answers = CancelInputs
            ? null
            : fields.ToDictionary(f => f.Key, f => InputAnswers.GetValueOrDefault(f.Key) ?? f.Value.Default ?? "");
        return Task.FromResult(answers);
    }

    public Task<bool> ReviewConfigsAsync(IReadOnlyList<ConfigFileUpdate> files, bool readOnly, CancellationToken cancellationToken)
    {
        Reviews.Add(files);
        ResolveReview?.Invoke(files);
        return Task.FromResult(!CancelReview);
    }

    /// <summary>Answers for each cleanup step in turn; Run once they run out.</summary>
    public Queue<CleanupDecision> CleanupDecisions { get; } = new();
    public List<(string Title, string Explanation)> CleanupPrompts { get; } = [];

    public Task<CleanupDecision> ConfirmCleanupAsync(string title, string explanation, int number, int total, CancellationToken cancellationToken)
    {
        CleanupPrompts.Add((title, explanation));
        return Task.FromResult(CleanupDecisions.Count > 0 ? CleanupDecisions.Dequeue() : CleanupDecision.Run);
    }
}
