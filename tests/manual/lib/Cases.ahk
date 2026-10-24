; The manual test, as cases that run in order. Each case starts where the one before it left off, so a case picked
; on its own needs what the earlier ones made (its Needs). Every case ends at a check: what the runner can check by
; itself, then what to look at by hand, with buttons that open it.

global OrdersDir := "C:\Apps\Contoso.Orders"
global OrdersData := "C:\ProgramData\Contoso.Orders"
global ReportsDir := "C:\Apps\Fabrikam.Reports"
global ApiKey := "pk-test-123"

class Cases {
    static All := [
        {Name: "Open a package", Run: OpenPackage, Needs: "A clean computer",
            Covers: "The start screen, opening a package, its settings, and the Steps and deploy.json tabs"},
        {Name: "Try a deploy without changing anything", Run: DryRun, Needs: "Case 1",
            Covers: "Naming a new install, a dry run, the list of files and the settings files it would write, the summary of what it would change, and nothing being written"},
        {Name: "Install the app for the first time (main)", Run: AddMain, Needs: "Case 2",
            Covers: "A real deploy: the Windows service, the scheduled task, the database, the settings, and the green dot for a running service"},
        {Name: "Start, stop and restart a service", Run: ControlService, Needs: "Case 3",
            Covers: "A service stopped outside NetDeploy and Refresh, then Stop, Start and Restart from the buttons beside an install, with the dot following along"},
        {Name: "Install it a second time, for Production (blue)", Run: AddBlue, Needs: "Case 3",
            Covers: "Picking from a list of choices, a second install beside the first, and the steps that only run for Production"},
        {Name: "Change settings on the server", Run: EditOnServer, Needs: "Case 3",
            Covers: "Going into an install, folding a folder, filtering the files, editing with tracked changes, undoing one change, staging a delete and taking it back, and Finalize"},
        {Name: "Throw away changes you don't want", Run: ThrowAwayChanges, Needs: "Case 6",
            Covers: "Undo all in the editor, and Discard for everything waiting to be written"},
        {Name: "Cancel an upgrade halfway, then roll it back", Run: CancelAndRollBack, Needs: "Case 5",
            Covers: "The arrow saying the package is newer, a secret setting, the files an upgrade adds and deletes, a yes/no question, Cancel during a deploy, and Roll back"},
        {Name: "Put a backup back", Run: RestoreBackup, Needs: "Case 8",
            Covers: "The history of an install's deploys, a file lost on the server, and Restore beside a backup an earlier deploy made"},
        {Name: "Upgrade main to 1.1.0, keeping the server's changes", Run: UpgradeMain, Needs: "Case 9",
            Covers: "Each install's own remembered settings, showing the files that haven't changed, changing what happens to a file, how settings are combined, a renamed setting, and settings that need a value"},
        {Name: "Rename blue to green", Run: RenameBlue, Needs: "Case 10",
            Covers: "Changing an install's name, Rename, and removing the old install one step at a time"},
        {Name: "Copy main as a new install (qa)", Run: CloneMain, Needs: "Case 11",
            Covers: "Clone from the buttons that show when you point at an install, and a new install that starts as a copy"},
        {Name: "Deploy from the command line", Run: DeployFromCommandLine, Needs: "Case 12",
            Covers: "NetDeploy.exe deploy as a pipeline would run it: a mistake in the command, a deploy with a secret from the environment, and the deploy showing in the history"},
        {Name: "Change your mind while removing an install", Run: StopRemoving, Needs: "Case 12",
            Covers: "Stop removing, at the first step, leaving the install as it was"},
        {Name: "Remove an install someone copied by hand", Run: RemoveHandCopy, Needs: "Case 10",
            Covers: "A folder copied outside NetDeploy showing up as an install, and Skip this step for what it never had"},
        {Name: "Edit a deploy.json", Run: EditConfig, Needs: "Any time",
            Covers: "Open config, the list of steps, the form and the text view, a problem shown as you type, and Save"},
        {Name: "Look after an install folder without a package", Run: ManageFolder, Needs: "Case 12",
            Covers: "Open folder, the version of each install, running a program with what to pass it, deleting a file, and Up"},
        {Name: "Pick up where you left off", Run: PickUpWhereYouLeftOff, Needs: "Case 12",
            Covers: "The start screen's Recent list, a package opening with the install folder and each install's settings from last time, and the sign for the same version"},
        {Name: "Install Fabrikam.Reports 2.0.0", Run: AddReports, Needs: "No Fabrikam.Reports yet",
            Covers: "An older-style .NET Framework app, a question during the deploy, and a window asking for a step's details"},
        {Name: "Upgrade Fabrikam.Reports to 2.1.0", Run: UpgradeReports, Needs: "Case 19",
            Covers: "Settings changed by hand on the server, a tick box setting, combining older-style settings files, and plugins swapped"},
        {Name: "Remove every install", Run: RemoveAll, Needs: "Cases 12 and 20",
            Covers: "Remove from the buttons that show when you point at an install, each step confirmed, and a clean computer at the end"},
    ]
}

Package(name) => Settings.Packages "\" name ".zip"

; ===== Open a package =====

