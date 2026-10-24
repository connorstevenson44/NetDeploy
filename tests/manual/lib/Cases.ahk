; The manual test, as cases that run in order. Each case starts where the one before it left off, so a case picked
; on its own needs what the earlier ones made (its Needs). Every case ends at a checkpoint: the checks the runner can
; make itself, then what to look at by hand, with buttons that open it.

global OrdersDir := "C:\Apps\Contoso.Orders"
global OrdersData := "C:\ProgramData\Contoso.Orders"
global ReportsDir := "C:\Apps\Fabrikam.Reports"
global ApiKey := "pk-test-123"

class Cases {
    static All := [
        {Name: "Open a package", Run: OpenPackage, Needs: "A clean machine",
            Covers: "The start screen, Open package and its file picker, the package's settings, the Steps and deploy.json tabs"},
        {Name: "Dry run a new install", Run: DryRun, Needs: "Case 1",
            Covers: "Naming a new install, Dry run, the files and config reviews in a dry run, nothing written"},
        {Name: "Add the first install (main)", Run: AddMain, Needs: "Case 2",
            Covers: "Add: service, scheduled task, SQLite database, settings script, if/else on Staging, the service's dot in the folder list"},
        {Name: "Add a second install (blue) for Production", Run: AddBlue, Needs: "Case 3",
            Covers: "A choice parameter, a second install beside the first, the Production branch"},
        {Name: "Change settings on the server in the Files tab", Run: EditOnServer, Needs: "Case 3",
            Covers: "Going into an install, folding a folder and filtering the files, editing appsettings.json with tracked changes and undoing one, an .exe.config, staging and unstaging a delete, Finalize"},
        {Name: "Cancel an upgrade at the config check, then roll back", Run: CancelAndRollBack, Needs: "Case 4",
            Covers: "A secret parameter, the files review of an upgrade, a when question, Cancel during a deploy, Roll back"},
        {Name: "Upgrade main to 1.1.0", Run: UpgradeMain, Needs: "Case 5",
            Covers: "Changing a file's action, the three-way merge, a renamed setting, an override check, a setting that needs a value"},
        {Name: "Rename blue to green", Run: RenameBlue, Needs: "Case 6",
            Covers: "Install name, Rename, removing the old install step by step"},
        {Name: "Clone main as qa", Run: CloneMain, Needs: "Case 7",
            Covers: "Clone from the hover buttons, Start from, a new install that starts from a copy"},
        {Name: "Edit a deploy.json in the editor", Run: EditConfig, Needs: "A Contoso.Orders package",
            Covers: "Open config, the step list, Form and JSON views, a problem as you type, Save opening the folder as the package"},
        {Name: "Manage the install folder without a package", Run: ManageFolder, Needs: "Case 9",
            Covers: "Open folder and its folder picker, versions in the folder list, running an exe with arguments, deleting a file, Up"},
        {Name: "Add Fabrikam.Reports 2.0.0", Run: AddReports, Needs: "A clean Fabrikam.Reports",
            Covers: "A .NET Framework app, a question during the deploy, a step's inputs window"},
        {Name: "Upgrade Fabrikam.Reports to 2.1.0", Run: UpgradeReports, Needs: "Case 12",
            Covers: "Edits made by hand on the server, a yes/no parameter, .exe.config and web.config merges, plugins swapped"},
        {Name: "Remove every install", Run: RemoveAll, Needs: "Cases 9 and 13",
            Covers: "Remove from the hover buttons, each removal step confirmed, a clean machine at the end"},
    ]
}

Package(name) => Settings.Packages "\" name ".zip"

; ===== 1 =====

