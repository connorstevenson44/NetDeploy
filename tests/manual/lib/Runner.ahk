; The run itself: the panel beside the app, the pace, pausing, checkpoints and the results.

; Thrown to give up on the rest of a case, after the person chose to skip it.
class SkipCase extends Error {
}

; Thrown to end the whole run: the person stopped it, or an unattended run got stuck.
class StopRun extends Error {
}

class Runner {
    static Unattended := false
    static StopAtCheckpoints := false  ; true waits at each checkpoint until the person continues
    static Pace := 1100          ; ms spent on each action, so it can be followed
    static MouseSpeed := 15      ; 0 is instant, 100 the slowest
    static KeyDelay := 60        ; ms between typed keys
    static TimeoutScale := 1     ; waits are longer on a slow CI machine
    static Waiting := ""         ; "", "pause", "checkpoint" or "stuck"
    static StopRequested := false
    static ResultsDir := ""
    static LogPath := ""
    static Results := []         ; one {Name, Status, Failures, Notes} per case run
    static Current := ""         ; the running case's result
    static Captures := 0
    static Ui := ""
    static ToolActions := []

    ; Pace, mouse speed and key delay for each speed.
    static Speeds := Map("Slow", [1800, 25, 110], "Modest", [1100, 15, 60], "Fast", [450, 5, 20])

    static Init(unattended, speed, resultsDir) {
        this.Unattended := unattended
        this.ResultsDir := resultsDir
        DirCreate(resultsDir)
        this.LogPath := resultsDir "\run.log"
        if unattended
            this.Pace := 150, this.MouseSpeed := 0, this.KeyDelay := 10, this.TimeoutScale := 3
        else
            this.SetSpeed(speed)
        this.BuildPanel(speed)
        Hotkey("F8", (*) => Runner.PauseOrContinue())
        Hotkey("F12", (*) => Runner.RequestStop())
    }

    static SetSpeed(name) {
        settings := this.Speeds[name]
        this.Pace := settings[1], this.MouseSpeed := settings[2], this.KeyDelay := settings[3]
    }

    ; ===== The panel =====

    static BuildPanel(speed) {
        width := 420, inner := width - 30
        ui := Gui("+AlwaysOnTop +ToolWindow -MinimizeBox", "NetDeploy manual tests")
        ui.BackColor := "FFFFFF"
        ui.MarginX := 15, ui.MarginY := 12

        ui.SetFont("s8 c5B6B7C bold", "Segoe UI")
        this.CaseLabel := ui.Add("Text", "w" inner, "STARTING")
        ui.SetFont("s12 c1A2633 bold")
        this.CaseName := ui.Add("Text", "w" inner " y+2", "")

        ui.SetFont("s8 c5B6B7C bold")
        ui.Add("Text", "w" inner " y+14", "NOW")
        ui.SetFont("s11 c1A2633 norm")
        this.StepText := ui.Add("Text", "w" inner " r4 y+3", "")
        ui.SetFont("s10 cWhite bold")
        this.StateText := ui.Add("Text", "w" inner " h30 y+6 Center 0x200 Background004B87", "Running")

        ui.SetFont("s8 c5B6B7C bold")
        this.CheckTitle := ui.Add("Text", "w" inner " y+14", "CHECKPOINT")
        ui.SetFont("s9 c1A2633 norm", "Segoe UI")
        this.CheckText := ui.Add("Edit", "w" inner " r13 y+4 ReadOnly VScroll -WantReturn", "")
        this.Tools := []
        Loop 6 {
            position := Mod(A_Index, 3) = 1 ? "xm y+6" : "x+6 yp"
            button := ui.Add("Button", position " w126 h28 Hidden", "")
            button.OnEvent("Click", ObjBindMethod(this, "RunTool", A_Index))
            this.Tools.Push(button)
            this.ToolActions.Push("")
        }

        ui.SetFont("s8 c5B6B7C bold")
        ui.Add("Text", "xm w" inner " y+14", "SO FAR")
        ui.SetFont("s9 c1A2633 norm")
        this.History := ui.Add("ListBox", "xm w" inner " r7 y+4 0x100")
        this.HistoryCount := 0

        ui.SetFont("s9 c5B6B7C bold")
        ui.Add("Text", "xm w50 h26 y+12 0x200", "Speed")
        ui.SetFont("s9 c1A2633 norm")
        this.SpeedList := ui.Add("DropDownList", "x+4 yp w110", ["Slow", "Modest", "Fast"])
        this.SpeedList.Choose(speed)
        this.SpeedList.OnEvent("Change", (control, *) => Runner.SetSpeed(control.Text))
        this.SpeedList.Enabled := !this.Unattended

        ui.SetFont("s10 bold")
        this.MainButton := ui.Add("Button", "xm w192 h36 y+10", "Pause  (F8)")
        this.MainButton.OnEvent("Click", (*) => Runner.PauseOrContinue())
        ui.SetFont("s9 norm")
        this.NoteButton := ui.Add("Button", "x+6 yp w192 h36", "Note a problem…")
        this.NoteButton.OnEvent("Click", (*) => Runner.NoteProblem())
        this.StopButton := ui.Add("Button", "xm w" inner " h28 y+6", "Stop the run  (F12)")
        this.StopButton.OnEvent("Click", (*) => Runner.RequestStop())
        ui.OnEvent("Close", (*) => (Runner.RequestStop(), true))

        this.Ui := ui
        ; Sizes are scaled for the screen's DPI, so the panel is placed by the size it ended up.
        ui.Show("NA Hide w" width)
        WinGetPos(, , &shownWidth, , ui)
        MonitorGetWorkArea(MonitorGetPrimary(), &left, &top, &right, &bottom)
        this.PanelWidth := shownWidth
        WinMove(right - shownWidth, top, , , ui)
        ui.Show("NA")
        if this.Unattended {
            ; Clicks and hovers pass through it, so it never gets in the way of the app.
            ui.Opt("+E0x20")
            WinSetTransparent(210, ui)
        }
    }

