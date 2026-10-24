; The run itself: the panel beside the app, the pace, pausing, going step by step, checks and the results.

; Thrown to give up on the rest of a case, after the person chose to skip it.
class SkipCase extends Error {
}

; Thrown to end the whole run: the person stopped it, or an unattended run got stuck.
class StopRun extends Error {
}

class Runner {
    static Unattended := false
    static StopAtCheckpoints := false  ; true waits at each check until the person carries on
    static StepByStep := false   ; true waits before each step until the person presses Enter
    static Pace := 1100          ; ms spent on each action, so it can be followed
    static MouseSpeed := 15      ; 0 is instant, 100 the slowest
    static KeyDelay := 60        ; ms between typed keys
    static TimeoutScale := 1     ; waits are longer on a slow CI machine
    static Waiting := ""         ; "", "pause", "step", "checkpoint" or "stuck"
    static StopRequested := false
    static Asking := false       ; one of the runner's own questions is showing
    static YouClicked := false   ; the person clicked outside the panel since the run last carried on
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
        if unattended
            return
        ; The keys only act while nobody is typing, so they still type as usual in a text box the person clicked
        ; into, or in the note and the runner's own questions. The keys the runner sends never trigger them.
        HotIf((*) => Runner.KeysAreMine())
        Hotkey("$Space", (*) => Runner.PauseOrCarryOn())
        Hotkey("$Enter", (*) => Runner.NextStep())
        Hotkey("$Escape", (*) => Runner.RequestStop())
        HotIf()
        Hotkey("~*LButton", (*) => Runner.NoticeClick())
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
        this.CaseLabel := ui.Add("Text", "w" inner, "GETTING READY")
        ui.SetFont("s12 c1A2633 bold")
        this.CaseName := ui.Add("Text", "w" inner " y+2", "")

        ui.SetFont("s8 c5B6B7C bold")
        this.NowLabel := ui.Add("Text", "w" inner " y+14", "NOW")
        ui.SetFont("s11 c1A2633 norm")
        this.StepText := ui.Add("Text", "w" inner " r4 y+3", "")
        ui.SetFont("s10 cWhite bold")
        this.StateText := ui.Add("Text", "w" inner " h30 y+6 Center 0x200 Background004B87", "Running")

        ui.SetFont("s8 c5B6B7C bold")
        this.CheckTitle := ui.Add("Text", "w" inner " y+14", "CHECK")
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
        ui.Add("Text", "xm w" inner " y+14", "DONE SO FAR")
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
        this.MainButton := ui.Add("Button", "xm w192 h36 y+10", "Pause  (Space)")
        this.MainButton.OnEvent("Click", (*) => Runner.PauseOrCarryOn())
        this.NextButton := ui.Add("Button", "x+6 yp w192 h36", "Next step  (Enter)")
        this.NextButton.OnEvent("Click", (*) => Runner.NextStep())
        ui.SetFont("s9 norm")
        this.NoteButton := ui.Add("Button", "xm w192 h30 y+6", "Note a problem…")
        this.NoteButton.OnEvent("Click", (*) => Runner.NoteProblem())
        this.StopButton := ui.Add("Button", "x+6 yp w192 h30", "Stop the test  (Esc)")
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
        this.ShowState()
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