OpenPackage() {
    App.Start()
    App.Find({Type: "Text", Name: "Drop a package, folder or deploy.json here"}, "the start screen")
    Runner.Say("NetDeploy starts empty, with the ways to open a package, an install folder or a config.")
    App.OpenPackage(Package("Contoso.Orders-1.0.0"))
    App.WaitHeader("Contoso.Orders 1.0.0")
    Runner.Say("The package is open. On the left are its settings, then the install folder it goes into, which isn't there yet.")
    App.Tab("Steps")
    Runner.Say("The Steps tab lists every step the package runs.")
    App.Tab("deploy.json")
    Runner.Say("The deploy.json tab shows the package's config in the editor.")
    App.Tab("Files")
    Runner.Checkpoint("The package is open", [
        Check.Missing(OrdersDir),
    ], [
        "The header says Contoso.Orders 1.0.0, with the zip's path beside it.",
        "PACKAGE shows Database folder and Environment (Staging), then Dry run and Add.",
        "INSTALL FOLDER shows C:\Apps\Contoso.Orders, marked not created yet, and a New install row at the bottom.",
        "Under the folder list: Type a name for the new install.",
        "The Steps tab listed 11 steps, with the Then and Else of Lock down production indented.",
    ])
}

; ===== 2 =====

DryRun() {
    App.NewInstallName("main")
    App.Find({Type: "Button", Name: "Add"}, "the Add button")
    Runner.Say("The deploy button now says Add: this deploy adds a new install called main.")
    App.ClickButton("Dry run")
    App.WaitStage("Check the files")
    App.Find({Type: "Text", Name: "Dry run: nothing will change"}, "the dry run badge")
    Runner.Say("A dry run shows the same reviews as a deploy. Every file is new here, so every one would be added.")
    App.ContinueStage()
    App.WaitStage("Check the config")
    for file in ["appsettings.json", "appsettings.Production.json", "Contoso.Orders.Export.exe.config"]
        App.ConfigFile(file)
    Runner.Say("New config files are shown as they would be written. In a dry run they can't be changed.")
    App.ContinueStage()
    status := App.WaitStatus(["Dry run finished."])
    Runner.Checkpoint("The dry run changed nothing", [
        Check.Missing(OrdersDir),
        Check.Missing(OrdersData),
        Check.Service("Contoso.Orders-main", ""),
        Check.Exists(App.LogFile(status)),
    ], [
        "The Log tab shows each step and what it would do.",
        "The Steps tab shows every step as done or skipped.",
        "The status bar says Dry run finished. Nothing was changed.",
    ], [
        Tool.File(App.LogFile(status), "Open the log"),
    ])
}

; ===== 3 =====

AddMain() {
    main := OrdersDir "\main"
    App.ClickButton("Add", "Click Add to deploy main")
    App.WaitStage("Check the files")
    App.ContinueStage()
    App.WaitStage("Check the config")
    Runner.Say("This time the text could be edited before it is written.")
    App.ContinueStage()
    log := App.LogFile(App.WaitStatus(["Deploy finished."], 300000))
    App.GoToRoot()
    App.Hover({Type: "Text", Name: "Contoso.Orders-main: running", mm: 2}, "The green dot beside main says its service is running")
    Runner.Checkpoint("main is installed and running", [
        Check.Service("Contoso.Orders-main", "Running"),
        Check.Task("\Contoso\Orders Export-main"),
        Check.Exists(OrdersData "\db-main\orders.db"),
        Check.Json(main "\appsettings.json", "ConnectionStrings:Orders", "Data Source=" OrdersData "\db-main\orders.db"),
        Check.Logged(log, "Applied migration 1."),
        Check.Logged(log, "Seeded 3 customers and 4 orders."),
        Check.Logged(log, "Not production (Staging)"),
        Check.Logged(log, "Check passed."),
    ], [
        "The folder list shows main with v1.0.0 and a green dot, whose tooltip says its service is running.",
        "Services: Contoso Orders main is running, starting Automatic (Delayed Start).",
        "Task Scheduler: \Contoso\Orders Export-main runs daily at 02:00 as SYSTEM.",
        "The database is in C:\ProgramData\Contoso.Orders\db-main, outside the install folder, at schema 1 with 4 orders.",
    ], [
        Tool.Folder(main, "Open main"),
        Tool.Folder(OrdersData, "Database folder"),
        Tool.Database(main "\Contoso.Orders.exe", OrdersData "\db-main\orders.db"),
        Tool.Services("Contoso.Orders"),
        Tool.Tasks("\Contoso"),
        Tool.File(main "\appsettings.json"),
    ])
}