    ; Moves the panel to the other side of the screen when it covers what is about to be clicked.
    static KeepClear(rect) {
        if !this.Ui
            return
        WinGetPos(&x, &y, &w, &h, this.Ui)
        if rect.r < x || rect.l > x + w || rect.b < y || rect.t > y + h
            return
        MonitorGetWorkArea(MonitorGetPrimary(), &left, &top, &right, &bottom)
        WinMove(x > left + 10 ? left : right - w, top, , , this.Ui)
    }

    static SetState(text, color) {
        this.StateText.Opt("Background" color)
        this.StateText.Text := text
    }

    static AddHistory(text) {
        this.History.Add([FormatTime(, "HH:mm:ss") "  " text])
        this.HistoryCount++
        this.History.Choose(this.HistoryCount)
    }

    static ShowTools(tools) {
        for index, button in this.Tools {
            if index <= tools.Length {
                button.Text := tools[index].Label
                this.ToolActions[index] := tools[index].Fn
                button.Visible := true
            } else {
                button.Visible := false
                this.ToolActions[index] := ""
            }
        }
    }

    static RunTool(index, *) {
        if action := this.ToolActions[index]
            try action()
            catch Error as e
                MsgBox("That didn't open: " e.Message, "NetDeploy tests", "Icon! 0x40000")
    }

    ; ===== Pace, pausing and stopping =====

    ; Shows what is about to happen, then waits while the run is paused.
    static Step(text) {
        this.Log("STEP", text)
        this.StepText.Text := text
        this.AddHistory(text)
        this.Gate()
    }

    ; Explains what is on screen, and gives time to read it.
    static Say(text) {
        this.Step(text)
        if !this.Unattended
            this.Wait(Max(this.Pace * 2, StrLen(text) * 40))
    }

    ; Sleeps, a little at a time, so a pause or stop takes effect during long waits.
    static Wait(ms) {
        end := A_TickCount + ms
        while A_TickCount < end {
            this.Gate()
            Sleep(Min(50, Max(end - A_TickCount, 1)))
        }
    }

    ; Holds the run while it is paused, at a checkpoint or stuck, and ends it once a stop was asked for.
    static Gate() {
        while this.Waiting != "" && !this.StopRequested
            Sleep(100)
        if this.StopRequested
            throw StopRun("You stopped the run.")
    }

