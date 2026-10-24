# Production only: logs what a real deploy would lock down, and leaves a marker so the run is easy to spot.
param([Parameter(Mandatory)] [string] $InstallDir)
Write-Output "Production: would restrict $InstallDir to Administrators and the service account."
Set-Content -Path (Join-Path $InstallDir "PRODUCTION.txt") -Value "Hardened on $(Get-Date -Format s)"
