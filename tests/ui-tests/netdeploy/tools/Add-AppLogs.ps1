# Gives the install $args[0] an nlog.config, as an app that logs with NLog has, pointing at the heartbeat log its
# service writes, and two older logs in the archive folder it names, one of them zipped as NLog zips them.
$main = $args[0]
$archive = Join-Path $main 'logs\archive'
New-Item -ItemType Directory -Force -Path $archive | Out-Null
Set-Content -LiteralPath (Join-Path $main 'nlog.config') -Encoding UTF8 -Value @'
<?xml version="1.0" encoding="utf-8"?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <targets>
    <target xsi:type="File" name="heartbeat" fileName="${basedir}/logs/heartbeat.log" archiveFileName="${basedir}/logs/archive/heartbeat.{#}.log"
            archiveAboveSize="1048576" maxArchiveFiles="5" enableArchiveFileCompression="true" />
  </targets>
  <rules><logger name="*" minlevel="Info" writeTo="heartbeat" /></rules>
</nlog>
'@
Set-Content -LiteralPath (Join-Path $archive 'heartbeat.1.log') -Encoding UTF8 -Value @(
    '2026-10-20 09:00:00 1.0.0 region West | ORD-2001 payment authorised',
    '2026-10-20 09:00:10 1.0.0 region West | ORD-2002 shipped')
$zipped = Join-Path $env:TEMP 'heartbeat.2.log'
Set-Content -LiteralPath $zipped -Encoding UTF8 -Value @(
    '2026-10-19 09:00:00 1.0.0 region West | ORD-1042 payment declined',
    '2026-10-19 09:00:10 1.0.0 region West | ORD-1042 payment declined again')
Compress-Archive -LiteralPath $zipped -DestinationPath (Join-Path $archive 'heartbeat.2.zip') -Force
Remove-Item -LiteralPath $zipped