    static PauseOrContinue() {
        if this.Waiting = "" {
            this.Waiting := "pause"
            this.SetState("Paused: press F8 or Continue to carry on", "9A5B00")
            this.MainButton.Text := "Continue  (F8)"
            this.Log("PAUSE", "Paused by you")
        } else if this.Waiting != "stuck" {
            this.Waiting := ""
            this.SetState("Running", "004B87")
            this.MainButton.Text := "Pause  (F8)"
        }
    }

    static RequestStop() {
        if this.StopRequested
            return
        if !this.Unattended && MsgBox("Stop the run? The app is left as it is now, and the results so far are saved.",
            "NetDeploy tests", "YesNo Icon? 0x40000") != "Yes"
            return
        this.StopRequested := true
        this.SetState("Stopping…", "C62828")
    }

    static NoteProblem() {
        answer := InputBox("What looks wrong? It's saved with this case's results, with a screenshot.", "Note a problem", "w460 h150")
        if answer.Result != "OK" || answer.Value = ""
            return
        shot := this.Capture("note")
        if this.Current
            this.Current.Notes.Push(answer.Value " (" shot ")")
        this.Log("NOTE", answer.Value " (" shot ")")
        this.AddHistory("Noted: " answer.Value)
    }

    ; ===== Checkpoints =====

    ; Runs the automatic checks and lists what to look at by hand. In an attended run it then shows them for a few
    ; seconds and carries on by itself: F8 holds the run there to look closer. With StopAtCheckpoints it waits until
    ; the person continues. checks are made with Check.*, tools (buttons that open what to inspect) with Tool.*.
    static Checkpoint(title, checks := [], look := [], tools := []) {
        this.Step("Checkpoint: " title)
        this.CheckTitle.Text := "CHECKPOINT: " StrUpper(title)
        lines := ""
        if checks.Length {
            lines .= "Checked for you:`r`n"
            for check in checks {
                passed := this.Evaluate(check, &detail)
                lines .= (passed ? "  ✓ " : "  ✗ ") check.Label (passed ? "" : "`r`n      " detail) "`r`n"
            }
        }
        if look.Length {
            lines .= (lines ? "`r`n" : "") "Look for yourself:`r`n"
            for item in look
                lines .= "  •  " item "`r`n"
        }
        this.CheckText.Value := lines
        this.ShowTools(tools)
        if this.Unattended
            return

        if !this.StopAtCheckpoints {
            this.SetState("Checkpoint: carrying on by itself. F8 holds it here", "2E7D32")
            this.Wait(Max(this.Pace * 5, 4000))
            this.SetState("Running", "004B87")
            return
        }
        this.Waiting := "checkpoint"
        this.SetState("Your turn: check the list, then press F8 or Continue", "2E7D32")
        this.MainButton.Text := "Continue  (F8)"
        SoundBeep(880, 120)
        this.Gate()
        this.ShowTools([])
    }

    ; Waits for a check to pass, for things that take a moment, such as a service's next heartbeat.
    static Evaluate(check, &detail) {
        end := A_TickCount + (check.HasProp("WithinMs") ? check.WithinMs * this.TimeoutScale : 0)
        Loop {
            test := check.Fn
            try
                detail := test()
            catch Error as e
                detail := "Error: " e.Message
            if detail = "" || A_TickCount >= end
                break
            this.Wait(500)
        }
        if detail = "" {
            this.Log("PASS", check.Label)
            return true
        }
        this.Fail(check.Label ": " detail)
        return false
    }

    ; ===== Failures =====

    ; Records a failure against the running case without stopping it.
    static Fail(text) {
        this.Log("FAIL", text)
        if this.Current
            this.Current.Failures.Push(text)
    }

