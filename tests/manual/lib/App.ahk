; Drives NetDeploy the way a person would, slowly enough to watch: it finds each control with UI Automation, moves
; the mouse to it, outlines it, then uses it. Controls are found by what a person reads on them (a button's text, a
; field's label, a tooltip), so the steps read like the manual test.

class App {
    static Exe := ""
    static Hwnd := 0
    static Window := ""
    static Marked := ""      ; the status text before the last click, so a wait sees the new one

    ; ===== Starting and stopping =====

    static Start() {
        this.CloseAll()
        Runner.Step("Start NetDeploy")
        Run('"' this.Exe '"', , , &pid)
        this.Hwnd := WinWait("NetDeploy ahk_pid " pid, , 60)
        if !this.Hwnd
            Runner.Stuck("see NetDeploy's window open", this.Exe)
        this.Window := UIA.ElementFromHandle(this.Hwnd)
        this.Place()
    }

    ; Puts the app beside the panel, so the panel never covers it on a wide enough screen.
    static Place() {
        MonitorGetWorkArea(MonitorGetPrimary(), &left, &top, &right, &bottom)
        width := right - left - Runner.PanelWidth
        if width < 1000
            width := right - left
        WinRestore(this.Hwnd)
        WinMove(left, top, width, bottom - top, this.Hwnd)
        WinActivate(this.Hwnd)
    }

    static CloseAll() {
        while WinExist("ahk_exe NetDeploy.exe") {
            if !Runner.Unattended && A_Index = 1
                && MsgBox("NetDeploy is open. Close it so the tests can start it fresh?", "NetDeploy tests", "OKCancel Icon? 0x40000") != "OK"
                throw StopRun("NetDeploy was left open.")
            ProcessClose("NetDeploy.exe")
            ProcessWaitClose("NetDeploy.exe", 10)
        }
    }

    static Close() {
        Runner.Step("Close NetDeploy")
        WinClose(this.Hwnd)
        if !WinWaitClose(this.Hwnd, , 10)
            ProcessClose("NetDeploy.exe")
    }

    static Main() {
        if !WinExist(this.Hwnd)
            Runner.Stuck("find NetDeploy's window", "It has closed. If it crashed, the crash report is in the logs folder next to NetDeploy.exe.")
        return this.Window
    }

    ; ===== Finding controls =====

    ; Waits for a control that is on screen. what names it for the person, as in "the Add button".
    ; Under the main window, another NetDeploy window, such as an error, stops the wait straight away.
    static Find(condition, what, timeoutMs := 20000, root := "") {
        Loop {
            end := A_TickCount + timeoutMs * Runner.TimeoutScale
            while A_TickCount < end {
                Runner.Gate()
                if found := this.TryFind(condition, root)
                    return found
                if !IsObject(root) && (other := this.OtherWindow())
                    Runner.Stuck("find " what, "NetDeploy is showing another window instead: " this.Describe(other))
                Sleep(200)
            }
            Runner.Stuck("find " what)
        }
    }

    static TryFind(condition, root := "") {
        scope := IsObject(root) ? root : this.Main()
        try {
            for element in scope.FindElements(condition)
                if this.IsShown(element)
                    return element
        }
        return ""
    }

    static Exists(condition, root := "") => IsObject(this.TryFind(condition, root))

    static IsShown(element) {
        try {
            box := element.BoundingRectangle
            return box.r > box.l && box.b > box.t
        }
        return false
    }

    ; A control on the same row as another, such as a file's buttons beside its name.
    static FindInRow(anchor, condition, what) {
        row := anchor.BoundingRectangle
        middle := (row.t + row.b) // 2
        Loop {
            end := A_TickCount + 10000 * Runner.TimeoutScale
            while A_TickCount < end {
                Runner.Gate()
                window := this.Main()
                try {
                    for element in window.FindElements(condition) {
                        box := element.BoundingRectangle
                        if box.r > box.l && Abs((box.t + box.b) // 2 - middle) <= 14
                            return element
                    }
                }
                Sleep(200)
            }
            Runner.Stuck("find " what)
        }
    }

    ; Resolves a control given as an element or as a condition.
    static Resolve(target, what, root := "") => target is UIA.IUIAutomationElement ? target : this.Find(target, what, , root)

    ; ===== Using controls =====

    ; Scrolls a control into view, moves the mouse to it and outlines it, so the person sees what is next.
    static Point(element, what := "") {
        try element.ScrollIntoView()
        box := element.BoundingRectangle
        Runner.KeepClear(box)
        x := (box.l + box.r) // 2, y := (box.t + box.b) // 2
        CoordMode("Mouse", "Screen")
        CoordMode("ToolTip", "Screen")
        MouseMove(x, y, Runner.MouseSpeed)
        if !Runner.Unattended {
            try element.Highlight(-(Runner.Pace + 500), "FF8C00", 3)
            if what
                ToolTip(what, x + 24, y + 18)
            Runner.Wait(Runner.Pace)
            ToolTip()
        }
    }

    ; Clicks a control: invokes, toggles or selects it through UI Automation, or with the mouse when it has none of
    ; those, or when how is "mouse".
    static Click(target, what, how := "", root := "") {
        element := this.Resolve(target, what, root)
        Runner.Step(what)
        this.Marked := this.TryStatusText()
        this.Point(element, what)
        while !element.IsEnabled
            Runner.Stuck(StrLower(SubStr(what, 1, 1)) SubStr(what, 2), "It is disabled.")
        if how != "mouse" {
            for pattern in ["Invoke", "Toggle", "SelectionItem"]
                if element.GetPropertyValue(UIA.Property.Is%pattern%PatternAvailable) {
                    switch pattern {
                        case "Invoke": element.InvokePattern.Invoke()
                        case "Toggle": element.TogglePattern.Toggle()
                        default: element.SelectionItemPattern.Select()
                    }
                    Runner.Wait(Runner.Pace // 2)
                    return element
                }
        }
        WinActivate(element.WinId)
        element.Click("left")
        Runner.Wait(Runner.Pace // 2)
        return element
    }

    static ClickButton(name, what := "", root := "") => this.Click({Type: "Button", Name: name}, what ? what : "Click " name, , root)

    ; Moves the mouse over a control, for the buttons that only show on hover.
    static Hover(target, what) {
        element := this.Resolve(target, what)
        Runner.Step(what)
        this.Point(element, what)
        Sleep(300)
        return element
    }

    ; Types into a text box, a key at a time so it can be watched, then moves on with Tab so the value is taken.
    static Type(target, text, what, secret := false, root := "") {
        element := this.Resolve(target, what, root)
        Runner.Step(what)
        this.Point(element, what)
        WinActivate(element.WinId)
        try element.SetFocus()
        Sleep(150)
        SetKeyDelay(Runner.KeyDelay)
        SendEvent("^a")
        SendEvent(text = "" ? "{Delete}" : "{Text}" text)
        if !secret {
            try {
                if element.Value != text {
                    Runner.Log("NOTE", "Typing didn't land in full, so the value was set directly")
                    element.Value := text
                }
            }
        }
        SendEvent("{Tab}")
        Runner.Wait(Runner.Pace // 2)
        return element
    }

    ; Picks an option from a drop-down.
    static Choose(target, option, what, root := "") {
        element := this.Resolve(target, what, root)
        Runner.Step(what)
        this.Point(element, what)
        element.ExpandCollapsePattern.Expand()
        Runner.Wait(Runner.Pace)
        item := this.Find({Type: "ListItem", Name: option}, "the " option " option", 10000, element)
        this.Point(item, option)
        item.SelectionItemPattern.Select()
        try element.ExpandCollapsePattern.Collapse()
        Runner.Wait(Runner.Pace // 2)
    }

    ; Ticks or clears a check box.
    static SetCheck(target, checked, what, root := "") {
        element := this.Resolve(target, what, root)
        Runner.Step(what)
        this.Point(element, what)
        if (element.TogglePattern.CurrentToggleState = 1) != checked
            element.TogglePattern.Toggle()
        Runner.Wait(Runner.Pace // 2)
    }

    static Tab(name) => this.Click({Type: "TabItem", Name: name}, "Open the " name " tab")

    ; ===== Other windows: message boxes, file pickers, step inputs and removal steps =====

    ; Waits for one of NetDeploy's windows by its title. kind is "dialog" for a Windows message box or file picker,
    ; "window" for one of NetDeploy's own. Another titled NetDeploy window showing up instead stops the wait.
    static WaitWindow(title, kind, what, timeoutMs := 30000) {
        Loop {
            end := A_TickCount + timeoutMs * Runner.TimeoutScale
            while A_TickCount < end {
                Runner.Gate()
                if hwnd := this.FindWindow(title, kind)
                    return hwnd
                if (other := this.OtherWindow()) && WinGetTitle(other) != title
                    Runner.Stuck("see " what, "NetDeploy is showing another window instead: " this.Describe(other))
                Sleep(200)
            }
            Runner.Stuck("see " what)
        }
    }

    static FindWindow(title, kind) {
        for hwnd in WinGetList(title " ahk_exe NetDeploy.exe")
            if hwnd != this.Hwnd && (WinGetClass(hwnd) = "#32770") = (kind = "dialog") && WinGetStyle(hwnd) & 0x10000000
                return hwnd
        return 0
    }

    ; Any visible NetDeploy window with a title other than the main one: a message box, a dialog, a step's inputs.
    static OtherWindow() {
        for hwnd in WinGetList("ahk_exe NetDeploy.exe")
            if hwnd != this.Hwnd && WinGetTitle(hwnd) != "" && WinGetStyle(hwnd) & 0x10000000
                return hwnd
        return 0
    }

    static Describe(hwnd) {
        text := RegExReplace(WinGetText(hwnd), "\s+", " ")
        return '"' WinGetTitle(hwnd) '": ' SubStr(text, 1, 400)
    }

    ; Answers a Windows message box with Yes, No or OK. expect is part of the question, checked before answering.
    static Answer(title, button, what, expect := "", timeoutMs := 60000) {
        hwnd := this.WaitWindow(title, "dialog", 'the "' title '" message', timeoutMs)
        text := Trim(RegExReplace(WinGetText(hwnd), "\s*(&Yes|&No|OK|Cancel)\s*", " "))
        Runner.Step(what '  (' title ': "' text '")')
        if expect && !InStr(text, expect)
            Runner.Fail('The "' title '" message was "' text '", expected "' expect '"')
        ; Message box buttons have their command ids as automation ids, whatever the language.
        id := Map("Yes", 6, "No", 7, "OK", 1, "Cancel", 2)[button]
        dialog := UIA.ElementFromHandle(hwnd)
        this.Click(this.Find({Type: "Button", AutomationId: String(id)}, "the " button " button", 5000, dialog), "Click " button, , dialog)
        WinWaitClose(hwnd, , 10)
        return text
    }

    ; Picks a file in a Windows file picker.
    static PickFile(title, path, what) {
        hwnd := this.WaitWindow(title, "dialog", "the " title " picker")
        dialog := UIA.ElementFromHandle(hwnd)
        ; The file name box is control 1148 and the Open button control 1 in every Windows file picker.
        this.Type(this.Find({Type: "Edit", AutomationId: "1148"}, "the file name box", 10000, dialog), path, what, , dialog)
        Loop {
            Loop 3 {
                if open := this.TryFind({Type: "Button", AutomationId: "1"}, dialog)
                    this.Click(open, "Open it", , dialog)
                if WinWaitClose(hwnd, , 3)
                    return
            }
            Runner.Stuck("pick " path " in the " title " picker")
        }
    }

    ; Picks a folder in the Windows folder picker. Its first Select Folder can just go into the typed folder, so it
    ; may take two.
    static PickFolder(title, path, what) => this.PickFile(title, path, what)

    ; A step's inputs window: fill(window) fills it in, then Continue.
    static AnswerInputs(title, what, fill := "") {
        hwnd := this.WaitWindow(title, "window", 'the "' title '" window', 120000)
        window := UIA.ElementFromHandle(hwnd)
        Runner.Step(what)
        if fill
            fill(window)
        this.ClickButton("Continue", "Continue", window)
        WinWaitClose(hwnd, , 10)
    }

    ; Runs every removal step NetDeploy asks about, ticking "I understand" and running each, until it says it is done.
    static RunRemoval(timeoutMs := 300000) {
        end := A_TickCount + timeoutMs * Runner.TimeoutScale
        Loop {
            Runner.Gate()
            status := this.StatusText()
            if status != this.Marked && RegExMatch(status, "^(Removed |Removing .* had errors|Removal stopped)")
                return status
            if hwnd := this.FindWindow("Remove install", "window") {
                window := UIA.ElementFromHandle(hwnd)
                counter := this.Find({AutomationId: "Counter"}, "the step counter", 10000, window).Name
                stepTitle := this.Find({AutomationId: "StepTitle"}, "the step title", 10000, window).Name
                Runner.Say(counter ": " stepTitle "`n" this.Find({AutomationId: "Explanation"}, "the explanation", 10000, window).Name)
                this.Click(this.Find({Type: "CheckBox"}, "the I understand box", 10000, window), "Tick 'I understand'", , window)
                this.Click(this.Find({AutomationId: "RunButton"}, "the Run this step button", 10000, window), "Run this step", , window)
                WinWaitClose(hwnd, , 60)
                continue
            }
            if other := this.OtherWindow()
                Runner.Stuck("finish removing the install", "NetDeploy is showing another window: " this.Describe(other))
            if A_TickCount > end
                Runner.Stuck("see the removal finish", "The status still says: " status)
            Sleep(300)
        }
    }

    ; ===== The workspace =====

    static StatusText() => this.Main().FindElement({AutomationId: "StatusText"}).Name

    static TryStatusText() {
        try return this.Window.FindElement({AutomationId: "StatusText"}).Name
        return ""
    }

    ; Waits for the status bar to start with one of prefixes, after the last click changed it. A message box that
    ; shows up instead, such as a failure's roll back offer, stops the wait.
    static WaitStatus(prefixes, timeoutMs := 180000) {
        Loop {
            end := A_TickCount + timeoutMs * Runner.TimeoutScale
            while A_TickCount < end {
                Runner.Gate()
                status := this.StatusText()
                if status != this.Marked
                    for prefix in prefixes
                        if InStr(status, prefix) = 1 {
                            Runner.Log("STATUS", status)
                            return status
                        }
                if other := this.OtherWindow()
                    Runner.Stuck('see "' prefixes[1] '" in the status bar', "NetDeploy is showing another window: " this.Describe(other))
                Sleep(300)
            }
            Runner.Stuck('see "' prefixes[1] '" in the status bar', "It says: " status)
        }
    }

    ; The log file named at the end of a deploy's status.
    static LogFile(status) => RegExMatch(status, "Log: (.+)$", &match) ? match[1] : ""

    ; Waits for the header to name what is open, such as "Contoso.Orders 1.0.0".
    static WaitHeader(title) {
        this.Find({Type: "Text", Name: title "    ", mm: "StartsWith"}, '"' title '" in the header', 30000)
        Runner.Log("HEADER", title)
    }

    static OpenPackage(path) {
        SplitPath(path, &name)
        this.ClickButton("Open package")
        this.PickFile("Open a deploy package", path, "Pick " name)
    }

    ; ===== The install folder on the left =====

    ; Types the name of a new install in the last row of the install folder.
    static NewInstallName(name) => this.Type({Type: "Edit", Name: "New install name"}, name, "Name the new install " name)

    ; A folder's row in the folder list, by its full path, which is the row's tooltip.
    static FolderRow(path) {
        SplitPath(path, &name)
        return this.Find({Type: "Button", HelpText: path}, "the " name " folder on the left")
    }

    static GoInto(path) {
        SplitPath(path, &name)
        this.Click(this.FolderRow(path), "Go into " name)
    }

    static GoUp() => this.Click({Type: "Button", HelpText: "Up one folder"}, "Go up a folder")

    static GoToRoot() {
        while (up := this.TryFind({Type: "Button", HelpText: "Up one folder"})) && up.IsEnabled
            this.GoUp()
    }

    ; Clone or remove, from the buttons that show when the mouse is over an install's row.
    static InstallButton(path, tooltip, what) {
        SplitPath(path, &name)
        row := this.FolderRow(path)
        this.Hover(row, "Point at " name " to show its buttons")
        this.Click(this.Find({Type: "Button", HelpText: tooltip}, "the button to " StrLower(what), 10000, row), what)
    }

    static RemoveInstall(path) {
        SplitPath(path, &name)
        this.InstallButton(path, "Remove this install, confirming each step", "Remove " name)
        this.Answer("Remove install", "Yes", "Yes, remove " name, "Remove " name)
        return this.RunRemoval()
    }

    ; ===== The Files tab =====

    ; A file's name in the Files tab or the files review, by its path from the folder, which is the name's tooltip.
    ; The lists only build the rows in view, so it scrolls them until the file shows.
    static FileName(path) {
        condition := {Type: "Text", HelpText: path}
        end := A_TickCount + 20000 * Runner.TimeoutScale
        while A_TickCount < end {
            Runner.Gate()
            if found := this.ScrollTo(condition)
                return found
            Sleep(500)
        }
        return this.Find(condition, path " in the file list")
    }

    ; Scrolls each list in the window with the mouse wheel, up to its top and then down to its end, until something
    ; that matches condition is built.
    static ScrollTo(condition) {
        if found := this.TryFind(condition)
            return found
        CoordMode("Mouse", "Screen")
        for list in this.Main().FindElements({Type: "List"}) {
            try box := list.BoundingRectangle
            catch
                continue
            if box.b - box.t < 120
                continue
            spot := {l: box.l + 40, t: (box.t + box.b) // 2 - 20, r: box.l + 80, b: (box.t + box.b) // 2 + 20}
            Runner.KeepClear(spot)
            MouseMove(spot.l + 20, spot.t + 20, Runner.MouseSpeed)
            for wheel in ["WheelUp", "WheelDown"] {
                last := ""
                Loop 100 {
                    if found := this.TryFind(condition, list)
                        return found
                    if (shown := this.RowNames(list)) = last
                        break
                    last := shown
                    Send("{" wheel " 3}")
                    Sleep(150)
                }
            }
        }
        return ""
    }

    ; The names on a list's rows that are built now, to tell when scrolling it stops changing anything.
    static RowNames(list) {
        names := ""
        try {
            for text in list.FindElements({Type: "Text", AutomationId: "Name"})
                names .= text.Name "|"
        }
        return names
    }

    ; One of the buttons on a file's row. In the Files tab they show when the mouse is over the row.
    static FileButton(path, tooltip, what) {
        name := this.Hover(this.FileName(path), "Point at " path)
        this.Click(this.FindInRow(name, {Type: "Button", HelpText: tooltip}, "the button to " StrLower(what)), what)
    }

    static EditSettings(path) => this.FileButton(path, "Edit settings", "Edit the settings in " path)

    ; ===== The deploy's reviews =====

    static WaitStage(title) {
        this.Find({Type: "Text", Name: title}, '"' title '" on the right', 120000)
        Runner.Log("STAGE", title)
    }

    ; Continue, Continue the dry run, or Write files and continue.
    static ContinueStage() {
        button := this.Find({AutomationId: "ContinueButton"}, "the Continue button")
        this.Click(button, "Click " button.Name)
    }

    ; Shows one config file's diff in the config review, by clicking its chip.
    static ConfigFile(name) => this.Click({Type: "Text", Name: name}, "Show " name, "mouse")

    ; Types a setting's value on the right of a diff. path is the setting's path, such as Orders:Region.
    static SetSetting(path, value, what) => this.Type({Type: "Edit", Name: path}, value, what)

    ; ===== Diagnostics =====

    ; NetDeploy's windows as UI Automation sees them, for working out why a control wasn't found.
    static Dump() {
        text := ""
        for hwnd in WinGetList("ahk_exe NetDeploy.exe") {
            if !(WinGetStyle(hwnd) & 0x10000000)
                continue
            text .= "===== " WinGetTitle(hwnd) " (" WinGetClass(hwnd) ")`r`n"
            try text .= UIA.ElementFromHandle(hwnd).DumpAll() "`r`n"
        }
        return text
    }
}
