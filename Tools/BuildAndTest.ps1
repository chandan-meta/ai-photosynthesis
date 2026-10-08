<#
  Build + automated test loop for the Photosynthesis module (Scene 2 onward).
    .\Tools\BuildAndTest.ps1                 # build scene + player, run beat screenshots
    .\Tools\BuildAndTest.ps1 -Autoplay       # also play the whole module unattended (and with wrong answers first)
    .\Tools\BuildAndTest.ps1 -SkipBuild      # reuse the last player
  Output: Logs\build-*.log, Logs\shots\*.png, Logs\player-*.log, summary of errors.
#>
param([switch]$SkipBuild, [switch]$Autoplay, [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe")
$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent
$logs = Join-Path $proj 'Logs'; New-Item -ItemType Directory -Force $logs | Out-Null
$exe = Join-Path $proj 'Builds\Win\Photosynthesis.exe'

function Run-Unity($method, $log) {
  $p = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-projectPath', "`"$proj`"", '-executeMethod', $method, '-logFile', "`"$log`"", '-quit') -PassThru -Wait
  $errs = Select-String -Path $log -Pattern 'error CS\d+|Exception|\[Photosynthesis\]|Build Finished|Scripts have compiler errors' | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique -First 40
  "== $method exit $($p.ExitCode)"; $errs
  return $p.ExitCode
}

if (-not $SkipBuild) {
  Run-Unity 'Photosynthesis.EditorTools.PhotosynthesisBuild.BuildAll' (Join-Path $logs 'build-scene.log')
  Run-Unity 'Photosynthesis.EditorTools.PhotosynthesisBuild.BuildPlayer' (Join-Path $logs 'build-player.log')
}
if (-not (Test-Path $exe)) { throw "Player not built: $exe" }

function Run-Player($args2, $log, $timeout = 900) {
  $a = @('-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '0', '-logFile', "`"$log`"") + $args2
  $p = Start-Process -FilePath $exe -ArgumentList $a -PassThru
  if (-not $p.WaitForExit($timeout * 1000)) { $p.Kill(); "TIMEOUT after $timeout s" }
  $lines = Select-String -Path $log -Pattern 'Exception|Error|\[Photosynthesis\]|NullReference' | ForEach-Object { $_.Line.Trim() } | Group-Object | Sort-Object Count -Descending | Select-Object -First 30 | ForEach-Object { "$($_.Count)x $($_.Name)" }
  "== player $($args2 -join ' ')"; $lines
}

$shots = Join-Path $logs 'shots'; Remove-Item -Recurse -Force $shots -ErrorAction SilentlyContinue
Run-Player @('-shots', "`"$shots`"") (Join-Path $logs 'player-shots.log') 600
if ($Autoplay) {
  Run-Player @('-autoplay') (Join-Path $logs 'player-autoplay.log') 1500
  Run-Player @('-autoplay', '-wrongfirst') (Join-Path $logs 'player-autoplay-wrong.log') 1800
}
Get-ChildItem $shots -Filter *.png | Select-Object Name, Length | Format-Table -AutoSize
