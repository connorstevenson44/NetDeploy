param([Parameter(Mandatory)] [string] $InstallDir)

$link = Join-Path $InstallDir 'archive'
if (-not (Test-Path $link)) {
    New-Item -ItemType Junction -Path $link -Target 'D:\OrdersArchive' | Out-Null
    Write-Output "Linked $link to D:\OrdersArchive"
}
