# Points the service at its database, and stores the payments key when one is given. Only blank or default values
# are filled in, so settings someone changed on the server are left alone.
param(
    [Parameter(Mandatory)] [string] $Settings,
    [Parameter(Mandatory)] [string] $Database,
    [string] $ApiKey = ""
)
$json = Get-Content -Raw -Path $Settings | ConvertFrom-Json
$connection = "Data Source=$Database"
if (-not $json.ConnectionStrings.Orders -or $json.ConnectionStrings.Orders -eq "Data Source=orders.db") {
    $json.ConnectionStrings.Orders = $connection
    Write-Output "ConnectionStrings:Orders = $connection"
}
if ($ApiKey -and $json.PSObject.Properties.Name -contains "Payments" -and -not $json.Payments.ApiKey) {
    $json.Payments.ApiKey = $ApiKey
    Write-Output "Payments:ApiKey stored."
}
$json | ConvertTo-Json -Depth 10 | Set-Content -Path $Settings -Encoding UTF8
