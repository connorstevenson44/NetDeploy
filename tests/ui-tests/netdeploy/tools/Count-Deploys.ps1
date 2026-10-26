# How many deploys NetDeploy's history ($args[1]) has for the install folder $args[0]. Given an earlier count as
# $args[2], how many have been added since.
$folder, $history, $since = $args
$count = @(Get-ChildItem -Path "$history\*.json" -ErrorAction SilentlyContinue |
    ForEach-Object { try { Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json } catch { } } |
    Where-Object { $_.folder -eq $folder }).Count
$count - [int]$since
