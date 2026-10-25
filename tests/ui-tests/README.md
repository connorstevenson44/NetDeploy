# NetDeploy UI tests

NetDeploy's manual test plan as 22 automated UI test cases, run by
[AutoTestRunner](https://github.com/connorstevenson44/AutoTestRunner): a C# test runner on FlaUI whose cases are JSON.
The mouse moves to each control, it is outlined in orange, a panel beside the app says what is happening, and each
case ends at a checkpoint of checks it makes itself and things to look at. The cases replaced the AutoHotkey runner
that was in `tests/manual`, and CI runs them on every change in the **UI tests** workflow.

## Running it on a test machine

Take the **UiTests-ReadyToRun** artifact from any run of the UI tests workflow (the run's Summary page, under
Artifacts), unzip it and double-click `Open test launcher.cmd`. It asks for administrator, then opens the launcher:
tick the cases, pick how it runs and how fast, and Start. The launcher shows how each case went last time, and whether
an earlier run left services, tasks or folders behind, with a button that cleans them up.
`Open test launcher (step by step).cmd` opens it set to wait for Enter before every step.

The kit has the test runner (`autotestrunner\autotest.exe`), the suite, NetDeploy.exe and the test packages, and needs
no .NET runtime. Arguments after either `.cmd` go to `autotest launch`, so `"Open test launcher.cmd" --cases 3-5` ticks
just those.

While it runs, the panel beside the app shows the case, what it is doing now and what it is waiting for, and below,
everything it has done. Each command a case runs, NetDeploy.exe's or a PowerShell script's, shows there with its
command line and what it prints as it prints it, then its exit code.

## Running it from a clone

Build `autotest.exe` from the [AutoTestRunner](https://github.com/connorstevenson44/AutoTestRunner) repository, then
from this repository's root, as administrator:

```powershell
autotest launch tests\ui-tests\netdeploy                           # opens the launcher to pick cases
autotest run tests\ui-tests\netdeploy                              # runs every case while you watch
autotest run tests\ui-tests\netdeploy --cases 1-2 --mode step-by-step
autotest run tests\ui-tests\netdeploy --unattended                 # no panel and no pauses, as CI runs it
autotest validate tests\ui-tests\netdeploy                         # checks the scripts without running them
```

It finds NetDeploy.exe and the test packages where a build of this repository puts them
(`artifacts\portable\NetDeploy.exe` and `testpackages\dist`), or next to the suite in the kit. Space pauses and
carries on, Enter goes a step at a time, Esc stops. Results go in `netdeploy\results\<date and time>`: `run.log`,
`summary.txt`, `junit.xml`, and a screenshot and UI Automation dump of anything it got stuck on.

## The suite

| File | What it is |
| --- | --- |
| `suite.json` | How to start NetDeploy, the variables, what must exist, how to reset the machine |
| `elements.json` | NetDeploy's controls by name |
| `macros/` | Opening a package, the deploy's reviews, the folder list and its hover buttons, a file's buttons, removing an install a step at a time |
| `cases/` | One file per case |
| `tools/Reset-TestMachine.ps1` | Removes everything the test packages install, before a run or by hand |
| `tools/Find-Leftovers.ps1` | What an earlier run left behind |
| `tools/Show-Services.ps1`, `Show-ScheduledTasks.ps1`, `Show-Database.ps1` | What the checkpoints' Services, Scheduled tasks and Database buttons show |

The macros, which cases run with `{ "call": name }`:

| Macro | What it does |
| --- | --- |
| `answerRemovalStep` | Answers the Remove install window for one step: says what the step does, then runs it, skips it or stops removing. |
| `clickFileRowButton` | One of the buttons on a file's row, which in the Files tab show when the mouse is over the row. |
| `clickInstallRowButton` | One of the buttons that show when the mouse is over an install's row, such as Clone or Remove. |
| `continueInputsWindow` | Continues in a window that asks for a step's details, once it is filled in, and waits for it to close. |
| `continuePastReview` | Continue, Continue the dry run, or Write files and continue: the button under a review. |
| `goUpToInstallFolder` | Goes up to the top of the install folder, from wherever the folder list is. |
| `openFileInEditor` | Opens a file in the Files tab's editor, with the Edit button on its row. |
| `openFolderInList` | Goes into a folder in the folder list on the left, by its full path. |
| `openPackage` | Opens a package zip with Open package and the Windows file picker. |
| `removeInstall` | Removes an install with the Remove button that shows when the mouse is over its row, then goes through each step NetDeploy asks about. |
| `runShownRemovalStep` | Runs the step the Remove install window shows: ticks I understand, then Run this step. |
| `showConfigFileDiff` | Shows one config file's diff in the config review, by clicking its chip. |
| `stepThroughRemoval` | Goes through the steps NetDeploy asks about one at a time while it removes an install, until the bar at the bottom says it is done. |
| `waitForHeader` | Waits for the header to name what is open, such as Contoso.Orders 1.0.0. |
| `waitForReview` | Waits for one of the deploy's reviews, by its title on the right. |
| `waitForStatusBar` | Waits for the bar at the bottom to start with startsWith, after the last click changed it. Another window showing up instead, such as a failure's roll back offer, stops the wait. |

Most of NetDeploy's buttons have no AutomationId yet, so the cases find them by their text and tooltips. Giving the
controls a test touches an `x:Name` or `AutomationProperties.AutomationId` lets the elements switch to ids, and keeps
the tests working when wording changes.
