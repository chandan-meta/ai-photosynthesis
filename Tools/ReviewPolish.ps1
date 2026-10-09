param([string]$Iteration = 'iteration2', [switch]$Full)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$player = Join-Path $project 'Builds/Win/Photosynthesis.exe'
$output = Join-Path $project "Logs/Polish/$Iteration"
New-Item -ItemType Directory -Force $output | Out-Null
function Run-Review([string]$name, [string]$flags, [int]$width = 1920, [int]$height = 1080, [int]$timeout = 1200) {
    $log = Join-Path $output "$name.log"
    # Visible player is the actual interactive surface under review; hide/minimize would invalidate screenshots.
    $arguments = "-screen-width $width -screen-height $height -screen-fullscreen 0 -logFile `"$log`" $flags"
    $process = Start-Process -FilePath $player -ArgumentList $arguments -PassThru
    if (-not $process.WaitForExit($timeout * 1000)) { $process.Kill(); throw "Review $name timed out" }
    $summary = Select-String -LiteralPath $log -Pattern '\[Polish\].*PASS|complete, errors=|touchtest:|quitafter reached|Exception|Not allowed to access|error CS|Shader.*not supported' | ForEach-Object Line
    "$name exit=$($process.ExitCode)"; $summary
    if ($process.ExitCode -ne 0) { throw "Review $name exited $($process.ExitCode)" }
}
Run-Review 'banner1080' "-bannertest `"$output/banner1080`""
Run-Review 'banner4x3' "-bannertest `"$output/banner4x3`" -reducemotion" 1024 768
Run-Review 'shots' "-shots `"$output/shots`"" 1920 1080 400
if ($Full) {
    Run-Review 'science' '-sciencecheck -beat Reaction -quitafter 4' 1920 1080 30
    Run-Review 'autoplay' '-autoplay -testspeed 4'
    Run-Review 'wrongfirst' '-autoplay -wrongfirst -testspeed 4'
    Run-Review 'uishots720' "-uishots `"$output/uishots720`"" 1280 720 150
    Run-Review 'touch' '-touchtest' 1366 768 60
    Run-Review 'voicefilm' "-autoplay -realvoice -film `"$output/voicefilm`" -filmrate 5" 1920 1080 1500
    Run-Review 'performance' '-autoplay -testspeed 4 -perf -quality Performance' 1366 768
}