OpenPackage() {
    App.Start()
    App.Find({Type: "Text", Name: "Drop a package, folder or deploy.json here"}, "the start screen")
    Runner.Say("NetDeploy starts empty. From here you can open a package to install, a folder of installs to look after, or a deploy.json to edit.")
    App.OpenPackage(Package("Contoso.Orders-1.0.0"))
    App.WaitHeader("Contoso.Orders 1.0.0")
    Runner.Say("The package is open. On the left are its settings, and below them the folder it installs into, which isn't there yet.")
    App.Tab("Steps", "Open the Steps tab")
    Runner.Say("The Steps tab lists everything the package does when it is deployed, in order.")
    App.Tab("deploy.json", "Open the deploy.json tab")
    Runner.Say("The deploy.json tab shows the package's instructions in an editor.")
    App.Tab("Files", "Back to the Files tab")
    Runner.Checkpoint("The package is open", [
        Check.Missing(OrdersDir),
    ], [
        "The title bar says Contoso.Orders 1.0.0, with where the zip is beside it.",
        "PACKAGE shows Database folder and Environment (Staging), then the Dry run and Add buttons.",
        "INSTALL FOLDER shows C:\Apps\Contoso.Orders, marked not created yet, with a New install row at the bottom.",
        "Under the folder list it says to type a name for the new install.",
        "The Steps tab listed 11 steps, with what Lock down production does either way indented under it.",
    ])
}

; ===== Try a deploy without changing anything =====

DryRun() {
    App.NewInstallName("main")
    App.Find({Type: "Button", Name: "Add"}, "the Add button")
    Runner.Say("The deploy button now says Add, because this would add a new install called main.")
    App.ClickButton("Dry run", "Click Dry run, to see what a deploy would do without doing it")
    App.WaitStage("Check the files")
    App.Find({Type: "Text", Name: "Dry run: nothing will change"}, "the sign saying it's a dry run")
    Runner.Say("A dry run shows the same lists as a real deploy. Nothing is installed yet, so every file would be added.")
    App.ContinueStage()
    App.WaitStage("Check the config")
    for file in ["appsettings.json", "appsettings.Production.json", "Contoso.Orders.Export.exe.config"]
        App.ConfigFile(file)
    Runner.Say("Each settings file is shown just as it would be written. In a dry run they can't be changed.")
    App.ContinueStage()
    status := App.WaitStatus(["Dry run finished."])
    App.Find({Type: "Text", Name: "Dry run: what the deploy would change"}, "the dry run's summary above the log")
    files := App.Hover({Type: "Text", Name: "Files: ", mm: "StartsWith"},
        "Above the log, a summary counts the files a deploy would add, replace and delete, and lists the steps that would change the server").Name
    Runner.Checkpoint("The dry run changed nothing", [
        Check.Missing(OrdersDir),
        Check.Missing(OrdersData),
        Check.Service("Contoso.Orders-main", ""),
        Check.Exists(App.LogFile(status)),
        Check.Make("The summary says every file would be added, and none replaced or deleted",
            () => RegExMatch(files, "^Files: [1-9]\d* added, 0 replaced, 0 deleted\.$") ? "" : "it says " files),
    ], [
        "The summary above the log lists the service, the scheduled task and the scripts the deploy would run.",
        "The Log tab lists each step and what it would have done.",
        "The Steps tab marks every step as done or skipped.",
        "The bar at the bottom says Dry run finished. Nothing was changed.",
    ], [
        Tool.File(App.LogFile(status), "Open the log"),
    ])
}

; ===== Install the app for the first time =====

AddMain() {
    main := OrdersDir "\main"
    App.ClickButton("Add", "Click Add to install main for real")
    App.WaitStage("Check the files")
    App.ContinueStage()
    App.WaitStage("Check the config")
    Runner.Say("This time the settings could be edited before they are written.")
    App.ContinueStage()
    log := App.LogFile(App.WaitStatus(["Deploy finished."], 300000))
    App.GoToRoot()
    App.Hover({Type: "Text", Name: "Contoso.Orders-main: running", mm: 2}, "The green dot beside main means its service is running")
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
        "The folder list shows main, v1.0.0 and a green dot. Pointing at the dot says the service is running.",
        "In Services, Contoso Orders main is running and starts by itself (Automatic, Delayed Start).",
        "In Task Scheduler, \Contoso\Orders Export-main runs every day at 02:00 as SYSTEM.",
        "The database is in C:\ProgramData\Contoso.Orders\db-main, not in the install folder, and has 4 orders.",
    ], [
        Tool.Folder(main, "Open main"),
        Tool.Folder(OrdersData, "Database folder"),
        Tool.Database(main "\Contoso.Orders.exe", OrdersData "\db-main\orders.db"),
        Tool.Services("Contoso.Orders"),
        Tool.Tasks("\Contoso"),
        Tool.File(main "\appsettings.json"),
    ])
}

; ===== Start, stop and restart a service =====

