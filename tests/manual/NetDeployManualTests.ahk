; NetDeploy's manual test, run by AutoHotkey at a pace you can watch. See README.md.
;
;   Double-click it to pick the cases, the speed and how it runs. Space pauses and carries on, Enter does one step
;   at a time, Esc stops.
;   Unattended, as CI runs it:
;     AutoHotkey64.exe NetDeployManualTests.ahk --unattended [--exe <NetDeploy.exe>] [--packages <folder>]
;                      [--results <folder>] [--cases 1-18]

#Requires AutoHotkey v2.0
#SingleInstance Force
#Warn All, StdOut
SetTitleMatchMode(3)

#Include lib\UIA.ahk
#Include lib\Runner.ahk
#Include lib\App.ahk
#Include lib\Machine.ahk
#Include lib\Cases.ahk

; Where NetDeploy.exe and the test packages are. They are looked for next to this script (as in the ready-made bundle),
; then where a build of the repository puts them.
class Settings {
    static Unattended := false
    static Exe := ""
    static Packages := ""
    static Results := ""
    static Cases := ""
    static Speed := "Modest"
    static Repo := A_ScriptDir "\..\.."
    static PackageNames := ["Contoso.Orders-1.0.0", "Contoso.Orders-1.1.0", "Fabrikam.Reports-2.0.0", "Fabrikam.Reports-2.1.0"]

    static Read(args) {
        index := 1
        while index <= args.Length {
            switch args[index] {
                case "--unattended": this.Unattended := true
                case "--exe": this.Exe := args[++index]
                case "--packages": this.Packages := args[++index]
                case "--results": this.Results := args[++index]
                case "--cases": this.Cases := args[++index]
                case "--speed": this.Speed := args[++index]
            }
            index++
        }
        if this.Exe = ""
            this.Exe := FirstExisting([A_ScriptDir "\NetDeploy.exe", this.Repo "\artifacts\portable\NetDeploy.exe"])
        if this.Packages = ""
            this.Packages := FirstExisting([A_ScriptDir "\packages", this.Repo "\testpackages\dist"])
        this.Exe := FullPath(this.Exe), this.Packages := FullPath(this.Packages)
        if this.Results = ""
            this.Results := A_ScriptDir "\results"
        this.Results := FullPath(this.Results) "\" FormatTime(, "yyyy-MM-dd HHmmss")
    }

