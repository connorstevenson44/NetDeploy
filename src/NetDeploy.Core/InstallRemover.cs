using NetDeploy.Core.Steps;

namespace NetDeploy.Core;

/// <summary>
/// Removes an install: undoes what the package's steps create, removes any other service whose program is in the
/// folder, then deletes the folder. Every step is confirmed on its own, after the user has seen exactly what it does.
/// Nothing is read from a saved record, so installs made by hand can be removed the same way.
/// </summary>
public sealed class InstallRemover(IUserInteraction user, IDeployLog log)
{
    /// <summary>
    /// The steps in the order they run, with the install's values filled in. manifest is the package the install came
    /// from, when one is open; without it only the services and the folder are removed. A single install keeps the
    /// instance folders inside it.
    /// </summary>
    public static IReadOnlyList<DeployStep> Plan(
        Installation install, IReadOnlyList<Installation> all, DeployManifest? manifest, IReadOnlyDictionary<string, string> values)
    {
        var installFolder = install.IsInstance ? Path.GetDirectoryName(install.Directory)! : install.Directory;
        var withInstance = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in Installations.Variables(installFolder, install.Name))
            withInstance[name] = value;

        // Newest first: what a later step created may depend on what an earlier one did.
        var steps = (manifest is null ? [] : Installations.Flatten(manifest.Steps))
            .SelectMany(step => step.CleanupSteps())
            .Reverse()
            .Select(step => step.WithValues(withInstance))
            .ToList();

        var keep = install.IsInstance
            ? []
            : all.Where(other => other.IsInstance)
                .SelectMany(other => new[] { other.Name, Path.GetFileName(BackupStep.BackupsOf(other.Directory)) })
                .Where(name => Directory.Exists(Path.Combine(install.Directory, name)))
                .ToList();
        var known = steps.OfType<ServiceStep>().Where(step => step.Action == ServiceAction.Uninstall).Select(step => step.Service).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var services = WindowsServices.ServicesIn(install.Directory, keep.Select(name => Path.Combine(install.Directory, name)));
        steps.InsertRange(0, services.Where(service => !known.Contains(service))
            .Select(service => new ServiceStep { Action = ServiceAction.Uninstall, Service = service }));

        steps.Add(new DeleteFolderStep { Path = install.Directory, Keep = keep });
        return steps;
    }

    public async Task<DeployResult> RemoveAsync(
        Installation install, IReadOnlyList<Installation> all, DeployPackage? package, IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var secrets = package?.Manifest.Parameters.Where(pair => pair.Value.Type == InputType.Secret).Select(pair => pair.Key).ToHashSet() ?? [];
        var maskingLog = new MaskingLog(log);
        foreach (var name in secrets)
            if (values.TryGetValue(name, out var secret))
                maskingLog.AddSecret(secret);

        var context = new StepContext(new DeployRunner(user, maskingLog))
        {
            // Undo scripts may live in the package.
            PackageDirectory = package?.RootPath ?? install.Directory,
            DryRun = false,
            Log = maskingLog,
            User = user,
        };

        maskingLog.Info($"Removing {(install.IsInstance ? "instance " + install.Name : "the install")} in {install.Directory}");
        var steps = Plan(install, all, package?.Manifest, values);
        var failed = false;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i].WithValues(context.Variables);
            if (step.UnresolvedNames() is [_, ..] missing)
            {
                var fields = missing.ToDictionary(key => key, key => new InputField { Label = key, Type = secrets.Contains(key) ? InputType.Secret : InputType.Text });
                var answers = await user.AskInputsAsync(step.Title, fields, cancellationToken);
                if (answers is null)
                    return Cancelled();
                foreach (var (key, value) in answers)
                {
                    context.Variables[key] = value;
                    if (secrets.Contains(key))
                        maskingLog.AddSecret(value);
                }
                step = step.WithValues(context.Variables);
            }

            switch (await user.ConfirmCleanupAsync(step.Title, step.Explain(), i + 1, steps.Count, cancellationToken))
            {
                case CleanupDecision.Cancel:
                    return Cancelled();
                case CleanupDecision.Skip:
                    maskingLog.Info($"Skipped: {step.Title}");
                    continue;
            }

            try
            {
                maskingLog.Info($"> {step.Title}");
                await step.RunAsync(context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed = true;
                maskingLog.Error($"{step.Title} failed: {ex.Message}");
                if (!await user.AskYesNoAsync($"{step.Title} failed: {ex.Message}\n\nGo on with the remaining steps?", cancellationToken))
                    return DeployResult.Failed;
            }
        }

        maskingLog.Info(failed ? "Removal finished with errors." : "Removal finished.");
        return failed ? DeployResult.Failed : DeployResult.Succeeded;

        DeployResult Cancelled()
        {
            maskingLog.Warn("Removal cancelled. Steps already run are not undone.");
            return DeployResult.Cancelled;
        }
    }
}
