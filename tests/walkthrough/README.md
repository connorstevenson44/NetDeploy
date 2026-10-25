# Walkthrough suite

NetDeploy's manual test, run by [Walkthrough](https://github.com/connorstevenson44/AutoTestRunner): a C# runner on
FlaUI whose cases are JSON. The mouse moves to each control, it is outlined in orange, a panel beside the app says
what is happening, and each case ends at a checkpoint of checks it makes itself and things to look at. Its 22 cases
replaced the AutoHotkey runner that was in `tests/manual`, and CI runs them on every change.

## Running it

On a test machine, take the **WalkthroughBundle** artifact from any run of the Walkthrough workflow (the run's
Summary page, under Artifacts), unzip it and double-click `Run tests.cmd`, or `Step through.cmd` to go a step at a
time. It asks for administrator. The bundle has `walk`, the suite, NetDeploy.exe and the test packages, and needs no
.NET runtime. Arguments after `Run tests.cmd` go to `walk run`, so `"Run tests.cmd" --cases 3-5` runs just those.

From a clone, build `walk` from the [AutoTestRunner](https://github.com/connorstevenson44/AutoTestRunner) repository, then from this repository's root, as administrator:

```powershell
walk run tests\walkthrough\netdeploy                     # watch it, from case 1
walk run tests\walkthrough\netdeploy --cases 1-2 --mode steps
walk run tests\walkthrough\netdeploy --unattended        # as CI runs it
walk check tests\walkthrough\netdeploy                   # just check the scripts
```

It finds NetDeploy.exe and the test packages where a build of this repository puts them
(`artifacts\portable\NetDeploy.exe` and `testpackages\dist`), or next to the suite in a bundle. Space pauses and
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
| `tools/Leftovers.ps1` | What an earlier run left behind |
| `tools/Services.ps1`, `Tasks.ps1`, `Database.ps1` | What the checkpoints' Services, Scheduled tasks and Database buttons show |

Most of NetDeploy's buttons have no AutomationId yet, so the cases find them by their text and tooltips. Giving the
controls a test touches an `x:Name` or `AutomationProperties.AutomationId` lets the elements switch to ids, and keeps
the tests working when wording changes.