; ===== 4 =====

AddBlue() {
    blue := OrdersDir "\blue"
    App.NewInstallName("blue")
    App.Choose({Type: "ComboBox", Name: "Environment"}, "Production", "Set Environment to Production")
    App.ClickButton("Add", "Click Add to deploy blue")
    App.WaitStage("Check the files")
    App.ContinueStage()
    App.WaitStage("Check the config")
    App.ContinueStage()
    log := App.LogFile(App.WaitStatus(["Deploy finished."], 300000))
    Runner.Checkpoint("blue runs beside main, set up for Production", [
        Check.Service("Contoso.Orders-blue", "Running"),
        Check.Service("Contoso.Orders-main", "Running"),
        Check.Task("\Contoso\Orders Export-blue"),
        Check.Exists(OrdersData "\db-blue\orders.db"),
        Check.Exists(blue "\PRODUCTION.txt"),
        Check.Logged(log, "Production: would restrict"),
    ], [
        "The folder list shows blue and main, both v1.0.0.",
        "Services: Contoso Orders blue and Contoso Orders main, both running.",
        "blue has its own database folder, db-blue.",
    ], [
        Tool.Folder(blue, "Open blue"),
        Tool.Folder(OrdersData, "Database folder"),
        Tool.Services("Contoso.Orders"),
        Tool.Tasks("\Contoso"),
    ])
}

; ===== 5 =====

EditOnServer() {
    main := OrdersDir "\main"
    alerts := '"Alerts": { "Emails": [ "ops@contoso.local", "oncall@contoso.local" ] },'
    App.GoToRoot()
    App.GoInto(main)
    Runner.Say("Going into main makes it the deploy's target, so the button now says Update, and lists its files on the right.")
    App.Tab("Files")
    App.ToggleFolder("templates", "Fold the templates folder, as in File Explorer")
    App.ToggleFolder("templates", "And open it again")
    App.EditFile("appsettings.json")
    App.ReplaceText('("Region":\s*)"east"', '$1"west"', "Change Orders:Region to west, as someone might on the server")
    Runner.Say("The editor shows the change in place: east struck through in red, west in green.")
    App.ReplaceText('("HeartbeatSeconds":\s*)10', '${1}20', "Change HeartbeatSeconds to 20")
    App.ClickMark("Undo", "Put back the server's text: 10", "And undo just that change, with the button at the end of its line")
    App.ReplaceText("^\s*\{", "{`r`n  " alerts, "Add an Alerts section with a list of emails at the top of the file")
    Runner.Say("The new line is green. Region's change is still there, and HeartbeatSeconds is back to 10.")
    App.FilterFiles("config", "Filter the files to the ones with config in their path")
    App.EditFile("export/Contoso.Orders.Export.exe.config")
    App.ReplaceText('(key="BatchSize"\s+value=)"500"', '$1"750"', "Change the export's BatchSize to 750")
    App.FilterFiles("", "Clear the filter")
    App.Find({Type: "Text", Name: "3 changes"}, "the bar saying 3 changes")
    App.FileButton("README.txt", "Delete when finalized", "Stage README.txt to be deleted")
    App.Find({Type: "Text", Name: "3 changes, 1 file to delete"}, "the bar saying a file will be deleted")
    App.FileButton("README.txt", "Keep this file", "Change your mind and keep README.txt")
    App.ClickButton("Finalize")
    App.Answer("Finalize", "Yes", "Write the changes", "Write 2 changes to main/appsettings.json")
    App.WaitStatus(["Changes written."])
    Runner.Checkpoint("The server's settings changed", [
        Check.Json(main "\appsettings.json", "Orders:Region", "west"),
        Check.Json(main "\appsettings.json", "Orders:HeartbeatSeconds", "10"),
        Check.Json(main "\appsettings.json", "Alerts:Emails", "ops@contoso.local oncall@contoso.local"),
        Check.AppSetting(main "\export\Contoso.Orders.Export.exe.config", "BatchSize", "750"),
        Check.Exists(main "\README.txt"),
    ], [
        "The Finalize message listed exactly the two settings files.",
        "README.txt is still in the list, not struck through.",
        "appsettings.json has the Alerts section as it was typed, Region west, and the rest of its layout as it was.",
    ], [
        Tool.File(main "\appsettings.json"),
        Tool.File(main "\export\Contoso.Orders.Export.exe.config", "Open the export's config"),
    ])
}

