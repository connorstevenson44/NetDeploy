# The scheduled tasks in the folder $args[0], such as \Contoso\, for a checkpoint's Scheduled tasks button.
Get-ScheduledTask -TaskPath $args[0] -ErrorAction SilentlyContinue | ForEach-Object {
    $info = $_ | Get-ScheduledTaskInfo
    [pscustomobject]@{ Task = $_.TaskPath + $_.TaskName; State = $_.State; RunAs = $_.Principal.UserId
        Runs = ($_.Actions | ForEach-Object { $_.Execute + ' ' + $_.Arguments }) -join '; '; Next = $info.NextRunTime }
} | Format-List
