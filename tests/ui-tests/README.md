# NetDeploy UI tests

NetDeploy's manual test plan as 23 automated UI test cases, run by
[AutoTestRunner](https://github.com/connorstevenson44/AutoTestRunner): a C# test runner on FlaUI whose cases are JSON.
The mouse moves to each control, it is outlined in orange, a panel beside the app lists each step with the controls
it uses on badges, and each checkpoint shows its checks as expected beside actual, then things to look at. The cases replaced the AutoHotkey runner
that was in `tests/manual`, and CI runs them on every change in the **UI tests** workflow.

## Running it on a test machine

Take the **UiTests-ReadyToRun** artifact from any run of the UI tests workflow (the run's Summary page, under
Artifacts), unzip it and double-click `Open test launcher.cmd`. It asks for administrator, then opens the launcher:
tick the cases, pick how it runs and how fast, and Start. The launcher shows how each case went last time, and whether
an earlier run left services, tasks or folders behind, with a button that cleans them up. Step by step, beside Start,
waits for Enter before every step. **New case** opens the builder, which records a case as you use NetDeploy: see
[Building a case](https://github.com/connorstevenson44/AutoTestRunner#building-a-case). The pencil on a case opens it
to change: the builder plays it to any step, rolls back to one, or records again from one, and marks the cases that
carry on from it as needing a look, with a warning in the launcher.

The kit has the test runner (`autotestrunner\autotest.exe`), the suite, NetDeploy.exe and the test packages, and needs
no .NET runtime. Arguments after the `.cmd` go to `autotest launch`, so `"Open test launcher.cmd" --cases 3-5` ticks
just those, and `--mode step-by-step` starts it set to step by step.

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
autotest record tests\ui-tests\netdeploy --after open-package       # builds a new case from where case open-package leaves off
```

It finds NetDeploy.exe and the test packages where a build of this repository puts them
(`artifacts\portable\NetDeploy.exe` and `tests\test-packages\dist`), or next to the suite in the kit. Space pauses and
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
| `tools/Count-Deploys.ps1` | How many deploys the history has for an install, or how many were added since a count |
| `tools/Add-AppLogs.ps1` | Gives an install an nlog.config and archived logs, one zipped |
| `tools/Add-FailingStep.ps1` | Turns an unzipped 1.1.0 package into a 1.2.0 whose deploy fails before the service starts |

The macros, which cases run with `{ "call": name }`:

| Macro | What it does |
| --- | --- |
| `answerRemovalStep` | Answers the Remove install window for one step: runs it, skips it or stops removing. |
| `clickFileRowButton` | One of the buttons on a file's row in the Files tab, which show when the mouse is over the row. |
| `clickInstallRowButton` | One of the buttons that show when the mouse is over an install's row, such as Clone or Remove. |
| `continueInputsWindow` | Clicks Continue in a window that asks for a step's details, and waits for it to close. |
| `continuePastReview` | Continue, Continue the dry run, or Write files and continue: the button under a review. |
| `goUpToInstallFolder` | Goes up to the top of the install folder, from wherever the folder list is. |
| `openFileInEditor` | Opens a file in the Files tab's editor, with the Edit button on its row. |
| `openFolderInList` | Goes into a folder in the folder list on the left, by its full path. |
| `openPackage` | Opens a package zip with Open package and the Windows file picker. |
| `removeInstall` | Removes an install with the Remove button on its row, then answers each step NetDeploy asks about. |
| `runShownRemovalStep` | Runs the step the Remove install window shows: ticks I understand, then Run this step. |
| `showConfigFileDiff` | Shows one config file's diff in the config review, by clicking its chip. |
| `stepThroughRemoval` | Answers each step of a removal in the Remove install window, until the bar at the bottom says it is done. |
| `waitForHeader` | Waits for the header to name what is open, such as Contoso.Orders 1.0.0. |
| `waitForReview` | Waits for one of the deploy's reviews, by its title on the right. |
| `waitForStatusBar` | Waits for the bar at the bottom to start with startsWith, after the last click changed it. Another window showing up instead, such as a failure's roll back offer, stops the wait. |

Most of NetDeploy's buttons have no AutomationId yet, so the cases find them by their text and tooltips. Giving the
controls a test touches an `x:Name` or `AutomationProperties.AutomationId` lets the elements switch to ids, and keeps
the tests working when wording changes.