    static MissingPackages() {
        missing := []
        for name in this.PackageNames
            if !FileExist(this.Packages "\" name ".zip")
                missing.Push(name ".zip")
        return missing
    }

    static IsRepo => FileExist(this.Repo "\NetDeploy.slnx")
}

FirstExisting(paths) {
    for path in paths
        if FileExist(path)
            return path
    return paths[paths.Length]
}

FullPath(path) {
    if path = ""
        return ""
    size := DllCall("GetFullPathName", "str", path, "uint", 0, "ptr", 0, "ptr", 0)
    full := Buffer(size * 2)
    DllCall("GetFullPathName", "str", path, "uint", size, "ptr", full, "ptr", 0)
    return RTrim(StrGet(full), "\")
}

; Picks cases by number, such as "1-5,8".
SelectCases(spec) {
    if spec = ""
        return Cases.All
    picked := []
    for part in StrSplit(spec, ",") {
        range := StrSplit(Trim(part), "-")
        Loop range.Length = 2 ? range[2] - range[1] + 1 : 1
            picked.Push(Cases.All[range[1] + A_Index - 1])
    }
    return picked
}

; The test deploys services and scheduled tasks, so it and NetDeploy run as administrator.
EnsureAdmin() {
    if A_IsAdmin
        return
    arguments := ""
    for arg in A_Args {
        if arg = "--unattended" {
            FileAppend("NetDeploy's manual tests need to run as administrator.`n", "*")
            ExitApp(4)
        }
        arguments .= ' "' arg '"'
    }
    try Run('*RunAs "' A_AhkPath '" /restart "' A_ScriptFullPath '"' arguments)
    ExitApp()
}

UnhandledError(error, mode) {
    try Runner.Log("ERROR", error.Message " (" error.What ", line " error.Line ")")
    if Runner.Unattended {
        try Runner.Capture("unhandled")
        ExitApp(3)
    }
    return 0
}

Main() {
    EnsureAdmin()
    Settings.Read(A_Args)
    App.Exe := Settings.Exe
    OnError(UnhandledError)

    if Settings.Unattended {
        Runner.Init(true, "Fast", Settings.Results)
        Runner.Log("START", "NetDeploy " Settings.Exe ", packages " Settings.Packages)
        if !FileExist(Settings.Exe) || Settings.MissingPackages().Length {
            Runner.Log("ERROR", "NetDeploy.exe or a package is missing")
            ExitApp(2)
        }
        if (leftovers := Machine.Leftovers()).Length {
            Runner.Log("RESET", "Cleaning up first: " Join(leftovers, ", "))
            Machine.Reset()
        }
        failed := Runner.RunCases(SelectCases(Settings.Cases))
        App.CloseAll()
        ExitApp(failed ? 1 : 0)
    }
    Launcher.Show()
}

; The window to pick the cases, the speed and how it runs, and to clean up, before a run you watch.
class Launcher {
    static Show() {
        ui := Gui("+MinSize", "NetDeploy manual tests")
        ui.BackColor := "FFFFFF"
        ui.MarginX := 18, ui.MarginY := 14
        ui.SetFont("s14 c004B87 bold", "Segoe UI")
        ui.Add("Text", "w720", "NetDeploy manual tests")
        ui.SetFont("s9 c5B6B7C norm")
        ui.Add("Text", "w720 y+4", "Runs NetDeploy's manual test on this computer while you watch: it clicks and types for you, "
            . "says what it is doing, and checks the results. Space pauses and carries on, Enter does one step at a time, "
            . "and Esc stops.")

        ui.SetFont("s9 c1A2633 bold")
        ui.Add("Text", "w720 y+14", "NetDeploy.exe")
        ui.SetFont("norm")
        this.ExeBox := ui.Add("Edit", "w600 y+4 ReadOnly", Settings.Exe)
        ui.Add("Button", "x+8 yp-1 w112", "Browse…").OnEvent("Click", (*) => Launcher.BrowseExe())
        ui.SetFont("bold")
        ui.Add("Text", "xm w720 y+10", "Test packages")
        ui.SetFont("norm")
        this.PackagesBox := ui.Add("Edit", "w600 y+4 ReadOnly", Settings.Packages)
        ui.Add("Button", "x+8 yp-1 w112", "Browse…").OnEvent("Click", (*) => Launcher.BrowsePackages())
        this.FilesNote := ui.Add("Text", "xm w600 y+6 cC62828", "")
        this.BuildButton := ui.Add("Button", "x+8 yp-4 w112 Hidden", "Build them")
        this.BuildButton.OnEvent("Click", (*) => Launcher.Build())

        ui.SetFont("bold")
        ui.Add("Text", "xm w720 y+12", "This computer")
        ui.SetFont("norm")
        this.MachineNote := ui.Add("Text", "w600 y+4 r2", "")
        this.CleanButton := ui.Add("Button", "x+8 yp w112 Hidden", "Clean up")
        this.CleanButton.OnEvent("Click", (*) => Launcher.CleanUp())

        ui.SetFont("bold")
        ui.Add("Text", "xm w720 y+10", "Cases (each one carries on from where the one before it ended)")
        ui.SetFont("norm")
        this.List := ui.Add("ListView", "w720 r18 y+4 Checked -Multi", ["#", "Case", "Run after", "What it tries"])
        for index, testCase in Cases.All
            this.List.Add("Check", index, testCase.Name, testCase.Needs, testCase.Covers)
        this.List.ModifyCol(1, 30), this.List.ModifyCol(2, 300), this.List.ModifyCol(3, 140), this.List.ModifyCol(4, 900)

        ui.SetFont("bold")
        ui.Add("Text", "xm w720 y+12", "How it runs")
        ui.SetFont("norm")
        this.ByItself := ui.Add("Radio", "xm w720 y+4 Checked", "By itself, showing each check for a few seconds (Space holds it there)")
        this.WaitAtChecks := ui.Add("Radio", "xm w720 y+2", "By itself, waiting for you at each check")
        this.EachStep := ui.Add("Radio", "xm w720 y+2", "Step by step: it waits for you to press Enter before each step")

        ui.Add("Text", "xm w50 h26 y+12 0x200", "Speed")
        this.Speed := ui.Add("DropDownList", "x+4 yp w120", ["Slow", "Modest", "Fast"])
        this.Speed.Choose(Settings.Speed)
        ui.SetFont("s10 bold")
        start := ui.Add("Button", "x+300 yp-4 w120 h34 Default", "Start")
        start.OnEvent("Click", (*) => Launcher.Start())
        ui.SetFont("s9 norm")
        ui.Add("Button", "x+8 yp w110 h34", "Close").OnEvent("Click", (*) => ExitApp())
        ui.OnEvent("Close", (*) => ExitApp())
        this.Ui := ui
        this.Refresh()
        ui.Show()
    }

    static Refresh() {
        missing := []
        if !FileExist(Settings.Exe)
            missing.Push("NetDeploy.exe")
        for name in Settings.MissingPackages()
            missing.Push(name)
        this.FilesNote.Text := missing.Length ? "Can't find: " Join(missing, ", ") : ""
        this.BuildButton.Visible := missing.Length && Settings.IsRepo

        leftovers := Machine.Leftovers()
        this.MachineNote.Text := leftovers.Length
            ? "Left over from an earlier run: " Join(leftovers, ", ") ". Starting from case 1 cleans it up first."
            : "Ready: none of the test packages' services, tasks or folders are here."
        this.CleanButton.Visible := leftovers.Length > 0
    }

    static BrowseExe() {
        if path := FileSelect(1, Settings.Exe, "Find NetDeploy.exe", "NetDeploy (NetDeploy.exe)")
            Settings.Exe := path, App.Exe := path, this.ExeBox.Value := path
        this.Refresh()
    }

    static BrowsePackages() {
        if path := DirSelect("*" Settings.Packages, 0, "Pick the folder that has the four test package zips in it")
            Settings.Packages := path, this.PackagesBox.Value := path
        this.Refresh()
    }

    ; Builds the portable exe and the test packages from the repository, in a window you can watch.
    static Build() {
        this.Ui.Opt("+Disabled")
        RunWait(A_ComSpec ' /c "cd /d "' Settings.Repo '" && dotnet publish src/NetDeploy.App -p:PublishProfile=Portable && dotnet run testpackages/build.cs & pause"')
        this.Ui.Opt("-Disabled")
        Settings.Exe := FullPath(Settings.Repo "\artifacts\portable\NetDeploy.exe"), App.Exe := Settings.Exe
        Settings.Packages := FullPath(Settings.Repo "\testpackages\dist")
        this.ExeBox.Value := Settings.Exe, this.PackagesBox.Value := Settings.Packages
        this.Refresh()
    }

    static CleanUp() {
        if MsgBox("Remove everything the test packages installed: their services, scheduled tasks and folders "
            . "(C:\Apps\Contoso.Orders, C:\Apps\Fabrikam.Reports and C:\ProgramData\Contoso.Orders)?", "Clean up", "OKCancel Icon?") != "OK"
            return
        App.CloseAll()
        this.Ui.Opt("+Disabled")
        Machine.Reset()
        this.Ui.Opt("-Disabled")
        this.Refresh()
    }

    static Start() {
        picked := []
        row := 0
        while row := this.List.GetNext(row, "Checked")
            picked.Push(Cases.All[row])
        if !picked.Length
            return MsgBox("Tick at least one case to run.", "NetDeploy tests")
        if !FileExist(Settings.Exe) || Settings.MissingPackages().Length
            return MsgBox("NetDeploy.exe or one of the test packages can't be found. Use Browse to find them, or Build them.", "NetDeploy tests", "Icon!")
        speed := this.Speed.Text
        Runner.StopAtCheckpoints := this.WaitAtChecks.Value
        Runner.StepByStep := this.EachStep.Value
        this.Ui.Destroy()
        Runner.Init(false, speed, Settings.Results)
        Runner.Log("START", "NetDeploy " Settings.Exe ", packages " Settings.Packages)
        ; The first case expects a clean computer, so anything an earlier run left is removed first, as CI does.
        if picked[1] = Cases.All[1] && (leftovers := Machine.Leftovers()).Length {
            Runner.Step("Clean up what an earlier run left behind: " Join(leftovers, ", "))
            App.CloseAll(), Machine.Reset()
        }
        Runner.RunCases(picked)
        ExitApp()
    }
}

Main()
