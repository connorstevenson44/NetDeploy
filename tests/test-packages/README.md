# Test packages

Two apps, each in two versions, that between them use every kind of step and config file NetDeploy handles.
Build them with `dotnet run tests/test-packages/build.cs`; the zips land in `tests/test-packages/dist`. CI builds them on every
run (the TestPackages artifact) and runs `e2e`, which deploys, upgrades and removes both on a Windows machine and
checks each result.

## Contoso.Orders 1.0.0 → 1.1.0

A self-contained .NET 10 Windows service, installed twice side by side (`main` and `blue`).

| Area | What it exercises |
| --- | --- |
| Database | `Contoso.Orders.exe migrate --db <file> --seed` creates a SQLite database outside the install folder. 1.1 adds a table and a column. Removing the install deletes it with `cleanup` PowerShell (1.1), or with cleanup steps, the older form (1.0). |
| appsettings.json | 1.1 renames `Smtp:Server` to `Smtp:Host`, removes `LegacyMode`, adds `Payments` and `Features`, changes `BatchSize`, and takes the new `Logging` (`takeNew`). |
| appsettings.Production.json | In 1.0 only, so after the upgrade it is a config file that only the server has. |
| .exe.config | `export\Contoso.Orders.Export.exe` is a .NET Framework 4.8 tool. 1.1 changes and removes appSettings and adds a connection string. |
| Files | Templates are added, changed and removed (`deleteExtra`). `logs` and `exports` are preserved. |
| Steps | PowerShell prerequisite, service stop/install/start, backup, copy, a PowerShell script with arguments, runExe, a daily scheduled task, if/else on a choice, a `when` question, a secret parameter (1.1). |
| Installs | Service and task names use `{InstanceSuffix}`; each install gets its own folder and database. |

## Fabrikam.Reports 2.0.0 → 2.1.0

A .NET Framework 4.8 app that reads `Fabrikam.Reports.exe.config` (app.config), with plugins in `plugins\`. One install, `main`.

| Area | What it exercises |
| --- | --- |
| .exe.config | 2.1 adds `MaxRows`, removes `LegacyPdfEngine`, adds a connection string, raises `KeepReports` (`takeNew`), and changes a binding redirect, which comes from the new file. |
| web.config | `wwwroot\web.config` gains a setting in 2.1. |
| Files | 2.1 removes the Pdf plugin and adds an Excel one (`deleteExtra`). `data` and `reports` are preserved. Templates change. |
| Steps | Registry and PowerShell prerequisites, a task disabled only `when` it exists, `--init` that keeps existing data, if on a question, an hourly task with an input for its account, if/else on a yes/no parameter and IIS, `--check` (2.1). |

Things to try by hand: edit a setting on the server before upgrading, add an install, rename one, clone one, open
the install folder in the folder manager, and remove an install.
