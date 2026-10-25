# What Contoso.Orders.exe ($args[0]) says of the database $args[1], for a checkpoint's Database button.
$exe, $database = $args
Write-Output "Database: $database"
& $exe migrate --db $database
Get-Item -LiteralPath $database | Format-List FullName, Length, LastWriteTime
