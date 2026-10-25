# Lists what the NetDeploy test packages left on this computer, one line each: services, scheduled tasks, folders,
# and what NetDeploy remembers of them. Prints nothing on a clean computer. AutoTestRunner runs it before a run to decide
# whether to clean up first.
$ErrorActionPreference = 'SilentlyContinue'

Get-CimInstance Win32_Service -Filter "Name LIKE 'Contoso.Orders%'" | ForEach-Object { "service $($_.Name)" }

foreach ($folder in '\Contoso\', '\Fabrikam\') {
    Get-ScheduledTask -TaskPath $folder | ForEach-Object { "task $($_.TaskPath)$($_.TaskName)" }
}

foreach ($folder in 'C:\Apps\Contoso.Orders', 'C:\Apps\Fabrikam.Reports', 'C:\ProgramData\Contoso.Orders') {
    if (Test-Path -LiteralPath $folder) { "folder $folder" }
}

# What NetDeploy remembers of the test apps would change what a run sees, such as the settings a package opens with.
$settings = "$env:LOCALAPPDATA\NetDeploy\settings.json"
if ((Test-Path -LiteralPath $settings) -and ((Get-Content -Raw -LiteralPath $settings) -match '"(Contoso\.Orders|Fabrikam\.Reports)"\s*:')) {
    "NetDeploy's remembered settings for the test apps"
}

$history = Get-ChildItem -Path "$env:ProgramData\NetDeploy\history\*.json" |
    Where-Object { (Get-Content -Raw -LiteralPath $_.FullName) -match 'Apps\\\\(Contoso\.Orders|Fabrikam\.Reports)' } |
    Select-Object -First 1
if ($history) { "the test installs' deploy history" }