    ; Says on the panel whether the run is going or waiting, and what the keys do now.
    static ShowState() {
        if this.Unattended
            return this.SetState("Running by itself, unattended", "004B87")
        switch this.Waiting {
            case "pause": this.SetState("Paused", "9A5B00")
            case "step": this.SetState("Waiting for you: Enter does this step", "6A3D9A")
            case "checkpoint": this.SetState("Your turn: look it over, then carry on", "2E7D32")
            case "stuck": this.SetState("Stuck: see the message", "C62828")
            default: this.SetState(this.StepByStep ? "Doing this step" : "Running by itself", "004B87")
        }
        this.NowLabel.Text := this.Waiting = "step" ? "UP NEXT" : "NOW"
        this.MainButton.Text := this.Waiting = "" ? "Pause  (Space)" : this.StepByStep ? "Run by itself  (Space)" : "Carry on  (Space)"
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

    ; Asks the person something, with a button for each answer, and returns the one they clicked. Esc or closing the
    ; window gives the last answer.
    static Ask(title, message, answers) {
        this.Asking := true
        ui := Gui("+AlwaysOnTop -MinimizeBox", title)
        ui.BackColor := "FFFFFF"
        ui.MarginX := 20, ui.MarginY := 16
        ui.SetFont("s10 c1A2633", "Segoe UI")
        ui.Add("Text", "w480", message)
        picked := {Answer: ""}
        for index, answer in answers {
            button := ui.Add("Button", (index = 1 ? "xm y+18 h32 Default" : "x+8 yp h32"), "   " answer "   ")
            button.OnEvent("Click", ((text, *) => picked.Answer := text).Bind(answer))
        }
        last := answers[answers.Length]
        ui.OnEvent("Close", (*) => picked.Answer := last)
        ui.OnEvent("Escape", (*) => picked.Answer := last)
        ui.Show()
        while picked.Answer = ""
            Sleep(50)
        ui.Destroy()
        this.Asking := false
        return picked.Answer
    }

    ; ===== Pace, pausing, stepping and stopping =====

    ; Shows what is about to happen. Step by step, it then waits for Enter; paused, it waits until the run carries on.
    static Step(text) {
        this.Show(text)
        if this.StepByStep && !this.Unattended {
            this.Waiting := "step"
            this.ShowState()
        }
        this.Gate()
    }

    static Show(text) {
        this.Log("STEP", text)
        this.StepText.Text := text
        this.AddHistory(text)
    }

    ; Explains what is on screen, and gives time to read it. Step by step, the wait for Enter is the time to read it.
    static Say(text) {
        this.Step(text)
        if !this.Unattended && !this.StepByStep
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

    ; Holds the run while it is paused, waiting for a step, at a check or stuck, and ends it once a stop was asked for.
    static Gate() {
        while this.Waiting != "" && !this.StopRequested
            Sleep(100)
        if this.StopRequested
            throw StopRun("You stopped the test.")
    }

    ; Space: pauses a run that is going. On a run that is waiting, carries on by itself, no longer step by step.
    static PauseOrCarryOn() {
        if this.Waiting = "stuck" || this.StopRequested
            return
        if this.Waiting = "" {
            this.Waiting := "pause"
            this.Log("PAUSE", "Paused by you")
        } else
            this.StepByStep := false, this.CarryOn()
        this.ShowState()
    }

    ; Enter: does the next step, then waits for Enter again. On a run that is going, it waits before the next step.
    static NextStep() {
        if this.Waiting = "stuck" || this.StopRequested
            return
        this.StepByStep := true
        if this.Waiting != ""
            this.CarryOn()
        this.ShowState()
    }

    static CarryOn() {
        this.Waiting := ""
        this.YouClicked := false
    }

    static RequestStop() {
        if this.StopRequested || this.Asking
            return
        if !this.Unattended && this.Ask("Stop the test?", "Stop the test here? NetDeploy is left as it is now, and the results so far are saved.",
            ["Stop the test", "Keep going"]) != "Stop the test"
            return
        this.StopRequested := true
        this.SetState("Stopping…", "C62828")
    }

    ; Space, Enter and Esc belong to the runner unless the person is typing: in a text box they clicked into
    ; themselves, or in one of the runner's own windows, such as the note. While the runner is stuck they do nothing.
    static KeysAreMine() {
        if WinActive(this.Ui)
            return true
        if this.Waiting = "stuck" || this.Asking || WinActive("ahk_pid " ProcessExist())
            return false
        return !(this.YouClicked && this.IsTyping())
    }

    static NoticeClick() {
        MouseGetPos(, , &window)
        if window != this.Ui.Hwnd
            this.YouClicked := true
    }

    ; Whether the keyboard is in a text box that can be typed in, in any app.
    static IsTyping() {
        try {
            focused := UIA.GetFocusedElement()
            return (focused.Type = UIA.Type.Edit || focused.Type = UIA.Type.Document) && !focused.ValueIsReadOnly
        }
        return false
    }

    static NoteProblem() {
        this.Asking := true
        answer := InputBox("What looks wrong? It's saved with this case's results, with a screenshot of the screen now.", "Note a problem", "w460 h150")
        this.Asking := false
        if answer.Result != "OK" || answer.Value = ""
            return
        shot := this.Capture("note")
        if this.Current
            this.Current.Notes.Push(answer.Value " (" shot ")")
        this.Log("NOTE", answer.Value " (" shot ")")
        this.AddHistory("Noted: " answer.Value)
    }

    ; ===== Checks =====

    ; Checks what it can by itself, and lists what to look at by hand. In an attended run it then shows them for a few
    ; seconds and carries on by itself: Space holds the run there to look closer. With StopAtCheckpoints, or step by
    ; step, it waits until the person carries on. checks are made with Check.*, tools (buttons that open what to look
    ; at) with Tool.*.
    static Checkpoint(title, checks := [], look := [], tools := []) {
        this.Show("Check: " title)
        this.Gate()
        this.CheckTitle.Text := "CHECK: " StrUpper(title)
        lines := ""
        if checks.Length {
            lines .= "Checked for you:`r`n"
            for check in checks {
                passed := this.Evaluate(check, &detail)
                lines .= (passed ? "  ✓ " : "  ✗ ") check.Label (passed ? "" : "`r`n      " detail) "`r`n"
            }
        }
        if look.Length {
            lines .= (lines ? "`r`n" : "") "Have a look yourself:`r`n"
            for item in look
                lines .= "  •  " item "`r`n"
        }
        this.CheckText.Value := lines
        this.ShowTools(tools)
        if this.Unattended
            return

        if this.StopAtCheckpoints || this.StepByStep {
            this.Waiting := "checkpoint"
            this.ShowState()
            SoundBeep(880, 120)
            this.Gate()
            return
        }
        this.SetState("Carrying on in a few seconds. Space stays here", "2E7D32")
        this.Wait(Max(this.Pace * 5, 4000))
        this.ShowState()
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

    ; Something the run needs isn't there. Attended, the person can put it right and try again, skip the case or
    ; stop; unattended, the run ends.
    static Stuck(what, detail := "") {
        shot := this.Capture("stuck")
        message := "I couldn't " what "." (detail ? "`n`n" detail : "")
        this.Log("STUCK", StrReplace(message, "`n", " ") " (" shot ")")
        if this.Unattended {
            this.Fail(StrReplace(message, "`n", " "))
            throw StopRun(message)
        }

        this.Waiting := "stuck"
        this.ShowState()
        answer := this.Ask("NetDeploy tests: stuck", message "`n`nYou can put NetDeploy right by hand first, then try again. "
            . "A screenshot of the screen now is saved with the results:`n" shot,
            ["Try again", "Skip the rest of this case", "Stop the test"])
        this.CarryOn()
        this.ShowState()
        if answer = "Try again"
            return
        skip := answer = "Skip the rest of this case"
        this.Fail(StrReplace(message, "`n", " ") (skip ? " (skipped the rest of the case)" : " (stopped the test)"))
        throw skip ? SkipCase(what) : StopRun(what)
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
            this.CheckTitle.Text := "CHECK"
            this.CheckText.Value := ""
            this.ShowTools([])
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
                if this.Unattended || this.Ask("NetDeploy tests: script error", "Something went wrong in the test script itself:`n`n" e.Message
                    . "`n`nIt's saved with the results.", ["Go on to the next case", "Stop the test"]) != "Go on to the next case"
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
            this.CarryOn()
            this.SetState(failed ? "Finished, with problems" : "Finished: everything passed", failed ? "C62828" : "2E7D32")
            this.Asking := true
            if MsgBox(text "`nOpen the results folder?", "NetDeploy tests: results", "YesNo 0x40000") = "Yes"
                Run('explorer.exe "' this.ResultsDir '"')
            this.Asking := false
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
