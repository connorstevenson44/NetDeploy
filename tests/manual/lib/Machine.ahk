; What the test packages leave on the machine: services, scheduled tasks, files, settings and databases. Machine
; reads them, Check turns them into checkpoint checks, and Tool opens them for a person to look at.

class Machine {
    static Temp := A_Temp "\NetDeployManualTests"

    ; What NetDeploy remembers for the person using it, and the deploys made on this computer.
    static SettingsFile := EnvGet("LOCALAPPDATA") "\NetDeploy\settings.json"
    static HistoryFolder := EnvGet("ProgramData") "\NetDeploy\history"

    ; Runs a PowerShell script, passing args as $args, and returns what it printed.
    static PowerShell(script, args*) {
        DirCreate(this.Temp)
        file := this.Temp "\query.ps1", output := this.Temp "\query.txt"
        try FileDelete(file)
        try FileDelete(output)
        FileAppend("$ErrorActionPreference = 'Stop'`r`n" script, file, "UTF-8")
        RunWait(A_ComSpec ' /c ""powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "' file '"' QuoteAll(args) ' > "' output '" 2>&1"', , "Hide")
        return FileExist(output) ? Trim(FileRead(output), " `t`r`n") : ""
    }

    ; Runs a program and returns what it printed.
    static Run(program, arguments) {
        DirCreate(this.Temp)
        output := this.Temp "\run.txt"
        try FileDelete(output)
        RunWait(A_ComSpec ' /c ""' program '" ' arguments ' > "' output '" 2>&1"', , "Hide")
        return FileExist(output) ? FileRead(output) : ""
    }

