# NetDeploy

A portable Windows app for deploying, updating and configuring .NET apps. It opens a deploy package
(a zip holding the app and a `deploy.json` manifest) beside the folder it installs into, shows exactly which files
and settings will change, and runs the steps with a live log. Every deploy can back up first, so a failure can be
rolled back.

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

## The workspace

Everything happens in one window. Open or drop a package, an install folder, or a `deploy.json`.

- **Left:** the package's settings with **Dry run** and **Deploy**, and below them the install folder, one folder at
  a time: the folders in the one you are in, with installs marked with their version. Click a folder to go into it
  and **Up** to come back. Going into an install makes it the deploy's target, and the **Deploy** button says what it
  will do (**Add**, **Update** or **Rename**). For a new install, use the **New install** row at the
  bottom of the install folder and type a name, so the installs already there stay in view. Hover an install to clone
  or remove it. The dot beside an install says whether its Windows services are running (green), stopped (red) or in
  between (amber); hover it for each service. A question mark means no service runs from that install.
- **Right:** tabs for **Files** (every file in the folder you are in, as a tree whose folders fold, with a filter box:
  edit any text file, run a script, or stage a delete, all written together with **Finalize**),
  **Steps**, **deploy.json** (the visual editor) and **Log**.
- **Files are edited with tracked changes**, as in a word processor. There is one editor: type in it and new text shows
  in green, while text taken out stays where it was, struck through in red. Each change has buttons at the end of its
  line to undo it, or in a deploy to take the package's text instead; **Undo all** starts over. This works for any
  text file, JSON, XML or plain text, and the file is written as typed, comments and layout included.
- **Deploy** first shows every file it will add, replace, merge, keep or delete in the right-hand pane, so you can
  change any of them. **Continue** runs the steps; when the copy step gets to the config files, their review shows in
  the same pane, then the rest of the deploy runs with its log.

An install folder opened on its own, without a package, works the same way for managing it: edit settings, run tools,
clone or remove installs.

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

To write one without editing JSON by hand, use the **deploy.json** tab: it shows the open package's config, or use
**New config** or **Open config** (or drop a `.json` onto the window). It lists the steps, with the steps of an `if`
under its Then and Else, and shows a form for the selected one, built from the schema. Problems NetDeploy would report
are listed as you type; click one to go to its step. Saving a `deploy.json` opens its folder as the package, ready for
a dry run. Settings the form doesn't show, such as `$schema`, are saved as they were.

The editor saves its version in `netdeployVersion`. Opening a package saved by another version shows a warning, and a
setting this version doesn't know (from a newer NetDeploy, or one since removed) is left out with a warning instead of
stopping the package from opening.

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

### Installs

Add an `install` block so NetDeploy can add, update, rename, clone and remove installs of the app:

```jsonc
"install": { "folder": "{InstallDir}" }
```

- Every install has a name and its own subfolder of the install folder, for example `C:\Apps\Orders\main\Orders.exe`,
  whether there is one install or several.
- Steps use `{InstanceDir}` for the install's folder, and `{Instance}` (the name) or `{InstanceSuffix}` (`-main`) in
  service and task names, so installs side by side don't clash.

NetDeploy saves nothing extra to mark an install. It finds installs from the folder layout: each subfolder of the
install folder that holds the app's exe (or its dlls, for an app without one) is an install. Installs copied by hand are
found the same way. The version shown is read from the exe.

**Removing an install** runs the open package's undo steps, newest first, then removes any other service whose program
is in the folder (read from Windows, so services installed by hand are included), then deletes the folder. Each step is
shown with exactly what it will do and needs its own OK. The built-in undo:

- A `service` install removes the service.
- A `scheduledTask` createOrUpdate deletes the task.
- A `backup` deletes the backups.

Give a step `cleanup` PowerShell to run after its built-in undo, for example to drop a database:
`"cleanup": "Remove-Item -LiteralPath '{DataDir}\\db{InstanceSuffix}' -Recurse -Force"`. The editor shows what each step
undoes by itself above the box. The older form, a list of steps (`"cleanup": [ ... ]`), still works and replaces the
built-in undo; `"cleanup": []` means nothing to undo.

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
config file should change and shows the **config review** in the right-hand pane. Each file opens in the
track-changes editor as it will be written, marked against the server's copy: what the deploy adds in green, what it
takes out struck through in red, each with a button to keep the server's text instead. Where the text keeps the
server's value but the package has another, a dashed outline offers the package's. Settings that need a value are
outlined in amber and listed above the file, with buttons to settle them; typing a value in the text settles them too.
Edit anything else in the text as you like. Nothing is written until you continue. A dry run shows the same review
without letting you edit it.

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
- **A value can come from the deploy.** A setting whose value in the package is `{{Name}}` or `{{Name|default}}`
  takes the parameter or input called `Name`, or the default when there is none. A value the server already has is
  kept as usual.
- **A setting must not go live with a placeholder.** A new setting with an empty value, a `{{Name}}` with no value
  or default, or any value such as `CHANGE_ME`, `TODO`, `<your key>` or `__KEY__`, needs a value before the files can
  be written, unless an override on the server already sets it.

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
