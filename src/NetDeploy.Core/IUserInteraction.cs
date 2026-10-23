using NetDeploy.Core.Config;

namespace NetDeploy.Core;

/// <summary>Everything a deploy may need to ask the person running it.</summary>
public interface IUserInteraction
{
    Task<bool> AskYesNoAsync(string question, CancellationToken cancellationToken);

    /// <summary>Returns the answers keyed by input name, or null if the user cancelled.</summary>
    Task<IReadOnlyDictionary<string, string>?> AskInputsAsync(
        string title, IReadOnlyDictionary<string, InputField> fields, CancellationToken cancellationToken);

    /// <summary>
    /// Shows the merge table so the user can review and change each row.
    /// Returns false if the user cancelled. When readOnly is true (a dry run) nothing will be written.
    /// </summary>
    Task<bool> ReviewMergeAsync(string filePath, MergePlan plan, bool readOnly, CancellationToken cancellationToken);
}