    ; Runs NetDeploy.exe from the command line, as a pipeline would, and returns {Code, Output, First}: its exit code,
    ; what it printed, and the first line of that. Piping its output is what makes PowerShell wait for a windowed exe.
    static NetDeploy(exe, args*) {
        printed := this.PowerShell("
        (
            $exe = $args[0]
            $rest = @($args | Select-Object -Skip 1)
            $output = & $exe @rest | Out-String
            $output.TrimEnd()
            "Exit code: $LASTEXITCODE"
        )", exe, args*)
        code := RegExMatch(printed, "Exit code: (-?\d+)$", &match) ? Integer(match[1]) : -1
        output := RegExReplace(printed, "\R?Exit code: -?\d+$")
        return {Code: code, Output: output, First: Trim(StrSplit(output, "`n")[1], " `r")}
    }

    ; How many deploys to a folder the history holds.
    static HistoryCount(folder) {
        return Integer(this.PowerShell("
        (
            $folder, $history = $args
            @(Get-ChildItem -Path "$history\*.json" -ErrorAction SilentlyContinue |
                ForEach-Object { try { Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json } catch { } } |
                Where-Object { $_.folder -eq $folder }).Count
        )", folder, this.HistoryFolder))
    }

    ; "Running", "Stopped" and so on, or "" when there is no such service.
    static ServiceState(name) {
        for service in ComObjGet("winmgmts:").ExecQuery("SELECT State FROM Win32_Service WHERE Name = '" name "'")
            return service.State
        return ""
    }

    static Services(prefix) {
        names := []
        for service in ComObjGet("winmgmts:").ExecQuery("SELECT Name FROM Win32_Service WHERE Name LIKE '" prefix "%'")
            names.Push(service.Name)
        return names
    }

    ; A task by its full path, such as \Contoso\Orders Export-main.
    static TaskExists(path) {
        SplitPath(path, &name, &folder)
        scheduler := ComObject("Schedule.Service")
        scheduler.Connect()
        try {
            scheduler.GetFolder(folder = "" ? "\" : folder).GetTask(name)
            return true
        }
        return false
    }

    static Tasks(folder) {
        names := []
        scheduler := ComObject("Schedule.Service")
        scheduler.Connect()
        try {
            for task in scheduler.GetFolder(folder).GetTasks(1)
                names.Push(task.Path)
        }
        return names
    }

    ; A JSON setting by its path, such as Orders:Region, or "<absent>".
    static Json(file, path) {
        return this.PowerShell("
        (
            $value = Get-Content -Raw -LiteralPath $args[0] | ConvertFrom-Json
            foreach ($key in ($args[1] -split ':')) { if ($null -eq $value) { break }; $value = $value.$key }
            if ($null -eq $value) { '<absent>' } else { "$value" }
        )", file, path)
    }

    ; An appSettings value from a .config file, or "<absent>".
    static AppSetting(file, key) => this.XmlValue(file, "appSettings", "key", key, "value")

    ; A connection string from a .config file, or "<absent>".
    static ConnectionString(file, name) => this.XmlValue(file, "connectionStrings", "name", name, "connectionString")

    static XmlValue(file, section, keyAttribute, key, valueAttribute) {
        return this.PowerShell("
        (
            $file, $section, $keyAttribute, $key, $valueAttribute = $args
            $xml = [xml](Get-Content -Raw -LiteralPath $file)
            $add = $xml.configuration.$section.add | Where-Object { $_.$keyAttribute -eq $key } | Select-Object -First 1
            if ($null -eq $add) { '<absent>' } else { $add.$valueAttribute }
        )", file, section, keyAttribute, key, valueAttribute)
    }

    ; Starts or stops a service, as someone in Services would: action is "Start" or "Stop".
    static SetService(name, action) => this.PowerShell(action "-Service -Name $args[0]", name)

    ; Copies a folder, as someone copying it in File Explorer would. A file a service has open, such as its log, may
    ; not copy, as in File Explorer.
    static CopyFolder(from, to) {
        this.PowerShell("
        (
            $from, $to = $args
            if (Test-Path -LiteralPath $to) { Remove-Item -LiteralPath $to -Recurse -Force }
            Copy-Item -LiteralPath $from -Destination $to -Recurse -ErrorAction SilentlyContinue
        )", from, to)
    }

    ; Sets an appSettings value in a .config file, as someone editing it on the server would.
    static SetAppSetting(file, key, value) {
        this.PowerShell("
        (
            $file, $key, $value = $args
            $xml = [xml](Get-Content -Raw -LiteralPath $file)
            ($xml.configuration.appSettings.add | Where-Object { $_.key -eq $key }).value = $value
            $xml.Save($file)
        )", file, key, value)
    }

    ; The last line of a file a service may be writing.
    static LastLine(file) {
        if !FileExist(file)
            return ""
        lines := StrSplit(Trim(FileOpen(file, "r").Read(), "`r`n"), "`n", "`r")
        return lines.Length ? lines[lines.Length] : ""
    }

    ; Files, or with mode "D" folders, matching pattern in a folder; "R" in mode looks in its subfolders too.
    static Count(folder, pattern, mode := "F") {
        count := 0
        Loop Files folder "\" pattern, mode
            count++
        return count
    }

    static Unzip(zip, folder) {
        this.PowerShell("
        (
            $zip, $folder = $args
            if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
            Expand-Archive -LiteralPath $zip -DestinationPath $folder
        )", zip, folder)
    }

    static Screenshot(file) {
        this.PowerShell("
        (
            Add-Type -AssemblyName System.Windows.Forms, System.Drawing
            Add-Type -Namespace Win32 -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
            [void][Win32.Dpi]::SetProcessDPIAware()
            $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
            $bitmap = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $graphics.CopyFromScreen($screen.Left, $screen.Top, 0, 0, $bitmap.Size)
            $bitmap.Save($args[0], [System.Drawing.Imaging.ImageFormat]::Png)
        )", file)
    }

    ; What the test packages left behind: services, tasks and folders. Empty on a clean machine.
    static Leftovers() {
        found := []
        for name in this.Services("Contoso.Orders")
            found.Push("service " name)
        for folder in ["\Contoso", "\Fabrikam"]
            for task in this.Tasks(folder)
                found.Push("task " task)
        for folder in [OrdersDir, ReportsDir, OrdersData]
            if DirExist(folder)
                found.Push("folder " folder)
        ; What NetDeploy remembers of the test apps would change what a run sees, such as the settings a package opens with.
        if FileExist(this.SettingsFile) && RegExMatch(FileRead(this.SettingsFile), '"(Contoso\.Orders|Fabrikam\.Reports)"\s*:')
            found.Push("NetDeploy's remembered settings for the test apps")
        Loop Files this.HistoryFolder "\*.json" {
            if RegExMatch(FileRead(A_LoopFileFullPath), "i)Apps\\\\(Contoso\.Orders|Fabrikam\.Reports)") {
                found.Push("the test installs' deploy history")
                break
            }
        }
        return found
    }

    ; Removes everything the test packages install, with tools\Reset-TestMachine.ps1.
    static Reset() {
        RunWait('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' A_ScriptDir '\tools\Reset-TestMachine.ps1"', , "Hide")
    }
}

; Checks for a checkpoint. Each has a Label and a Fn that returns "" when it passes, or what it found instead.
class Check {
    static Make(label, fn, withinMs := 0) {
        check := {Label: label, Fn: fn}
        if withinMs
            check.WithinMs := withinMs
        return check
    }

    ; A service can take a few seconds to start or go, so this waits a little for it.
    static Service(name, state) {
        label := state = "" ? "There is no service called " name : "The service " name " is " StrLower(state)
        return this.Make(label, () => (found := Machine.ServiceState(name)) = state ? "" : "it is " (found = "" ? "not there" : StrLower(found)), 20000)
    }

    static Task(path, exists := true) {
        return this.Make((exists ? "The scheduled task " path " is there" : "There is no scheduled task " path),
            () => Machine.TaskExists(path) = exists ? "" : exists ? "it isn't there" : "it is still there")
    }

    static Exists(path) => this.Make(path " is there", () => FileExist(path) ? "" : "it isn't there")

    static Missing(path) => this.Make(path " isn't there", () => FileExist(path) ? "it is there" : "")

    static Json(file, path, expected) {
        SplitPath(file, &name)
        label := expected = "<absent>" ? name " has no " path " setting" : "In " name ", " path " is " expected
        return this.Make(label, () => (found := Machine.Json(file, path)) = expected ? "" : found = "<absent>" ? "it isn't in the file" : "it is " found)
    }

    static AppSetting(file, key, expected) {
        SplitPath(file, &name)
        label := expected = "<absent>" ? name " has no " key " setting" : "In " name ", " key " is " expected
        return this.Make(label, () => (found := Machine.AppSetting(file, key)) = expected ? "" : found = "<absent>" ? "it isn't in the file" : "it is " found)
    }

    static ConnectionString(file, connection) {
        SplitPath(file, &name)
        return this.Make(name " has a connection string called " connection,
            () => Machine.ConnectionString(file, connection) != "<absent>" ? "" : "it isn't there")
    }

    static FileHas(file, text, label := "") {
        SplitPath(file, &name)
        return this.Make(label ? label : name " has " text " in it",
            () => !FileExist(file) ? "the file isn't there" : InStr(FileRead(file), text) ? "" : "it doesn't")
    }

    ; A line in the log of the deploy that just ran.
    static Logged(log, text) => this.FileHas(log, text, 'The log says "' text '"')

    static Count(folder, pattern, expected, mode := "F") {
        return this.Make(folder " has " expected " " (mode = "D" ? "folder" : pattern " file") (expected = 1 ? "" : "s") " in it",
            () => (found := Machine.Count(folder, pattern, mode)) = expected ? "" : "it has " found)
    }

    static Version(exe, expected) {
        SplitPath(exe, &name)
        return this.Make(name " is version " expected,
            () => !FileExist(exe) ? "it isn't there" : InStr(FileGetVersion(exe), expected) = 1 ? "" : "it is version " FileGetVersion(exe))
    }

    static Output(program, arguments, text) {
        SplitPath(program, &name)
        return this.Make('Running "' name " " arguments '" says ' text,
            () => InStr(output := Machine.Run(program, arguments), text) ? "" : "it printed: " Trim(output, " `r`n"))
    }

    ; The service writes a heartbeat every 10 seconds, so this waits for the next one.
    static Heartbeat(file, text) {
        return this.Make("The service's latest heartbeat says " text,
            () => InStr(line := Machine.LastLine(file), text) ? "" : "the latest one says: " line, 30000)
    }

    static Clean() {
        return this.Make("Nothing from the test packages is left on this computer",
            () => (found := Machine.Leftovers()).Length = 0 ? "" : "still there: " Join(found, ", "))
    }
}

; Buttons on a checkpoint that open what to inspect. Each has a Label and a Fn.
class Tool {
    static Folder(path, label := "") {
        SplitPath(path, &name)
        return {Label: label ? label : "Open " name, Fn: () => Run('explorer.exe "' path '"')}
    }

    static File(path, label := "") {
        SplitPath(path, &name)
        return {Label: label ? label : "Open " name, Fn: () => Run('notepad.exe "' path '"')}
    }

    static App(label, command) => ({Label: label, Fn: () => Run(command)})

    ; A PowerShell window that runs a script, passing args as $args, and stays open.
    static Console(label, script, args*) => ({Label: label, Fn: () => Tool.OpenConsole(label, script, args)})

    static OpenConsole(title, script, args) {
        DirCreate(Machine.Temp)
        file := Machine.Temp "\" RegExReplace(title, "\W+", "-") ".ps1"
        try FileDelete(file)
        FileAppend("$Host.UI.RawUI.WindowTitle = '" title "'`r`n" script, file, "UTF-8")
        Run('powershell.exe -NoExit -NoProfile -ExecutionPolicy Bypass -File "' file '"' QuoteAll(args))
    }

    static Services(prefix) => this.Console("Services", "
    (
        Get-CimInstance Win32_Service -Filter "Name LIKE '$($args[0])%'" |
            Format-Table Name, State, StartMode, DelayedAutoStart, PathName -AutoSize -Wrap
    )", prefix)

    static Tasks(folder) => this.Console("Scheduled tasks", "
    (
        Get-ScheduledTask -TaskPath $args[0] -ErrorAction SilentlyContinue | ForEach-Object {
            $info = $_ | Get-ScheduledTaskInfo
            [pscustomobject]@{ Task = $_.TaskPath + $_.TaskName; State = $_.State; RunAs = $_.Principal.UserId;
                Runs = ($_.Actions | ForEach-Object { $_.Execute + ' ' + $_.Arguments }) -join '; '; Next = $info.NextRunTime }
        } | Format-List
    )", folder "\")

    ; Describes a Contoso.Orders database with the app's own migrate command, which only reports when it is up to date.
    static Database(exe, database) => this.Console("Database", "
    (
        $exe, $database = $args
        Write-Output "Database: $database"
        & $exe migrate --db $database
        Get-Item -LiteralPath $database | Format-List FullName, Length, LastWriteTime
    )", exe, database)
}

; Each argument in double quotes, with a space before each, for a command line.
QuoteAll(args) {
    text := ""
    for arg in args
        text .= ' "' arg '"'
    return text
}

Join(items, separator) {
    text := ""
    for item in items
        text .= (A_Index > 1 ? separator : "") item
    return text
}
