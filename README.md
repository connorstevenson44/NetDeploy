# NetDeploy

A portable Windows app for deploying, updating and configuring .NET apps. It opens a deploy package
(a zip holding the app and a `deploy.json` manifest), shows the plan, asks for any values it needs,
and runs the steps with a live log. Every deploy can back up first, so a failure can be rolled back.

## Build and run

Needs the .NET 10 SDK.

```powershell
dotnet build
dotnet test
dotnet run --project src/NetDeploy.App
```

To make the portable exe (one self-contained file, about 65 MB, nothing to install on the server):

```powershell
dotnet publish src/NetDeploy.App -p:PublishProfile=Portable
# -> artifacts\portable\NetDeploy.exe
```

CI uploads a `Screenshots` artifact with each window, so a design change can be reviewed without running the app.

Logs are written to a `logs` folder next to the exe, or to `%LOCALAPPDATA%\NetDeploy\logs` if that folder is read-only.

## Packages

A package is a zip with `deploy.json` at its root. Paths in steps are relative to the zip unless they are absolute.
You can also open or drop a plain folder that contains `deploy.json`, which is handy while writing a manifest.

```text
OrdersService-2.4.0.zip
  deploy.json      manifest: app info, parameters, steps
  app/             the published .NET app
  migrations/      database migration exes
  scripts/         .ps1 files
  tasks/           scheduled task XML exported from Task Scheduler
  prereqs/         installers, such as the .NET runtime
```

[samples/OrdersService](samples/OrdersService/deploy.json) uses every step type. Add
`"$schema": "<path>/deploy.schema.json"` to a manifest to get autocomplete and validation in Visual Studio and VS Code
from [schema/deploy.schema.json](schema/deploy.schema.json).

### Parameters, inputs and placeholders

`parameters` are asked once before the run. Any step can also have `inputs`, asked just before it runs.
Answers are used as `{Name}` in any later text; `{PackageDir}` is the extracted package folder.
Input types: `text`, `secret`, `number`, `yesNo` (gives `true` or `false`), `choice` (with `options`), `folder`, `file`.
Secret values are masked in the log.

### Steps

| Step | What it does |
| --- | --- |
| `checkPrerequisite` | Checks for a .NET runtime, an installed program, a registry value, a file, or a PowerShell test; runs `installer` if missing |
| `copyFiles` | Copies a package folder to the server; `preserve` keeps existing files such as config, `deleteExtra` mirrors |
| `mergeConfig` | Three-way merges appsettings.json, app.config or .exe.config and opens the merge table |
| `runExe` | Runs an exe, such as a database migrator, and checks its exit code |
| `powershell` | Runs a .ps1 file or an inline script in Windows PowerShell or `pwsh` |
| `backup` | Copies a folder to `<folder>.backups\<timestamp>` for rollback, keeping the newest `keep` |
| `service` | Installs or updates, starts, stops or uninstalls a Windows service |
| `scheduledTask` | Creates or updates, enables, disables, runs or deletes a scheduled task |
| `if` | Runs `then` or `else` steps depending on a condition |

Every step also takes `name`, `when`, `inputs` and `continueOnError`.

### Conditions

Used by `when` and `if`. Exactly one of:

```jsonc
{ "powershell": "Test-Path 'D:\\Data'" }            // the script's last output is True or False
{ "ask": "Run the database migration now?" }         // a Yes/No question at run time
{ "value": "{Environment}", "equals": "Production" } // ignores case
{ "all": [ ... ] }  { "any": [ ... ] }  { "not": { ... } }
```

A PowerShell condition that errors stops the run instead of counting as false. Conditions also run in a dry run,
so their scripts should only read state.

### Merging config

`mergeConfig` compares three versions of each setting: the package deployed last time (kept in `.deploy\base`
beside the file), the file on the server, and the file in the new package.

| Situation | Proposed result |
| --- | --- |
| Changed only on the server | Keep the server's value |
| Changed only in the package | Take the package's value |
| Changed on both sides | Conflict: you choose in the merge table |
| New in the package | Add it |
| Removed from the package | Remove it, or keep it with `keepRemoved` |
| Under a `takeNew` path | Take the package's value |

The first deploy of an app has no earlier package to compare with, so any value that differs keeps the server's value.
Arrays are one value. In XML config files, `appSettings` and `connectionStrings` entries are merged and every other
section comes from the new file. List the merged file in `copyFiles` `preserve`, so the copy doesn't overwrite it first.

## Solution layout

```text
src/NetDeploy.Core/          manifest, runner, steps and config merge; no UI
src/NetDeploy.App/           WPF app (Material Design for WPF, Pantone 301 theme in Styles.xaml)
tests/NetDeploy.Core.Tests/  xUnit tests
tests/NetDeploy.App.Tests/   dry runs the sample in the real window and saves screenshots of each window
schema/                      deploy.json schema
samples/                     sample package
```
