# Manual test runner

An [AutoHotkey v2](https://www.autohotkey.com/) script that runs NetDeploy's manual test on your computer while you
watch. It moves the mouse to each button or box, outlines it in orange, and says what it is doing in a panel beside
the app. You don't need to type or click anything. At the end of each case it checks what it can by itself (services,
scheduled tasks, files, settings, the database, the log), then lists what to look at yourself, with buttons that open
it: the install folder, the services, the tasks, the database, a settings file.

It installs real Windows services and scheduled tasks, so run it on a test computer, or one where you don't mind
`C:\Apps\Contoso.Orders`, `C:\Apps\Fabrikam.Reports` and `C:\ProgramData\Contoso.Orders` coming and going.

## Running it

1. Install AutoHotkey v2, or use the ready-made bundle, which has `AutoHotkey64.exe` in it.
2. Double-click `NetDeployManualTests.ahk` (or `Run tests.cmd` in the bundle). It asks to run as administrator,
   because installing services needs it.
3. Check it has found NetDeploy.exe and the test packages. It looks next to the script first (the bundle), then where a
   build of this repository puts them (`artifacts\portable\NetDeploy.exe` and `testpackages\dist`). If they are
   missing, **Build them** builds both.
4. Tick the cases you want, pick the speed and how it runs, then **Start**. Starting from case 1 first cleans up
   anything an earlier run left behind.

How it runs:

- **By itself** shows each check for a few seconds and carries on. Press **Space** to stay on one and look closer.
- **By itself, waiting at each check** stops at every check until you carry on.
- **Step by step** waits before every step until you press **Enter**, so you can follow it one click at a time.

You can switch at any time: **Enter** turns step by step on, and **Space** carries on by itself again.

| Key | What it does |
| --- | --- |
| **Space** | Pause, or carry on by itself |
| **Enter** | Do the next step, then wait |
| **Esc** | Stop the test (it asks first) |

The panel has a button for each of these too. The keys belong to the test while it runs, so leave the keyboard alone
or pause first. Once you click into a text box yourself, such as one in NetDeploy or the box for a note, the keys type
there as usual; click outside it, or use the panel's buttons, to use them for the test again.

**Note a problem…** saves what you type, with a screenshot, against the case that is running. If the test can't find
something, it says what, and you can put NetDeploy right by hand and try again, skip the rest of the case, or stop.
The speed can be changed at any time from the panel.

Each run saves `run.log`, `summary.txt` and any screenshots in `results\<date and time>` next to the script.
`tools\Reset-TestMachine.ps1` removes everything the test packages install; **Clean up** in the launcher runs it.

## The cases

They run in order, each carrying on from where the one before it ended, so a case run on its own needs what the earlier
ones leave behind (the launcher's Run after column).

| # | Case | What it checks at the end |
| --- | --- | --- |
| 1 | Open a package | The package's settings, and its Steps and deploy.json tabs |
| 2 | Try a deploy without changing anything | Nothing was created |
| 3 | Install the app for the first time (main) | The service is running (with a green dot in the folder list), and the task, database, settings and log |
| 4 | Notice when a service stops | The dot goes red when the service is stopped outside NetDeploy, and green again when it starts |
| 5 | Install it a second time, for Production (blue) | Both services, the Production steps, separate databases |
| 6 | Change settings on the server | Region and an Alerts section typed in, one change undone, BatchSize written, README kept |
| 7 | Throw away changes you don't want | Undo all and Discard leave the files as they were |
| 8 | Cancel an upgrade halfway, then roll it back | blue is back on 1.0.0 and running |
| 9 | Upgrade main to 1.1.0, keeping the server's changes | How settings were combined, the renamed setting, the heartbeat on 1.1.0, the upgraded database |
| 10 | Rename blue to green | green is running, and blue's service, task, database and folder are gone |
| 11 | Copy main as a new install (qa) | qa has main's settings |
| 12 | Change your mind while removing an install | Stop removing leaves qa just as it was |
| 13 | Remove an install someone copied by hand | A copied folder shows up as an install, and removing it skips what it never had |
| 14 | Edit a deploy.json | The saved version, and its folder open as the package |
| 15 | Look after an install folder without a package | Running a program with what to pass it, and deleting a file |
| 16 | Install Fabrikam.Reports 2.0.0 | Its data, the first report, the hourly task |
| 17 | Upgrade Fabrikam.Reports to 2.1.0 | Its two settings files combined, and the plugins swapped |
| 18 | Remove every install | Services, tasks, databases and folders all gone |

## How it works

`NetDeployManualTests.ahk` is the launcher. `lib\Cases.ahk` holds the cases, written as the steps a person would take.
`lib\App.ahk` finds NetDeploy's buttons and boxes with UI Automation, by what a person reads on them: a button's text,
a box's label, a tooltip. `lib\Runner.ahk` is the panel, the pace, pausing, going step by step and the checks, and
`lib\Machine.ahk` the checks themselves and the buttons that open things. `lib\UIA.ahk` is
[Descolada's UIA-v2](https://github.com/Descolada/UIA-v2) 1.1.3 (MIT).

CI runs every case unattended on a Windows runner (the Manual tests workflow): no pauses or waiting at checks, and the
first thing it can't find ends the run with a screenshot and a dump of the UI Automation tree in the ManualTestResults
artifact. So when a UI change renames a button or a box, that workflow fails before the manual test does.

```powershell
AutoHotkey64.exe NetDeployManualTests.ahk --unattended [--cases 1-5] [--exe <NetDeploy.exe>] [--packages <folder>]
```
