# Walkthrough suite

NetDeploy's manual test, being ported from the AutoHotkey runner in `tests/manual` to
[Walkthrough](https://github.com/connorstevenson44/AutoTestRunner): a C# runner on FlaUI whose cases are JSON. The
person watching sees the same thing: the mouse moves to each control, it is outlined in orange, a panel beside the
app says what is happening, and each case ends at a checkpoint of checks it makes itself and things to look at.

So far cases 1 and 2 are ported. Until every case is, and the suite has passed on main for two weeks, the AutoHotkey
runner stays the one to use, and both run in CI.

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

| File | What it is | Was |
| --- | --- | --- |
| `suite.json` | How to start NetDeploy, the variables, what must exist, how to reset the machine | `Settings`, `App.Start`, `Machine.Reset` |
| `elements.json` | NetDeploy's controls by name | The conditions in `App.ahk` |
| `macros/` | `openPackage`, `waitHeader`, `waitStage`, `continueStage`, `configFile`, `waitStatus` | The NetDeploy helpers in `App.ahk` |
| `cases/` | One file per case, each step a line of `Cases.ahk` | `Cases.ahk` |
| `tools/Leftovers.ps1` | What an earlier run left behind | `Machine.Leftovers` |

The reset is `tests\manual\tools\Reset-TestMachine.ps1`, shared with the AutoHotkey runner.

Most of NetDeploy's buttons have no AutomationId yet, so the cases find them by their text and tooltips, as the
AutoHotkey cases do. Giving the controls a test touches an `x:Name` or `AutomationProperties.AutomationId` lets the
elements switch to ids, and keeps the tests working when wording changes.
