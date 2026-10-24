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

[samples/OrdersService](samples/OrdersService/deploy.json) uses every step type, and
[testpackages](testpackages/README.md) builds two real apps in two versions each to deploy and upgrade. Add
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
| `copyFiles` | Copies a package folder to the server and merges its config files (see below); `preserve` keeps existing files such as logs, `deleteExtra` mirrors |
| `runExe` | Runs an exe, such as a database migrator, and checks its exit code |
| `powershell` | Runs a .ps1 file or an inline script in Windows PowerShell or `pwsh`. A file's `args` are passed as with `-File`, so quote paths with double quotes: `"-InstallDir \"{InstallDir}\""` |
| `backup` | Copies a folder to `<folder>.backups\<timestamp>` for rollback, keeping the newest `keep` |
| `service` | Installs or updates, starts, stops or uninstalls a Windows service |
| `scheduledTask` | Creates or updates, enables, disables, runs or deletes a scheduled task |
| `if` | Runs `then` or `else` steps depending on a condition |
| `deleteFolder` | Deletes a folder, except the subfolders named in `keep`; mostly used in `cleanup` |

Every step also takes `name`, `when`, `inputs`, `continueOnError` and `cleanup`.

### Installs and instances

Add an `install` block so NetDeploy can add, update, clone and remove installs of the app:

```jsonc
"install": { "folder": "{InstallDir}", "instances": true }
```

- A single install goes straight into the folder, for example `C:\Apps\Orders\Orders.exe`.
- With `instances`, each named instance gets its own subfolder, for example `C:\Apps\Orders\east\Orders.exe`.
- Steps use `{InstanceDir}` for the app's folder, and `{Instance}` or `{InstanceSuffix}` (empty, or `-east`) in
  service and task names, so instances don't clash.

NetDeploy saves nothing extra to mark an install. It finds installs from the folder layout: the folder itself is the
single install when it holds the app's exe (or its dlls, for an app without one), and each subfolder that holds it is an
instance. Installs copied by hand are found the same way. The version shown is read from the exe.

**Removing an install** runs the open package's undo steps, newest first, then removes any other service whose program
is in the folder (read from Windows, so services installed by hand are included), then deletes the folder. Each step is
shown with exactly what it will do and needs its own OK. The built-in undo:

- A `service` install removes the service.
- A `scheduledTask` createOrUpdate deletes the task.
- A `backup` deletes the backups.

Give a step `cleanup` steps to replace its undo, for example to drop a database, or `"cleanup": []` for none.

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

### Config files

Config files are never copied over the server's copy. `copyFiles` treats every file matching `configs`
(default `**/appsettings*.json` and `**/*.config`) as config. After the other files are copied, it works out how each
config file should change and opens the **config review**. The review shows each file the way it will be written,
with changes marked like a GitHub diff. Nothing is written until you confirm. A dry run shows the same review
without writing anything.

How the tool decides, following how .NET layers configuration:

- **appsettings.json ships with the app and decides which settings exist.** It is merged three ways: the package
  deployed last time (kept in `.deploy\base` beside the file), the server's file, and the new package's file.

  | Situation | Proposed result |
  | --- | --- |
  | Changed only on the server | Keep the server's value |
  | Changed only in the package | Use the package's value |
  | Changed on both sides | You choose |
  | New in the package | Add it |
  | Removed from the package | Remove it |
  | Under a `takeNew` path, such as `Logging` | Use the package's value |

  The first deploy of an app has no earlier package to compare with, so any value that differs keeps the server's value.
- **appsettings.{Environment}.json on the server holds that server's values.** Overrides the package doesn't ship are
  checked against the new appsettings.json. .NET silently ignores a setting that the app no longer has, so a
  renamed setting would quietly lose its server value:

  | In the override | Proposed result |
  | --- | --- |
  | Still in appsettings.json | Nothing to do |
  | Removed from appsettings.json, and one new setting appeared in the same section | Treated as a rename: the value moves to the new name |
  | Removed from appsettings.json | Remove it |
  | appsettings.json now has a section at that path | You choose |
  | Never in appsettings.json (a typo, or a setting only the app's code reads) | Keep it, but show it |

  An override the package does ship is merged three ways like appsettings.json.
- **A setting must not go live with a placeholder.** A new setting with an empty value, or any value such as
  `CHANGE_ME`, `TODO`, `<your key>` or `__KEY__`, needs a value before the files can be written, unless an override
  on the server already sets it.

Arrays are one value. In XML config files, `appSettings` and `connectionStrings` entries are merged and every other
section comes from the new file.

## Solution layout

```text
src/NetDeploy.Core/          manifest, runner, steps and config merge; no UI
src/NetDeploy.App/           WPF app (Material Design for WPF, Pantone 301 theme in Styles.xaml)
tests/NetDeploy.Core.Tests/  xUnit tests
tests/NetDeploy.App.Tests/   dry runs the sample in the real window and saves screenshots of each window
schema/                      deploy.json schema
samples/                     sample package
testpackages/                two test apps in two versions, their build, and an end-to-end run on Windows
```
