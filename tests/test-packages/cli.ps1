# Deploys a test package with NetDeploy.exe's command line, as a pipeline would, and checks each result. Windows only,
# as administrator; run it after e2e, which leaves the machine clean.
#   ./tests/test-packages/cli.ps1 -Exe artifacts/portable/NetDeploy.exe -Dist tests/test-packages/dist
param([Parameter(Mandatory)] [string] $Exe, [Parameter(Mandatory)] [string] $Dist)

$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path $Exe).Path
$package = Join-Path (Resolve-Path $Dist).Path 'Contoso.Orders-1.0.0.zip'
$installDir = 'C:\Apps\Cli.Orders'
$values = '--param', "InstallDir=$installDir", '--param', 'DataDir=C:\ProgramData\Cli.Orders'
$failures = @()

# NetDeploy.exe is a windowed app: piping its output is what makes PowerShell wait for it and set $LASTEXITCODE.
function Invoke-NetDeploy {
    $output = & $exe @args | Out-String
    Write-Output $output
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = $output }
}

function Expect([bool] $condition, [string] $what) {
    Write-Output "$(if ($condition) { 'PASS' } else { 'FAIL' }): $what"
    if (-not $condition) { $script:failures += $what }
}

Write-Output "`n===== A deploy with no package"
$run = Invoke-NetDeploy deploy
Expect ($run.Code -eq 2 -and $run.Output -match 'Name the package') 'a mistake in the command exits with 2 and says what is missing'

Write-Output "`n===== A deploy with no install name"
$run = Invoke-NetDeploy deploy $package @values
Expect ($run.Code -eq 2 -and $run.Output -match '--install') 'a package with an install block needs --install'

Write-Output "`n===== A dry run"
$run = Invoke-NetDeploy deploy $package @values --install main --yes --dry-run
Expect ($run.Code -eq 0 -and $run.Output -match 'Dry run finished') 'the dry run finishes'
Expect (-not (Test-Path $installDir)) 'the dry run creates nothing'

Write-Output "`n===== A deploy"
$run = Invoke-NetDeploy deploy $package @values --install main --yes
Expect ($run.Code -eq 0 -and $run.Output -match 'Deploy finished') 'the deploy finishes'
Expect ((Get-Service 'Contoso.Orders-main' -ErrorAction SilentlyContinue).Status -eq 'Running') "the install's service runs"
$log = [regex]::Match($run.Output, 'Log: (.+)').Groups[1].Value.Trim()
Expect ($log -and (Test-Path $log)) 'the log file is where the output says'
$history = Get-ChildItem "$env:ProgramData\NetDeploy\history\*.json" -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_ -Raw | ConvertFrom-Json }
Expect (@($history | Where-Object { $_.folder -eq "$installDir\main" -and $_.result -eq 'Succeeded' -and $_.version -eq '1.0.0' }).Count -eq 1) 'the deploy is in the history'

if ($failures.Count -eq 0) { Write-Output "`nALL CLI CHECKS PASSED"; exit 0 }
Write-Output "`n$($failures.Count) CLI CHECK(S) FAILED:`n  $($failures -join "`n  ")"
exit 1
