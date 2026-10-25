# Opens the manual test runner's launcher and checks it fits the screen: the whole window on the work area, and the
# Start and Close buttons in it, as it opens and once made as small as it goes. A screenshot of each is saved in $Out.
# CI runs it at a few screen sizes, since a small screen at 100% lays out like a laptop's at 125% or 150%.
#   Check-Launcher.ps1 -AutoHotkey <AutoHotkey64.exe> -Out <folder> -Name 1280x720
param(
    [Parameter(Mandatory)] [string] $AutoHotkey,
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Name
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$element = [Windows.Automation.AutomationElement]

function Find($parent, $name, $scope) {
    $parent.FindFirst($scope, [Windows.Automation.PropertyCondition]::new($element::NameProperty, $name))
}

function Inside($inner, $outer) {
    $inner.Left -ge $outer.Left -and $inner.Top -ge $outer.Top -and $inner.Right -le $outer.Right -and $inner.Bottom -le $outer.Bottom
}

# Fails when the window or one of its buttons is off the work area, after saving what the screen showed.
function Check($window, $shot) {
    Start-Sleep -Milliseconds 500
    $work = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $workRect = [Windows.Rect]::new($work.X, $work.Y, $work.Width, $work.Height)
    $bounds = $window.Current.BoundingRectangle
    $picture = New-Object Drawing.Bitmap ([int]$bounds.Width), ([int]$bounds.Height)
    [Drawing.Graphics]::FromImage($picture).CopyFromScreen([int]$bounds.Left, [int]$bounds.Top, 0, 0, $picture.Size)
    New-Item $Out -ItemType Directory -Force | Out-Null
    $picture.Save((Join-Path $Out "launcher-$shot.png"))
    $problems = @()
    if (-not (Inside $bounds $workRect)) { $problems += "the window ($bounds) is off the work area ($workRect)" }
    foreach ($button in 'Start', 'Close') {
        $found = Find $window $button ([Windows.Automation.TreeScope]::Descendants)
        if (-not $found) { $problems += "there is no $button button"; continue }
        $rect = $found.Current.BoundingRectangle
        if (-not (Inside $rect $bounds) -or -not (Inside $rect $workRect)) { $problems += "the $button button ($rect) doesn't all show" }
    }
    if ($problems) { throw "The launcher at $shot doesn't fit: $($problems -join '; ')." }
    Write-Output "The launcher at $shot fits: the window is $($bounds.Width)x$($bounds.Height) on a $($work.Width)x$($work.Height) work area."
}

$ahk = Start-Process $AutoHotkey -ArgumentList "`"$PSScriptRoot\..\NetDeployManualTests.ahk`"" -PassThru
try {
    $end = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $window = Find $element::RootElement 'NetDeploy manual tests' ([Windows.Automation.TreeScope]::Children)
    } until ($window -or (Get-Date) -gt $end)
    if (-not $window) { throw "The launcher didn't open." }
    Check $window $Name
    # Asking for 1x1 makes it as small as the window lets it go.
    $window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize(1, 1)
    Check $window "$Name-smallest"
}
finally {
    Stop-Process $ahk -Force -ErrorAction SilentlyContinue
}