; ===== 6 =====

CancelAndRollBack() {
    blue := OrdersDir "\blue"
    App.OpenPackage(Package("Contoso.Orders-1.1.0"))
    App.WaitHeader("Contoso.Orders 1.1.0")
    App.Type({Type: "Edit", Name: "Payments API key (new in 1.1)"}, ApiKey, "Type the payments API key, a secret parameter new in 1.1", true)
    App.GoToRoot()
    App.GoInto(blue)
    App.Find({Type: "Button", Name: "Update"}, "the Update button")
    Runner.Say("blue is on 1.0.0, so the button says Update.")
    App.ClickButton("Update", "Click Update to upgrade blue")
    App.WaitStage("Check the files")
    App.Hover(App.FileName("templates/password-reset.html"), "1.1.0 drops password-reset.html, so it is deleted (struck through, in red)")
    App.Hover(App.FileName("templates/return-received.html"), "and adds return-received.html")
    App.ContinueStage()
    App.Answer("NetDeploy", "No", "Skip the optional settings check of the old version", "Run a settings check on the old version first?")
    App.WaitStage("Check the config")
    Runner.Say("Cancel here, at the config check. The service is already stopped and a backup taken, so NetDeploy offers to roll back.")
    App.ClickButton("Cancel", "Cancel the deploy")
    App.Answer("Roll back", "Yes", "Restore the backup and restart the service", "Restore the backup")
    App.WaitStatus(["Rolled back to the backup."], 120000)
    Runner.Checkpoint("blue is back as it was", [
        Check.Service("Contoso.Orders-blue", "Running"),
        Check.Version(blue "\Contoso.Orders.exe", "1.0.0"),
        Check.Exists(blue "\templates\password-reset.html"),
        Check.Missing(blue "\templates\return-received.html"),
    ], [
        "The folder list still shows blue with v1.0.0.",
        "The Log tab shows the cancel, then the backup restored and the service started.",
        "blue.backups in the install folder holds the backup.",
    ], [
        Tool.Folder(blue, "Open blue"),
        Tool.Folder(OrdersDir, "Install folder"),
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== 7 =====

UpgradeMain() {
    main := OrdersDir "\main"
    App.GoToRoot()
    App.GoInto(main)
    App.ClickButton("Update", "Click Update to upgrade main")
    App.WaitStage("Check the files")
    changed := "templates/order-confirmation.html"
    App.FileButton(changed, "Keep the server's file as it is", "Keep the server's copy of order-confirmation.html instead")
    Runner.Say("A file whose action differs from the default is tinted blue.")
    App.FileButton(changed, "Replace with the package's file", "Change it back to Replace")
    App.ContinueStage()
    App.Answer("NetDeploy", "Yes", "Run the settings check on the old version first", "Run a settings check on the old version first?")
    App.WaitStage("Check the config")
    App.ConfigFile("appsettings.json")
    Runner.Say("Region keeps the server's west. BatchSize takes the package's 250. Smtp:Server is renamed Smtp:Host. LegacyMode goes. Logging takes the package's (takeNew).")
    App.ReplaceText('("ApiKey":\s*)""', '$1"' ApiKey '"', "Fill in Payments:ApiKey, new and empty in the package, so it needs a value")
    App.ConfigFile("appsettings.Production.json")
    Runner.Say("This override is only on the server now, so its settings are checked against the new appsettings.json.")
    App.ConfigFile("Contoso.Orders.Export.exe.config")
    Runner.Say("BatchSize changed on both sides, 500 to 750 here and to 1000 in the package, so it needs a value. The other settings take the package's.")
    App.Settle("appSettings:BatchSize", "Keep the server's", "Keep the server's BatchSize of 750")
    App.ContinueStage()
    log := App.LogFile(App.WaitStatus(["Deploy finished."], 300000))
    settings := main "\appsettings.json", export := main "\export\Contoso.Orders.Export.exe.config"
    Runner.Checkpoint("main is on 1.1.0 with the server's changes kept", [
        Check.Service("Contoso.Orders-main", "Running"),
        Check.Version(main "\Contoso.Orders.exe", "1.1.0"),
        Check.Json(settings, "Orders:Region", "west"),
        Check.Json(settings, "Orders:BatchSize", "250"),
        Check.Json(settings, "Smtp:Host", "smtp.contoso.local"),
        Check.Json(settings, "Smtp:Server", "<absent>"),
        Check.Json(settings, "LegacyMode", "<absent>"),
        Check.Json(settings, "Payments:ApiKey", ApiKey),
        Check.Json(settings, "Logging:LogLevel:Contoso.Orders", "Debug"),
        Check.Exists(main "\appsettings.Production.json"),
        Check.Missing(main "\templates\password-reset.html"),
        Check.Exists(main "\templates\return-received.html"),
        Check.Exists(OrdersDir "\blue\templates\password-reset.html"),
        Check.AppSetting(export, "BatchSize", "750"),
        Check.AppSetting(export, "Format", "json"),
        Check.AppSetting(export, "IncludeCancelled", "<absent>"),
        Check.ConnectionString(export, "Archive"),
        Check.Output(main "\export\Contoso.Orders.Export.exe", "--check", "BatchSize = 750"),
        Check.Logged(log, "Applied migration 2."),
        Check.Count(main ".backups", "*", 1, "D"),
        Check.Heartbeat(main "\logs\heartbeat.log", "1.1.0 region west"),
        Check.Heartbeat(main "\logs\heartbeat.log", "db schema 2"),
    ], [
        "The folder list shows main with v1.1.0 and blue still on v1.0.0.",
        "The API key shows as **** in the log, never in full.",
        "The database is at schema 2: Database shows the new table.",
    ], [
        Tool.File(settings),
        Tool.File(main "\logs\heartbeat.log", "Open the heartbeat"),
        Tool.Database(main "\Contoso.Orders.exe", OrdersData "\db-main\orders.db"),
        Tool.File(log, "Open the log"),
        Tool.Folder(main ".backups", "Open the backups"),
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== 8 =====

RenameBlue() {
    green := OrdersDir "\green"
    App.GoToRoot()
    App.GoInto(OrdersDir "\blue")
    App.Type({Type: "Edit", Name: "Install name"}, "green", "Change the install name to green")
    App.Find({Type: "Button", Name: "Rename"}, "the Rename button")
    Runner.Say("The button says Rename: blue is copied to green, 1.1.0 is deployed there, then blue can be removed.")
    App.ClickButton("Rename", "Click Rename")
    App.WaitStage("Check the files")
    App.ContinueStage()
    App.Answer("NetDeploy", "No", "Skip the optional settings check", "Run a settings check on the old version first?")
    App.WaitStage("Check the config")
    App.ConfigFile("appsettings.json")
    App.ReplaceText('("ApiKey":\s*)""', '$1"' ApiKey '"', "Fill in Payments:ApiKey")
    App.ContinueStage()
    App.Answer("Remove the old install", "Yes", "Remove blue now that green works", "Remove the old one", 300000)
    App.RunRemoval()
    Runner.Checkpoint("blue is now green, on 1.1.0", [
        Check.Service("Contoso.Orders-green", "Running"),
        Check.Service("Contoso.Orders-blue", ""),
        Check.Task("\Contoso\Orders Export-green"),
        Check.Task("\Contoso\Orders Export-blue", false),
        Check.Version(green "\Contoso.Orders.exe", "1.1.0"),
        Check.Missing(OrdersDir "\blue"),
        Check.Exists(OrdersData "\db-green\orders.db"),
        Check.Missing(OrdersData "\db-blue"),
        Check.Service("Contoso.Orders-main", "Running"),
    ], [
        "Each removal step said exactly what it would do before it ran.",
        "The folder list shows green and main, both v1.1.0.",
    ], [
        Tool.Folder(OrdersDir, "Install folder"),
        Tool.Folder(OrdersData, "Database folder"),
        Tool.Services("Contoso.Orders"),
        Tool.Tasks("\Contoso"),
    ])
}

; ===== 9 =====

CloneMain() {
    qa := OrdersDir "\qa"
    App.GoToRoot()
    App.InstallButton(OrdersDir "\main", "Clone: deploy as a new install that starts from a copy of this one", "Clone main")
    App.NewInstallName("qa")
    App.Find({Type: "ComboBox", Name: "Start from"}, "Start from")
    Runner.Say("Start from says A copy of main, so qa starts with main's files and settings, such as Region west.")
    App.ClickButton("Add", "Click Add to deploy qa")
    App.WaitStage("Check the files")
    App.ContinueStage()
    App.Answer("NetDeploy", "No", "Skip the optional settings check", "Run a settings check on the old version first?")
    App.WaitStage("Check the config")
    App.ContinueStage()
    App.WaitStatus(["Deploy finished."], 300000)
    Runner.Checkpoint("qa is a copy of main", [
        Check.Service("Contoso.Orders-qa", "Running"),
        Check.Json(qa "\appsettings.json", "Orders:Region", "west"),
        Check.Json(qa "\appsettings.json", "Payments:ApiKey", ApiKey),
        Check.Exists(OrdersData "\db-qa\orders.db"),
        Check.Task("\Contoso\Orders Export-qa"),
    ], [
        "Start from read A copy of main before the deploy.",
        "The folder list shows green, main and qa.",
    ], [
        Tool.Folder(qa, "Open qa"),
        Tool.File(qa "\appsettings.json"),
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== 10 =====

EditConfig() {
    folder := Machine.Temp "\editor"
    config := folder "\deploy.json"
    Runner.Step("Unpack Contoso.Orders 1.1.0 into a folder, to edit its deploy.json there")
    Machine.Unzip(Package("Contoso.Orders-1.1.0"), folder)
    App.ClickButton("Open config")
    App.PickFile("Open a deploy config", config, "Pick the unpacked deploy.json")
    App.Tab("deploy.json")
    App.Click({Type: "Text", HelpText: "Copy the app"}, "Select the Copy the app step", "mouse")
    Runner.Say("The form shows the copy step's settings, built from the schema.")
    App.Click({Type: "RadioButton", Name: "JSON"}, "Switch to the JSON view")
    Runner.Say("The JSON view shows the file as it will be saved.")
    App.Click({Type: "RadioButton", Name: "Form"}, "Back to the form")
    App.Click({Type: "Text", HelpText: "App and parameters"}, "Select App and parameters", "mouse")
    App.Type({Type: "Edit", Name: "Version"}, "", "Clear the version")
    Runner.Say("The problems above the form now say the version is missing.")
    App.Type({Type: "Edit", Name: "Version"}, "1.1.1", "Set the version to 1.1.1")
    App.ClickButton("Save")
    App.WaitHeader("Contoso.Orders 1.1.1")
    Runner.Checkpoint("The config is saved, and its folder is open as the package", [
        Check.FileHas(config, "1.1.1", "deploy.json now has version 1.1.1"),
    ], [
        "The header says Contoso.Orders 1.1.1 and the unpacked folder.",
        "The problem showed while the version was empty, and went once it was typed.",
        "deploy.json kept its $schema line and the order of its settings.",
    ], [
        Tool.File(config, "Open deploy.json"),
    ])
}

; ===== 11 =====

ManageFolder() {
    qa := OrdersDir "\qa"
    App.ClickButton("Open folder")
    App.PickFolder("Open an install folder", OrdersDir, "Pick C:\Apps\Contoso.Orders")
    App.WaitHeader("Contoso.Orders")
    Runner.Say("Without a package the folder is for managing what is there: each install with its version.")
    App.GoInto(qa)
    App.FileButton("Contoso.Orders.exe", "Run, with arguments", "Run Contoso.Orders.exe")
    App.AnswerInputs("Run Contoso.Orders.exe", "Give it the argument check",
        (window) => App.Type({Type: "Edit", Name: "Arguments"}, "check", "Type check", , window))
    status := App.WaitStatus(["Contoso.Orders.exe exited with code"])
    App.Tab("Files")
    App.FileButton("README.txt", "Delete when finalized", "Stage README.txt to be deleted")
    App.ClickButton("Finalize")
    App.Answer("Finalize", "Yes", "Delete it", "README.txt")
    App.WaitStatus(["Changes written."])
    App.GoInto(qa "\templates")
    Runner.Say("Going into a plain folder lists just its files.")
    App.GoUp()
    Runner.Checkpoint("The folder was managed without a package", [
        Check.Make("Contoso.Orders.exe check exited with code 0", () => InStr(status, "code 0.") ? "" : status),
        Check.Missing(qa "\README.txt"),
    ], [
        "The folder list showed green, main and qa, each with v1.1.0.",
        "The Log tab shows the settings check's output, ending Check passed.",
        "The header says Contoso.Orders and C:\Apps\Contoso.Orders, with no PACKAGE panel.",
    ], [
        Tool.Folder(qa, "Open qa"),
    ])
}

; ===== 12 =====

AddReports() {
    main := ReportsDir "\main"
    App.OpenPackage(Package("Fabrikam.Reports-2.0.0"))
    App.WaitHeader("Fabrikam.Reports 2.0.0")
    App.NewInstallName("main")
    App.ClickButton("Add", "Click Add to deploy Fabrikam.Reports")
    App.WaitStage("Check the files")
    App.ContinueStage()
    App.WaitStage("Check the config")
    App.ConfigFile("Fabrikam.Reports.exe.config")
    App.ReplaceText('(key="SmtpPassword"\s+value=)""', '$1"smtp-test-123"', "Fill in SmtpPassword, empty in the package, so it needs a value")
    App.ConfigFile("web.config")
    Runner.Say("A .NET Framework app's .exe.config and its site's web.config, both new here.")
    App.ContinueStage()
    App.Answer("NetDeploy", "Yes", "Yes, run a report now", "Run a report now to check the install?", 120000)
    App.AnswerInputs("Schedule the hourly report", "Keep SYSTEM as the account for the hourly report",
        (window) => App.Choose({Type: "ComboBox", Name: "Run the hourly report as"}, "SYSTEM", "Pick SYSTEM", window))
    log := App.LogFile(App.WaitStatus(["Deploy finished."], 300000))
    Runner.Checkpoint("Fabrikam.Reports is installed", [
        Check.Exists(main "\data\sales.csv"),
        Check.Count(main "\reports", "*.pdf", 1),
        Check.Task("\Fabrikam\Reports hourly"),
        Check.Logged(log, "The report site is not published"),
    ], [
        "The question and the inputs window each came up once, in that order.",
        "Task Scheduler: \Fabrikam\Reports hourly runs every hour as SYSTEM.",
    ], [
        Tool.Folder(main, "Open main"),
        Tool.Tasks("\Fabrikam"),
        Tool.File(log, "Open the log"),
    ])
}

; ===== 13 =====

UpgradeReports() {
    main := ReportsDir "\main"
    config := main "\Fabrikam.Reports.exe.config", web := main "\wwwroot\web.config"
    Runner.Step("Change three settings on the server by hand: ReportTitle, KeepReports and the site's AllowDownload")
    Machine.SetAppSetting(config, "ReportTitle", "Fabrikam weekly sales")
    Machine.SetAppSetting(config, "KeepReports", "45")
    Machine.SetAppSetting(web, "AllowDownload", "false")
    App.OpenPackage(Package("Fabrikam.Reports-2.1.0"))
    App.WaitHeader("Fabrikam.Reports 2.1.0")
    App.SetCheck({Type: "CheckBox", Name: "Publish the report site"}, true, "Tick Publish the report site")
    App.ClickButton("Update", "Click Update to upgrade Fabrikam.Reports")
    App.WaitStage("Check the files")
    App.Hover(App.FileName("plugins/Fabrikam.Reports.Pdf.dll"), "The Pdf plugin is deleted")
    App.Hover(App.FileName("plugins/Fabrikam.Reports.Excel.dll"), "and the Excel one added")
    App.ContinueStage()
    App.WaitStage("Check the config")
    App.ConfigFile("Fabrikam.Reports.exe.config")
    Runner.Say("ReportTitle keeps the server's text, KeepReports takes the package's 90 (takeNew), MaxRows is added and LegacyPdfEngine removed.")
    App.ConfigFile("web.config")
    Runner.Say("web.config keeps the server's AllowDownload and gains PageSize.")
    App.ContinueStage()
    App.Answer("NetDeploy", "Yes", "Yes, run a report now", "Run a report now to check the install?", 120000)
    App.AnswerInputs("Schedule the hourly report", "Keep SYSTEM as the account")
    log := App.LogFile(App.WaitStatus(["Deploy finished."], 300000))
    Runner.Checkpoint("Fabrikam.Reports is on 2.1.0 with the server's changes kept", [
        Check.Logged(log, "left as it is"),
        Check.Logged(log, "plugins: csv, xlsx"),
        Check.Logged(log, "Check passed."),
        Check.Missing(main "\plugins\Fabrikam.Reports.Pdf.dll"),
        Check.Count(main "\reports", "*.xlsx", 1),
        Check.AppSetting(config, "ReportTitle", "Fabrikam weekly sales"),
        Check.AppSetting(config, "KeepReports", "90"),
        Check.AppSetting(config, "LegacyPdfEngine", "<absent>"),
        Check.ConnectionString(config, "Audit"),
        Check.FileHas(config, 'newVersion="13.0.0.0"', "The binding redirect comes from the new file"),
        Check.AppSetting(web, "AllowDownload", "false"),
        Check.AppSetting(web, "PageSize", "25"),
    ], [
        "The files review showed the Pdf plugin deleted and the Excel one added.",
        "The reports folder has the first report as .pdf and the new one as .xlsx.",
    ], [
        Tool.File(config, "Open .exe.config"),
        Tool.File(web, "Open web.config"),
        Tool.Folder(main "\reports", "Open reports"),
        Tool.File(log, "Open the log"),
    ])
}

; ===== 14 =====

RemoveAll() {
    App.GoToRoot()
    App.RemoveInstall(ReportsDir "\main")
    App.OpenPackage(Package("Contoso.Orders-1.1.0"))
    App.WaitHeader("Contoso.Orders 1.1.0")
    ; A removal can leave the folder list inside the next install, so each starts from the top.
    for name in ["qa", "green", "main"] {
        App.GoToRoot()
        App.RemoveInstall(OrdersDir "\" name)
    }
    Runner.Checkpoint("Every install is gone", [
        Check.Service("Contoso.Orders-main", ""),
        Check.Service("Contoso.Orders-green", ""),
        Check.Service("Contoso.Orders-qa", ""),
        Check.Task("\Contoso\Orders Export-main", false),
        Check.Task("\Fabrikam\Reports hourly", false),
        Check.Missing(OrdersDir "\main"),
        Check.Missing(OrdersData "\db-main"),
        Check.Missing(ReportsDir "\main"),
    ], [
        "Each removal listed its steps one at a time, newest first, ending with deleting the folder.",
        "The folder list is empty apart from the New install row.",
    ], [
        Tool.Folder("C:\Apps", "Open C:\Apps"),
        Tool.Services("Contoso.Orders"),
        Tool.App("Task Scheduler", "taskschd.msc"),
    ])
    App.Close()
}
