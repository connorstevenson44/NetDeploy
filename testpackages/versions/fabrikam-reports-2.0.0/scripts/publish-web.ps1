# Lists what publishing the report site would do. It changes nothing, so it is safe on any test machine.
param([Parameter(Mandatory)] [string] $SiteFolder)
Write-Output "Would publish $SiteFolder as the IIS application /reports."
Get-ChildItem -Path $SiteFolder | ForEach-Object { Write-Output "  $($_.Name)" }