    ; Something the run needs isn't there. Attended, the person can put it right and retry, skip the case or stop;
    ; unattended, the run ends.
    static Stuck(what, detail := "") {
        shot := this.Capture("stuck")
        message := "I couldn't " what "." (detail ? "`n`n" detail : "")
        this.Log("STUCK", StrReplace(message, "`n", " ") " (" shot ")")
        if this.Unattended {
            this.Fail(StrReplace(message, "`n", " "))
            throw StopRun(message)
        }

        this.Waiting := "stuck"
        this.SetState("Stuck: see the message", "C62828")
        answer := MsgBox(message "`n`nYou can put the app right by hand first. Then:`n`n"
            . "Retry:  look again`nIgnore:  skip the rest of this case`nAbort:  stop the run`n`nScreenshot: " shot,
            "NetDeploy tests: stuck", "AbortRetryIgnore Icon! 0x40000")
        this.Waiting := ""
        this.SetState("Running", "004B87")
        this.MainButton.Text := "Pause  (F8)"
        if answer = "Retry"
            return
        this.Fail(StrReplace(message, "`n", " ") (answer = "Ignore" ? " (skipped the rest of the case)" : " (stopped the run)"))
        throw answer = "Ignore" ? SkipCase(what) : StopRun(what)
    }

    ; Saves a screenshot and the app's UI Automation tree, for working out what went wrong.
    static Capture(name) {
        this.Captures++
        base := this.ResultsDir "\" Format("{:02}", this.Captures) "-" name
        try Machine.Screenshot(base ".png")
        try FileAppend(App.Dump(), base "-uia.txt", "UTF-8")
        return base ".png"
    }

    ; ===== The cases =====

    static RunCases(cases) {
        for index, testCase in cases {
            this.Current := {Name: testCase.Name, Status: "passed", Failures: [], Notes: []}
            this.Results.Push(this.Current)
            this.CaseLabel.Text := "CASE " index " OF " cases.Length
            this.CaseName.Text := testCase.Name
            this.CheckTitle.Text := "CHECKPOINT"
            this.CheckText.Value := ""
            this.Log("CASE", index ". " testCase.Name)
            this.AddHistory("▶ " testCase.Name)
            try {
                run := testCase.Run
                run()
                if this.Current.Failures.Length
                    this.Current.Status := "failed"
            } catch SkipCase {
                this.Current.Status := "skipped"
            } catch StopRun as e {
                this.Current.Status := this.StopRequested ? "stopped" : "failed"
                this.Log("STOP", e.Message)
                break
            } catch Error as e {
                this.Capture("error")
                this.Fail("Script error: " e.Message " (" e.What ", line " e.Line ")")
                this.Current.Status := "failed"
                if this.Unattended || MsgBox("The test script hit an error:`n`n" e.Message "`n`nCarry on with the next case?",
                    "NetDeploy tests", "YesNo Icon! 0x40000") != "Yes"
                    break
            }
            this.Log("RESULT", testCase.Name ": " this.Current.Status)
        }
        return this.Finish()
    }

    ; Writes the summary, shows it, and returns the number of cases that didn't pass.
    static Finish() {
        counts := Map("passed", 0, "failed", 0, "skipped", 0, "stopped", 0)
        text := "NetDeploy manual tests, " FormatTime(, "yyyy-MM-dd HH:mm") "`r`n`r`n"
        for index, result in this.Results {
            counts[result.Status]++
            text .= Format("{:-8}", StrUpper(result.Status)) index ". " result.Name "`r`n"
            for failure in result.Failures
                text .= "          ✗ " failure "`r`n"
            for note in result.Notes
                text .= "          note: " note "`r`n"
        }
        text .= "`r`n" this.Results.Length " cases: " counts["passed"] " passed, " counts["failed"] " failed, "
            . counts["skipped"] " skipped, " counts["stopped"] " stopped`r`n"
        FileAppend(text, this.ResultsDir "\summary.txt", "UTF-8")
        this.Log("DONE", StrReplace(text, "`r`n", " | "))
        failed := this.Results.Length - counts["passed"]
        if !this.Unattended {
            this.SetState(failed ? "Finished with problems" : "Finished: everything passed", failed ? "C62828" : "2E7D32")
            if MsgBox(text "`nOpen the results folder?", "NetDeploy tests: results", "YesNo 0x40000") = "Yes"
                Run('explorer.exe "' this.ResultsDir '"')
        }
        return failed
    }

    static Log(kind, text) {
        line := FormatTime(, "HH:mm:ss") "  " Format("{:-6}", kind) "  " text "`n"
        try FileAppend(line, this.LogPath, "UTF-8")
        if this.Unattended
            try FileAppend(line, "*")
    }
}
