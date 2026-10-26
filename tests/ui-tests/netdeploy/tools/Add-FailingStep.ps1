# Turns the unzipped Contoso.Orders 1.1.0 deploy.json at $args[0] into 1.2.0 with a step that always fails, just before
# the service is started again: by then the service is stopped, a backup taken and the new files copied.
$manifest = $args[0]
$text = Get-Content -Raw -LiteralPath $manifest
$start = '{ "type": "service", "action": "start", "service": "Contoso.Orders{InstanceSuffix}" }'
if (-not $text.Contains($start) -or -not $text.Contains('"version": "1.1.0"')) { throw "$manifest doesn't have the version or the step that starts the service" }
$failing = '{ "type": "powershell", "name": "Warm up the cache", "script": "Write-Output ''Calling the cache server''; throw ''The cache server did not answer.''" },'
$text = $text.Replace('"version": "1.1.0"', '"version": "1.2.0"').Replace($start, $failing + [Environment]::NewLine + '    ' + $start)
[IO.File]::WriteAllText($manifest, $text)