ControlService() {
    main := OrdersDir "\main"
    App.GoToRoot()
    Runner.Step("Stop main's service outside NetDeploy, as if it had crashed")
    Machine.SetService("Contoso.Orders-main", "Stop")
    App.Refresh("Click Refresh above the folder list, so NetDeploy looks at the services again")
    App.Hover({Type: "Text", Name: "Contoso.Orders-main: stopped", mm: 2}, "The dot beside main is red now. Pointing at it says the service has stopped")
    App.InstallButton(main, "Start its services", "Start main's service again, with the button that shows when you point at main")
    App.WaitStatus(["Contoso.Orders-main: start done."], 120000)
    App.Hover({Type: "Text", Name: "Contoso.Orders-main: running", mm: 2}, "And the dot is green again")
    App.InstallButton(main, "Stop its services", "Stop main's service from NetDeploy")
    App.Answer("Stop service", "Yes", "Yes, stop it", "Stop Contoso.Orders-main?")
    App.WaitStatus(["Contoso.Orders-main: stop done."], 120000)
    App.Hover({Type: "Text", Name: "Contoso.Orders-main: stopped", mm: 2}, "NetDeploy asked first, then stopped it: the dot is red")
    stopped := Machine.ServiceState("Contoso.Orders-main")
    App.InstallButton(main, "Start its services", "Start it again")
    App.WaitStatus(["Contoso.Orders-main: start done."], 120000)
    App.InstallButton(main, "Restart its services", "Restart main's service")
    App.Answer("Restart service", "Yes", "Yes, restart it", "Restart Contoso.Orders-main?")
    App.WaitStatus(["Contoso.Orders-main: restart done."], 120000)
    App.Hover({Type: "Text", Name: "Contoso.Orders-main: running", mm: 2}, "After the restart the dot is green")
    Runner.Checkpoint("NetDeploy shows how main's service is doing, and starts and stops it", [
        Check.Make("Stop from NetDeploy stopped the service", () => stopped = "Stopped" ? "" : "it was " StrLower(stopped)),
        Check.Service("Contoso.Orders-main", "Running"),
        Check.Heartbeat(main "\logs\heartbeat.log", "1.0.0"),
    ], [
        "The dot beside main went red while its service was stopped, and green once it was started.",
        "Stop and Restart asked first; Start didn't.",
        "The bar at the bottom said what was done each time.",
    ], [
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== Install it a second time, for Production =====

AddBlue() {
    blue := OrdersDir "\blue"
    App.GoToRoot()
    App.NewInstallName("blue")
    App.Choose({Type: "ComboBox", Name: "Environment"}, "Production", "Set Environment to Production")
    App.ClickButton("Add", "Click Add to install blue")
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
        "The folder list shows blue and main, both on v1.0.0.",
        "In Services, Contoso Orders blue and Contoso Orders main are both running.",
        "blue has a database of its own, in db-blue.",
    ], [
        Tool.Folder(blue, "Open blue"),
        Tool.Folder(OrdersData, "Database folder"),
        Tool.Services("Contoso.Orders"),
        Tool.Tasks("\Contoso"),
    ])
}

; ===== Change settings on the server =====

EditOnServer() {
    main := OrdersDir "\main"
    alerts := '"Alerts": { "Emails": [ "ops@contoso.local", "oncall@contoso.local" ] },'
    App.GoToRoot()
    App.GoInto(main)
    Runner.Say("Going into main makes it what the next deploy updates, so the button now says Update. Its files are listed on the right.")
    App.Tab("Files")
    App.ToggleFolder("templates", "Fold up the templates folder, as in File Explorer")
    App.ToggleFolder("templates", "And open it again")
    App.EditFile("appsettings.json")
    App.ReplaceText('("Region":\s*)"east"', '$1"west"', "Change Region to west, as someone might on the server")
    Runner.Say("The change shows where it was made: east crossed out in red, west in green.")
    App.ReplaceText('("HeartbeatSeconds":\s*)10', '${1}20', "Change HeartbeatSeconds to 20")
    App.ClickMark("Undo", "Put back the server's text: 10", "Then undo just that change, with the button at the end of its line")
    App.ReplaceText("^\s*\{", "{`r`n  " alerts, "Add an Alerts section, with a list of emails, at the top of the file")
    Runner.Say("The new line is green. The Region change is still there, and HeartbeatSeconds is back to 10.")
    App.FilterFiles("config", "Type config in the filter box, to list only the files with config in their name")
    App.EditFile("export/Contoso.Orders.Export.exe.config")
    App.ReplaceText('(key="BatchSize"\s+value=)"500"', '$1"750"', "Change the export tool's BatchSize to 750")
    App.FilterFiles("", "Clear the filter box")
    App.Find({Type: "Text", Name: "3 changes"}, "the bar at the bottom saying 3 changes")
    App.FileButton("README.txt", "Delete when finalized", "Mark README.txt to be deleted")
    App.Find({Type: "Text", Name: "3 changes, 1 file to delete"}, "the bar saying a file will be deleted")
    App.FileButton("README.txt", "Keep this file", "Change your mind and keep README.txt")
    App.ClickButton("Finalize", "Click Finalize to write the changes")
    App.Answer("Finalize", "Yes", "Yes, write the changes", "Write 2 changes to main/appsettings.json")
    App.WaitStatus(["Changes written."])
    Runner.Checkpoint("The server's settings changed", [
        Check.Json(main "\appsettings.json", "Orders:Region", "west"),
        Check.Json(main "\appsettings.json", "Orders:HeartbeatSeconds", "10"),
        Check.Json(main "\appsettings.json", "Alerts:Emails", "ops@contoso.local oncall@contoso.local"),
        Check.AppSetting(main "\export\Contoso.Orders.Export.exe.config", "BatchSize", "750"),
        Check.Exists(main "\README.txt"),
    ], [
        "Finalize asked about exactly the two settings files.",
        "README.txt is still in the list, and not crossed out.",
        "appsettings.json has the Alerts section just as it was typed, Region west, and is otherwise laid out as before.",
    ], [
        Tool.File(main "\appsettings.json"),
        Tool.File(main "\export\Contoso.Orders.Export.exe.config", "Open the export's settings"),
    ])
}

; ===== Throw away changes you don't want =====

ThrowAwayChanges() {
    main := OrdersDir "\main"
    App.GoToRoot()
    App.GoInto(main)
    App.Tab("Files")
    App.EditFile("appsettings.json")
    App.ReplaceText('("Region":\s*)"west"', '$1"north"', "Change Region to north")
    App.Find({Type: "Text", Name: "1 change"}, "the bar at the bottom saying 1 change")
    App.ClickButton("Undo all", "Click Undo all, to put the whole file back as it is on the server")
    App.WaitGone({Type: "Button", Name: "Finalize"}, "the bar at the bottom")
    Runner.Say("The file is back as it was, and the bar at the bottom has gone: nothing is waiting to be written.")
    App.ReplaceText('("HeartbeatSeconds":\s*)10', '${1}30', "Change HeartbeatSeconds to 30")
    App.FileButton("README.txt", "Delete when finalized", "Mark README.txt to be deleted")
    App.Find({Type: "Text", Name: "1 change, 1 file to delete"}, "the bar saying 1 change and 1 file to delete")
    App.ClickButton("Discard", "Click Discard, to throw away everything that is waiting")
    App.WaitGone({Type: "Button", Name: "Finalize"}, "the bar at the bottom")
    Runner.Checkpoint("Nothing was written", [
        Check.Json(main "\appsettings.json", "Orders:Region", "west"),
        Check.Json(main "\appsettings.json", "Orders:HeartbeatSeconds", "10"),
        Check.Exists(main "\README.txt"),
    ], [
        "Undo all took the Region change out of the editor straight away.",
        "Discard closed the file and took the crossing out off README.txt.",
        "Nothing asked to write anything.",
    ], [
        Tool.File(main "\appsettings.json"),
    ])
}

; ===== Cancel an upgrade halfway, then roll it back =====

CancelAndRollBack() {
    blue := OrdersDir "\blue"
    App.OpenPackage(Package("Contoso.Orders-1.1.0"))
    App.WaitHeader("Contoso.Orders 1.1.0")
    App.Type({Type: "Edit", Name: "Payments API key (new in 1.1)"}, ApiKey, "Type the payments API key, a secret setting new in 1.1", true)
    App.GoToRoot()
    App.Hover(App.Find({Type: "Text", Name: "The package (1.1.0) is newer: deploying here updates it."}, "the arrow beside blue's version", , App.FolderRow(blue)),
        "The green arrow beside blue's version points up: the open package is newer than what is installed")
    App.GoInto(blue)
    App.Find({Type: "Button", Name: "Update"}, "the Update button")
    Runner.Say("blue is on 1.0.0, so the button says Update.")
    App.ClickButton("Update", "Click Update to upgrade blue")
    App.WaitStage("Check the files")
    App.Hover(App.FileName("templates/password-reset.html"), "1.1.0 no longer has password-reset.html, so it will be deleted (crossed out, in red)")
    App.Hover(App.FileName("templates/return-received.html"), "and return-received.html is new")
    App.ContinueStage()
    App.Answer("NetDeploy", "No", "Say No to the optional check of the old version's settings", "Run a settings check on the old version first?")
    App.WaitStage("Check the config")
    Runner.Say("Now cancel, while the settings are being checked. The service is already stopped and a backup taken, so NetDeploy offers to put things back.")
    App.ClickButton("Cancel", "Click Cancel")
    App.Answer("Roll back", "Yes", "Yes, restore the backup and start the service again", "Restore the backup")
    App.WaitStatus(["Rolled back to the backup."], 120000)
    Runner.Checkpoint("blue is back as it was", [
        Check.Service("Contoso.Orders-blue", "Running"),
        Check.Version(blue "\Contoso.Orders.exe", "1.0.0"),
        Check.Exists(blue "\templates\password-reset.html"),
        Check.Missing(blue "\templates\return-received.html"),
    ], [
        "The folder list still shows blue on v1.0.0.",
        "The Log tab shows the cancel, then the backup put back and the service started.",
        "The blue.backups folder, next to blue, holds the backup.",
    ], [
        Tool.Folder(blue, "Open blue"),
        Tool.Folder(OrdersDir, "Install folder"),
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== Put a backup back =====

RestoreBackup() {
    blue := OrdersDir "\blue"
    App.GoToRoot()
    App.GoInto(blue)
    App.Hover({Type: "Text", Name: "1.1.0 stopped, rolled back"},
        "HISTORY lists the deploys made to blue, newest first: the upgrade that was stopped and rolled back, then the first install")
    Runner.Step("Delete blue's README.txt outside NetDeploy, as if someone had removed it by mistake")
    FileDelete(blue "\README.txt")
    App.Click({Type: "Button", HelpText: "Put this backup back in place of the folder's files"},
        "Click Restore beside the backup the upgrade took of blue, under BACKUPS")
    App.Answer("Restore backup", "Yes", "Yes, put it back", "Put back the backup from")
    App.WaitStatus(["Restored "], 180000)
    Runner.Checkpoint("blue is back as the backup had it", [
        Check.Exists(blue "\README.txt"),
        Check.Version(blue "\Contoso.Orders.exe", "1.0.0"),
        Check.Service("Contoso.Orders-blue", "Running"),
    ], [
        "Restore asked first, and said the services would be stopped and started again.",
        "The Log tab shows the service stopped, the files put back and the service started.",
        "HISTORY showed the stopped upgrade with the time and who ran it.",
    ], [
        Tool.Folder(blue, "Open blue"),
        Tool.Folder(blue ".backups", "Open the backups"),
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== Upgrade main, keeping the server's changes =====

UpgradeMain() {
    main := OrdersDir "\main"
    unchanged := {Type: "Button", HelpText: "Show files that are the same on the server"}
    App.GoToRoot()
    App.GoInto(main)
    environment := App.Selection({Type: "ComboBox", Name: "Environment"}, "the Environment box")
    Runner.Say("blue was deployed for Production, but main for Staging: going into main brings back the settings main was last deployed with, so Environment says " environment ".")
    App.ClickButton("Update", "Click Update to upgrade main")
    App.WaitStage("Check the files")
    App.Click(unchanged, "Click the eye, to also list the files that are already the same on the server")
    Runner.Say("Now every file is listed, including the ones the upgrade leaves as they are.")
    App.Click(unchanged, "Click the eye again, to list only what changes")
    changed := "templates/order-confirmation.html"
    App.FileButton(changed, "Keep the server's file as it is", "Keep the server's copy of order-confirmation.html instead")
    Runner.Say("A file that won't get what it normally would is tinted blue.")
    App.FileButton(changed, "Replace with the package's file", "Change your mind: take the package's copy after all")
    App.ContinueStage()
    App.Answer("NetDeploy", "Yes", "Say Yes to checking the old version's settings first", "Run a settings check on the old version first?")
    App.WaitStage("Check the config")
    App.ConfigFile("appsettings.json")
    Runner.Say("Region keeps the server's west. BatchSize takes the package's 250. Smtp:Server is now called Smtp:Host. LegacyMode is gone. Logging always takes the package's settings.")
    App.ClickButton("Next change", "Jump to the next change, with the arrow above the text")
    App.ReplaceText('("ApiKey":\s*)""', '$1"' ApiKey '"', "Fill in Payments:ApiKey: it's new and empty in the package, so it needs a value")
    App.ConfigFile("appsettings.Production.json")
    Runner.Say("Only the server has this file now, so NetDeploy checks its settings still exist in the new appsettings.json.")
    App.ConfigFile("Contoso.Orders.Export.exe.config")
    Runner.Say("BatchSize changed on both sides, to 750 here and to 1000 in the package, so you pick. The other settings take the package's.")
    App.Settle("appSettings:BatchSize", "Keep the server's", "Keep the server's BatchSize of 750")
    App.ContinueStage()
    log := App.LogFile(App.WaitStatus(["Deploy finished."], 300000))
    settings := main "\appsettings.json", export := main "\export\Contoso.Orders.Export.exe.config"
    Runner.Checkpoint("main is on 1.1.0, with the server's changes kept", [
        Check.Service("Contoso.Orders-main", "Running"),
        Check.Version(main "\Contoso.Orders.exe", "1.1.0"),
        Check.Json(settings, "Orders:Region", "west"),
        Check.Json(settings, "Orders:BatchSize", "250"),
        Check.Json(settings, "Smtp:Host", "smtp.contoso.local"),
        Check.Json(settings, "Smtp:Server", "<absent>"),
        Check.Json(settings, "LegacyMode", "<absent>"),
        Check.Json(settings, "Payments:ApiKey", ApiKey),
        Check.Json(settings, "Logging:LogLevel:Contoso.Orders", "Debug"),
        Check.Make("Environment said Staging for main, as main was last deployed", () => environment = "Staging" ? "" : "it said " environment),
        Check.Missing(main "\PRODUCTION.txt"),
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
        "The folder list shows main on v1.1.0, and blue still on v1.0.0.",
        "The log shows the API key as ****, never in full.",
        "The database has been upgraded: Database shows the new table.",
    ], [
        Tool.File(settings),
        Tool.File(main "\logs\heartbeat.log", "Open the heartbeat"),
        Tool.Database(main "\Contoso.Orders.exe", OrdersData "\db-main\orders.db"),
        Tool.File(log, "Open the log"),
        Tool.Folder(main ".backups", "Open the backups"),
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== Rename blue to green =====

RenameBlue() {
    green := OrdersDir "\green"
    App.GoToRoot()
    App.GoInto(OrdersDir "\blue")
    App.Type({Type: "Edit", Name: "Install name"}, "green", "Change the install's name to green")
    App.Find({Type: "Button", Name: "Rename"}, "the Rename button")
    Runner.Say("The button says Rename: blue is copied to green, 1.1.0 is installed there, and then blue can be removed.")
    App.ClickButton("Rename", "Click Rename")
    App.WaitStage("Check the files")
    App.ContinueStage()
    App.Answer("NetDeploy", "No", "Say No to the optional settings check", "Run a settings check on the old version first?")
    App.WaitStage("Check the config")
    App.ConfigFile("appsettings.json")
    App.ReplaceText('("ApiKey":\s*)""', '$1"' ApiKey '"', "Fill in Payments:ApiKey")
    App.ContinueStage()
    App.Answer("Remove the old install", "Yes", "Yes, remove blue now that green works", "Remove the old one", 300000)
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
        "The folder list shows green and main, both on v1.1.0.",
    ], [
        Tool.Folder(OrdersDir, "Install folder"),
        Tool.Folder(OrdersData, "Database folder"),
        Tool.Services("Contoso.Orders"),
        Tool.Tasks("\Contoso"),
    ])
}

; ===== Copy main as a new install =====

CloneMain() {
    qa := OrdersDir "\qa"
    App.GoToRoot()
    App.InstallButton(OrdersDir "\main", "Clone: deploy as a new install that starts from a copy of this one", "Clone main")
    App.NewInstallName("qa")
    App.Find({Type: "ComboBox", Name: "Start from"}, "Start from")
    Runner.Say("Start from says A copy of main, so qa starts with main's files and settings, such as Region west.")
    App.ClickButton("Add", "Click Add to install qa")
    App.WaitStage("Check the files")
    App.ContinueStage()
    App.Answer("NetDeploy", "No", "Say No to the optional settings check", "Run a settings check on the old version first?")
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
        "Start from said A copy of main before the deploy.",
        "The folder list shows green, main and qa.",
    ], [
        Tool.Folder(qa, "Open qa"),
        Tool.File(qa "\appsettings.json"),
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== Deploy from the command line =====

DeployFromCommandLine() {
    qa := OrdersDir "\qa"
    zip := Package("Contoso.Orders-1.1.0")
    deploys := Machine.HistoryCount(qa)
    Runner.Step("Run NetDeploy.exe deploy Contoso.Orders-1.1.0.zip without saying which install, as a pipeline might by mistake")
    mistake := Machine.NetDeploy(App.Exe, "deploy", zip)
    Runner.Say("It changes nothing, ends with exit code " mistake.Code ", and says what is missing: " mistake.First)
    Runner.Step("Deploy 1.1.0 to qa again from the command line: NetDeploy.exe deploy Contoso.Orders-1.1.0.zip --install qa --yes, "
        . "with the payments API key in the NETDEPLOY_PaymentsApiKey environment variable, so it stays out of the command")
    EnvSet("NETDEPLOY_PaymentsApiKey", ApiKey)
    run := Machine.NetDeploy(App.Exe, "deploy", zip, "--install", "qa", "--yes")
    EnvSet("NETDEPLOY_PaymentsApiKey")
    output := Runner.ResultsDir "\command-line.txt"
    FileAppend(mistake.Output "`r`n`r`n" run.Output, output, "UTF-8")
    Runner.Say("It printed the deploy's log as it went, and ended with exit code " run.Code ".")
    App.GoToRoot()
    App.Refresh("Click Refresh, so NetDeploy reads the folder and its history again")
    App.GoInto(qa)
    App.Hover({Type: "Text", Name: "1.1.0 deployed"}, "HISTORY starts with the deploy the command line just made")
    Runner.Checkpoint("qa was deployed from the command line", [
        Check.Make("Without --install it stopped with exit code 2 and asked for one",
            () => mistake.Code = 2 && InStr(mistake.Output, "--install") ? "" : "exit code " mistake.Code ": " mistake.First),
        Check.Make("The deploy ended with exit code 0 and said Deploy finished.",
            () => run.Code = 0 && InStr(run.Output, "Deploy finished.") ? "" : "exit code " run.Code),
        Check.Make("The API key never showed in what it printed", () => InStr(run.Output, ApiKey) ? "it is there" : ""),
        Check.Make("The deploy was added to the history", () => (now := Machine.HistoryCount(qa)) = deploys + 1 ? "" : "it has " now " deploys to qa, not " deploys + 1),
        Check.Service("Contoso.Orders-qa", "Running"),
        Check.Json(qa "\appsettings.json", "Payments:ApiKey", ApiKey),
        Check.Json(qa "\appsettings.json", "Orders:Region", "west"),
    ], [
        "What the command printed is in command-line.txt: the log, then the log file's path at the end.",
        "The newest deploy under HISTORY is by you, a minute ago.",
    ], [
        Tool.File(output, "Open what it printed"),
        Tool.Folder(qa, "Open qa"),
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== Change your mind while removing an install =====

StopRemoving() {
    qa := OrdersDir "\qa"
    App.GoToRoot()
    App.RemoveInstall(qa, (*) => "stop")
    Runner.Checkpoint("qa is just as it was", [
        Check.Service("Contoso.Orders-qa", "Running"),
        Check.Task("\Contoso\Orders Export-qa"),
        Check.Exists(qa "\Contoso.Orders.exe"),
        Check.Exists(OrdersData "\db-qa\orders.db"),
    ], [
        "The removal window said what its first step would do, and nothing ran.",
        "The bar at the bottom says Removal stopped.",
        "qa is still in the folder list, with its green dot.",
    ], [
        Tool.Folder(qa, "Open qa"),
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== Remove an install someone copied by hand =====

RemoveHandCopy() {
    spare := OrdersDir "\spare"
    Runner.Step("Copy main's folder to a new one called spare, outside NetDeploy, as someone might on a server")
    Machine.CopyFolder(OrdersDir "\main", spare)
    App.GoToRoot()
    App.Refresh("Click Refresh above the folder list, so NetDeploy looks at the folder again")
    App.Hover(App.Find({Type: "Text", Name: "No Windows service runs from this install"}, "the question mark beside spare", , App.FolderRow(spare)),
        "spare shows up as an install, though NetDeploy didn't make it. The question mark means no service runs from it")
    ; Only the last step, deleting the folder, applies to a copy: it never had a service, a task or a database.
    App.RemoveInstall(spare, (number, total, *) => number = total ? "run" : "skip")
    Runner.Checkpoint("spare is gone, and main is untouched", [
        Check.Missing(spare),
        Check.Service("Contoso.Orders-spare", ""),
        Check.Service("Contoso.Orders-main", "Running"),
        Check.Exists(OrdersDir "\main\Contoso.Orders.exe"),
        Check.Exists(OrdersData "\db-main\orders.db"),
    ], [
        "spare showed in the folder list with v1.1.0 and a question mark.",
        "Every removal step for something spare never had was skipped, and only its folder was deleted.",
        "The Log tab lists each step that was skipped.",
    ], [
        Tool.Folder(OrdersDir, "Install folder"),
        Tool.Services("Contoso.Orders"),
    ])
}

; ===== Edit a deploy.json =====

EditConfig() {
    folder := Machine.Temp "\editor"
    config := folder "\deploy.json"
    Runner.Step("Unzip Contoso.Orders 1.1.0 into a folder, to edit its deploy.json there")
    Machine.Unzip(Package("Contoso.Orders-1.1.0"), folder)
    App.ClickButton("Open config", "Click Open config")
    App.PickFile("Open a deploy config", config, "Pick the deploy.json that was just unzipped")
    App.Tab("deploy.json")
    App.Click({Type: "Text", HelpText: "Copy the app"}, "Click the Copy the app step", "mouse")
    Runner.Say("The form shows the copy step's settings.")
    App.Click({Type: "RadioButton", Name: "JSON"}, "Switch to the text view")
    Runner.Say("The text view shows the file just as it will be saved.")
    App.Click({Type: "RadioButton", Name: "Form"}, "Back to the form")
    App.Click({Type: "Text", HelpText: "App and parameters"}, "Click App and parameters", "mouse")
    App.Type({Type: "Edit", Name: "Version"}, "", "Clear the version")
    Runner.Say("The list of problems above the form now says the version is missing.")
    App.Type({Type: "Edit", Name: "Version"}, "1.1.1", "Set the version to 1.1.1")
    App.ClickButton("Save", "Click Save")
    App.WaitHeader("Contoso.Orders 1.1.1")
    Runner.Checkpoint("The deploy.json is saved, and its folder is open as the package", [
        Check.FileHas(config, "1.1.1", "deploy.json now says version 1.1.1"),
    ], [
        "The title bar says Contoso.Orders 1.1.1 and names the unzipped folder.",
        "The problem showed while the version was empty, and went away once it was typed.",
        "deploy.json still has its $schema line, and its settings in the same order.",
    ], [
        Tool.File(config, "Open deploy.json"),
    ])
}

; ===== Look after an install folder without a package =====

ManageFolder() {
    qa := OrdersDir "\qa"
    App.ClickButton("Open folder", "Click Open folder")
    App.PickFolder("Open an install folder", OrdersDir, "Pick C:\Apps\Contoso.Orders")
    App.WaitHeader("Contoso.Orders")
    Runner.Say("Without a package, the folder is for looking after what is already there: each install, with its version.")
    App.GoInto(qa)
    App.FileButton("Contoso.Orders.exe", "Run, with arguments", "Run Contoso.Orders.exe")
    App.AnswerInputs("Run Contoso.Orders.exe", "Ask it to check its settings",
        (window) => App.Type({Type: "Edit", Name: "Arguments"}, "check", "Type check", , window))
    status := App.WaitStatus(["Contoso.Orders.exe exited with code"])
    App.Tab("Files")
    App.FileButton("README.txt", "Delete when finalized", "Mark README.txt to be deleted")
    App.ClickButton("Finalize", "Click Finalize")
    App.Answer("Finalize", "Yes", "Yes, delete it", "README.txt")
    App.WaitStatus(["Changes written."])
    App.GoInto(qa "\templates")
    Runner.Say("Going into an ordinary folder just lists its files.")
    App.GoUp()
    Runner.Checkpoint("The folder was looked after without a package", [
        Check.Make("Contoso.Orders.exe check finished without an error (exit code 0)", () => InStr(status, "code 0.") ? "" : status),
        Check.Missing(qa "\README.txt"),
    ], [
        "The folder list showed green, main and qa, each on v1.1.0.",
        "The Log tab shows what the settings check printed, ending Check passed.",
        "The title bar says Contoso.Orders and C:\Apps\Contoso.Orders, and there is no PACKAGE section.",
    ], [
        Tool.Folder(qa, "Open qa"),
    ])
}

; ===== Pick up where you left off =====

PickUpWhereYouLeftOff() {
    App.Start()
    App.Find({Type: "Text", Name: "RECENT"}, "the Recent list on the start screen")
    Runner.Say("NetDeploy starts afresh, and the start screen lists what was opened lately: the packages, the install folder and the deploy.json's folder.")
    App.Click({Type: "Button", Name: "Contoso.Orders-1.1.0.zip"}, "Click Contoso.Orders-1.1.0.zip in the Recent list, to open it again")
    App.WaitHeader("Contoso.Orders 1.1.0")
    folder := App.Find({Type: "Edit", Name: "Install folder"}, "the Install folder box").Value
    Runner.Say("The install folder is the one it was last deployed to, " folder ", without typing it again.")
    App.GoToRoot()
    App.Hover(App.Find({Type: "Text", Name: "The package is the same version (1.1.0)."}, "the sign beside main's version", , App.FolderRow(OrdersDir "\main")),
        "main is on 1.1.0 already, so the sign beside its version says the package is the same version")
    App.GoInto(OrdersDir "\green")
    green := App.Selection({Type: "ComboBox", Name: "Environment"}, "the Environment box")
    Runner.Say("green started out as blue, for Production, and Environment says " green ".")
    App.GoToRoot()
    App.GoInto(OrdersDir "\main")
    main := App.Selection({Type: "ComboBox", Name: "Environment"}, "the Environment box")
    App.Hover({Type: "Text", Name: "Reinstall main"}, "Environment says " main " for main. Below the folders, the plan says Reinstall, as main is already on this version")
    Runner.Checkpoint("NetDeploy remembered what was opened and how each install was set up", [
        Check.Make("The install folder was filled in as " OrdersDir, () => folder = OrdersDir ? "" : "it was " folder),
        Check.Make("Environment said Production for green", () => green = "Production" ? "" : "it said " green),
        Check.Make("Environment said Staging for main", () => main = "Staging" ? "" : "it said " main),
        Check.Make("The remembered settings are in your own settings, not beside NetDeploy.exe or in an install",
            () => !FileExist(Machine.SettingsFile) ? "there is no " Machine.SettingsFile
                : (Machine.Count(OrdersDir, "settings.json", "FR") + Machine.Count(App.ExeFolder, "settings.json")) ? "a settings.json is beside NetDeploy.exe or in an install" : ""),
    ], [
        "The Recent list showed the packages, C:\Apps\Contoso.Orders and the editor folder, newest first.",
        "The payments API key box is empty: secrets are never remembered.",
    ], [
        Tool.File(Machine.SettingsFile, "Open the settings"),
    ])
}

; ===== Install Fabrikam.Reports =====

AddReports() {
    main := ReportsDir "\main"
    App.OpenPackage(Package("Fabrikam.Reports-2.0.0"))
    App.WaitHeader("Fabrikam.Reports 2.0.0")
    App.NewInstallName("main")
    App.ClickButton("Add", "Click Add to install Fabrikam.Reports")
    App.WaitStage("Check the files")
    App.ContinueStage()
    App.WaitStage("Check the config")
    App.ConfigFile("Fabrikam.Reports.exe.config")
    App.ReplaceText('(key="SmtpPassword"\s+value=)""', '$1"smtp-test-123"', "Fill in SmtpPassword: it's empty in the package, so it needs a value")
    App.ConfigFile("web.config")
    Runner.Say("An older-style .NET Framework app keeps its settings in .exe.config, and its website's in web.config. Both are new here.")
    App.ContinueStage()
    App.Answer("NetDeploy", "Yes", "Say Yes to running a report now", "Run a report now to check the install?", 120000)
    App.AnswerInputs("Schedule the hourly report", "Keep SYSTEM as the account the hourly report runs as",
        (window) => App.Choose({Type: "ComboBox", Name: "Run the hourly report as"}, "SYSTEM", "Pick SYSTEM", window))
    log := App.LogFile(App.WaitStatus(["Deploy finished."], 300000))
    Runner.Checkpoint("Fabrikam.Reports is installed", [
        Check.Exists(main "\data\sales.csv"),
        Check.Count(main "\reports", "*.pdf", 1),
        Check.Task("\Fabrikam\Reports hourly"),
        Check.Logged(log, "The report site is not published"),
    ], [
        "The question came up once, and then the window asking for the account, in that order.",
        "In Task Scheduler, \Fabrikam\Reports hourly runs every hour as SYSTEM.",
    ], [
        Tool.Folder(main, "Open main"),
        Tool.Tasks("\Fabrikam"),
        Tool.File(log, "Open the log"),
    ])
}

; ===== Upgrade Fabrikam.Reports =====

UpgradeReports() {
    main := ReportsDir "\main"
    config := main "\Fabrikam.Reports.exe.config", web := main "\wwwroot\web.config"
    Runner.Step("Change three settings on the server by hand: ReportTitle, KeepReports and the website's AllowDownload")
    Machine.SetAppSetting(config, "ReportTitle", "Fabrikam weekly sales")
    Machine.SetAppSetting(config, "KeepReports", "45")
    Machine.SetAppSetting(web, "AllowDownload", "false")
    App.OpenPackage(Package("Fabrikam.Reports-2.1.0"))
    App.WaitHeader("Fabrikam.Reports 2.1.0")
    App.SetCheck({Type: "CheckBox", Name: "Publish the report site"}, true, "Tick Publish the report site")
    App.ClickButton("Update", "Click Update to upgrade Fabrikam.Reports")
    App.WaitStage("Check the files")
    App.Hover(App.FileName("plugins/Fabrikam.Reports.Pdf.dll"), "The PDF plugin will be deleted")
    App.Hover(App.FileName("plugins/Fabrikam.Reports.Excel.dll"), "and the Excel one added")
    App.ContinueStage()
    App.WaitStage("Check the config")
    App.ConfigFile("Fabrikam.Reports.exe.config")
    Runner.Say("ReportTitle keeps the server's text. KeepReports always takes the package's 90. MaxRows is new, and LegacyPdfEngine is gone.")
    App.ConfigFile("web.config")
    Runner.Say("web.config keeps the server's AllowDownload, and gets the new PageSize.")
    App.ContinueStage()
    App.Answer("NetDeploy", "Yes", "Say Yes to running a report now", "Run a report now to check the install?", 120000)
    App.AnswerInputs("Schedule the hourly report", "Keep SYSTEM as the account")
    log := App.LogFile(App.WaitStatus(["Deploy finished."], 300000))
    Runner.Checkpoint("Fabrikam.Reports is on 2.1.0, with the server's changes kept", [
        Check.Logged(log, "left as it is"),
        Check.Logged(log, "plugins: csv, xlsx"),
        Check.Logged(log, "Check passed."),
        Check.Missing(main "\plugins\Fabrikam.Reports.Pdf.dll"),
        Check.Count(main "\reports", "*.xlsx", 1),
        Check.AppSetting(config, "ReportTitle", "Fabrikam weekly sales"),
        Check.AppSetting(config, "KeepReports", "90"),
        Check.AppSetting(config, "LegacyPdfEngine", "<absent>"),
        Check.ConnectionString(config, "Audit"),
        Check.FileHas(config, 'newVersion="13.0.0.0"', "The library version it points to comes from the new file"),
        Check.AppSetting(web, "AllowDownload", "false"),
        Check.AppSetting(web, "PageSize", "25"),
    ], [
        "The list of files showed the PDF plugin deleted and the Excel one added.",
        "The reports folder has the first report as a .pdf, and the new one as an .xlsx.",
    ], [
        Tool.File(config, "Open .exe.config"),
        Tool.File(web, "Open web.config"),
        Tool.Folder(main "\reports", "Open reports"),
        Tool.File(log, "Open the log"),
    ])
}

; ===== Remove every install =====

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
        "Each removal went through its steps one at a time, the last thing installed first, ending with deleting the folder.",
        "The folder list is empty apart from the New install row.",
    ], [
        Tool.Folder("C:\Apps", "Open C:\Apps"),
        Tool.Services("Contoso.Orders"),
        Tool.App("Task Scheduler", "taskschd.msc"),
    ])
    App.Close()
}
