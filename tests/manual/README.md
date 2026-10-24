# Manual test runner

An [AutoHotkey v2](https://www.autohotkey.com/) script that runs NetDeploy's manual test on your computer at a pace
you can watch. It moves the mouse to each control, outlines it in orange and says what it is doing in a panel beside
the app. Nothing needs you to type or click while it runs. At each checkpoint it checks what it can by itself
(services, scheduled tasks, files, settings, the database, the log) and lists what to look at by hand, with buttons that
open it: the install folder, the services, the tasks, the database, a settings file. It shows them for a few seconds
and carries on; press **F8** to hold it there and look closer, or tick **Stop at each checkpoint** in the launcher to
have it wait every time.

It installs real services and scheduled tasks, so run it on a test machine or one where you don't mind
`C:\Apps\Contoso.Orders`, `C:\Apps\Fabrikam.Reports` and `C:\ProgramData\Contoso.Orders` coming and going.

## Running it

1. Install AutoHotkey v2, or use the ready-made bundle, which has `AutoHotkey64.exe` in it.
2. Double-click `NetDeployManualTests.ahk` (or `Run tests.cmd` in the bundle). It asks to run as administrator,
   because deploys install services.
3. Check where NetDeploy.exe and the test packages are. It looks next to the script first (the bundle), then where a
   build of this repository puts them (`artifacts\portable\NetDeploy.exe` and `testpackages\dist`). **Build them**
   runs both builds if they are missing.
4. Pick the cases and the speed, then **Start**. Starting from case 1 cleans up anything an earlier run left first.
   Leave the mouse and keyboard alone while it runs, or pause first.

| Key | What it does |
| --- | --- |
| **F8** | Pause (or hold at a checkpoint), and continue |
| **F12** | Stop the run (asks first) |

**Note a problem…** saves what you type, with a screenshot, against the case that is running. If the runner can't
find something, it says what and lets you put the app right by hand and retry, skip the rest of the case, or stop.
The speed can be changed at any time from the panel.

Each run saves `run.log`, `summary.txt` and any screenshots in `results\<date and time>` next to the script.
`tools\Reset-TestMachine.ps1` removes everything the test packages install; **Clean up** in the launcher runs it.

## The cases

They run in order, each starting where the one before it ended, so a case run on its own needs the state the earlier
ones leave (the launcher's Needs column).

| # | Case | Checkpoint |
| --- | --- | --- |
| 1 | Open a package | The package's settings, Steps and deploy.json tabs |
| 2 | Dry run a new install | Nothing was created |
| 3 | Add the first install (main) | Service running (and its green dot in the folder list), task, database, settings, log |
| 4 | Add a second install (blue) for Production | Both services, Production branch, separate databases |
| 5 | Change settings on the server in the Files tab | Region, an Alerts section added as text, and BatchSize written, README kept |
| 6 | Cancel an upgrade at the config check, then roll back | blue back on 1.0.0 and running |
| 7 | Upgrade main to 1.1.0 | The three-way merge, rename, override check, heartbeat on 1.1.0, database schema 2 |
| 8 | Rename blue to green | green running, blue's service, task, database and folder gone |
| 9 | Clone main as qa | qa has main's settings |
| 10 | Edit a deploy.json in the editor | Saved version, folder open as the package |
| 11 | Manage the install folder without a package | Run an exe with arguments, delete a file |
| 12 | Add Fabrikam.Reports 2.0.0 | Data, first report, hourly task |
| 13 | Upgrade Fabrikam.Reports to 2.1.0 | .exe.config and web.config merges, plugins swapped |
| 14 | Remove every install | Services, tasks, databases and folders gone |

## How it works

`NetDeployManualTests.ahk` is the launcher. `lib\Cases.ahk` holds the cases, written as the steps a person would take.
`lib\App.ahk` finds NetDeploy's controls with UI Automation, by what a person reads on them: a button's text, a field's
label, a tooltip. `lib\Runner.ahk` is the panel, the pace, pausing and checkpoints, and `lib\Machine.ahk` the checks and
the buttons that open things. `lib\UIA.ahk` is [Descolada's UIA-v2](https://github.com/Descolada/UIA-v2) 1.1.3 (MIT).

CI runs every case unattended on a Windows runner (the Manual tests workflow): no pauses or checkpoints, and the first
control it can't find ends the run with a screenshot and a dump of the UI Automation tree in the ManualTestResults
artifact. So when a UI change renames a button or a field, that workflow fails before the manual test does.

```powershell
AutoHotkey64.exe NetDeployManualTests.ahk --unattended [--cases 1-5] [--exe <NetDeploy.exe>] [--packages <folder>]
```
