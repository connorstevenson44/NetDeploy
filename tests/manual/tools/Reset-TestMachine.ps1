# Removes everything the NetDeploy test packages install, so the manual tests can start from a clean computer:
# the Contoso.Orders services, the \Contoso and \Fabrikam scheduled tasks, the install folders and the databases.
# Run it in PowerShell as administrator. The manual test runner's Clean up button runs it too.
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
Write-Output 'Done.'
