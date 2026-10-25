# Removes everything the NetDeploy test packages install, so the manual tests can start from a clean computer:
# the Contoso.Orders services, the \Contoso and \Fabrikam scheduled tasks, the install folders and the databases, and
# what NetDeploy remembers of them: their deploy history, and the settings and recent files it keeps for you.
# Run it in PowerShell as administrator. Walkthrough runs it before a run when an earlier one left something behind.
$ErrorActionPreference = 'Continue'

Get-CimInstance Win32_Service -Filter "Name LIKE 'Contoso.Orders%'" | ForEach-Object {
    Stop-Service -Name $_.Name -Force -ErrorAction SilentlyContinue
    sc.exe delete $_.Name | Out-Null
    Write-Output "Removed the service $($_.Name)."
}

foreach ($folder in '\Contoso\', '\Fabrikam\') {
    Get-ScheduledTask -TaskPath $folder -ErrorAction SilentlyContinue | ForEach-Object {
        Unregister-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath -Confirm:$false
        Write-Output "Removed the task $($_.TaskPath)$($_.TaskName)."
    }
}

# The services may take a moment to let go of their files.
foreach ($folder in 'C:\Apps\Contoso.Orders', 'C:\Apps\Fabrikam.Reports', 'C:\ProgramData\Contoso.Orders',
        (Join-Path $env:TEMP 'NetDeployManualTests\editor')) {
    for ($try = 1; (Test-Path -LiteralPath $folder) -and $try -le 5; $try++) {
        Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $folder) { Start-Sleep -Seconds 2 } else { Write-Output "Removed $folder." }
    }
}

# The deploy history of the test installs, one file per deploy. Other deploys on this computer stay.
$testFolder = '^C:\\Apps\\(Contoso\.Orders|Fabrikam\.Reports)(\\|$)'
Get-ChildItem -Path "$env:ProgramData\NetDeploy\history\*.json" -ErrorAction SilentlyContinue | ForEach-Object {
    $record = try { Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json } catch { $null }
    if ($record -and $record.folder -match $testFolder) {
        Remove-Item -LiteralPath $_.FullName -Force
        Write-Output "Removed the history of a deploy to $($record.folder)."
    }
}

# The settings the test apps were last deployed with, and the test packages and folders in the recent list.
$settingsFile = "$env:LOCALAPPDATA\NetDeploy\settings.json"
if (Test-Path -LiteralPath $settingsFile) {
    try {
        $settings = Get-Content -Raw -LiteralPath $settingsFile | ConvertFrom-Json
        foreach ($app in 'Contoso.Orders', 'Fabrikam.Reports') {
            if ($settings.apps -and $settings.apps.PSObject.Properties[$app]) { $settings.apps.PSObject.Properties.Remove($app) }
        }
        $settings.recent = @($settings.recent | Where-Object { $_.path -notmatch '(Contoso\.Orders|Fabrikam\.Reports)[^\\]*$|\\NetDeployManualTests\\' })
        $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settingsFile -Encoding UTF8
        Write-Output "Removed what NetDeploy remembered of the test apps from $settingsFile."
    } catch {
        Write-Output "Couldn't read $settingsFile, so it was left as it is: $($_.Exception.Message)"
    }
}
Write-Output 'Done.'
