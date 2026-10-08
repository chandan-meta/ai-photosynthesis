<#
  Generates Dr. Curio's voice-over clips with Windows SAPI (System.Speech, "Microsoft David Desktop").
    .\Tools\GenerateVO.ps1                          # writes Assets/Photosynthesis/Resources/VO/<id>.wav for lines without a clip
    .\Tools\GenerateVO.ps1 -Force                   # regenerate every clip
    .\Tools\GenerateVO.ps1 -Only s3_good,s3_intro   # just these ids
    .\Tools\GenerateVO.ps1 -Out C:\temp\vo          # write somewhere else (tests)
  Input: Assets/Photosynthesis/Resources/curio_lines.txt  (id|text, '#' comments). Output: 22050 Hz, 16-bit, mono WAV.
  Delivery: playful, energetic investigator - SSML pitch "high" (~+6 %), rate +8 %, natural pauses on '...' and dashes.
#>
param(
  [switch]$Force,
  [string[]]$Only,
  [string]$Lines,
  [string]$Out,
  [string]$Voice = 'Microsoft David Desktop'
)
$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent
if (-not $Lines) { $Lines = Join-Path $proj 'Assets/Photosynthesis/Resources/curio_lines.txt' }
if (-not $Out) { $Out = Join-Path $proj 'Assets/Photosynthesis/Resources/VO' }
# .NET (File IO, SAPI) resolves relative paths against the process directory, not PowerShell's current location.
$Lines = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Lines)
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
if (-not (Test-Path $Lines)) { throw "Lines file not found: $Lines" }
# "-Only a,b" arrives as one string when run via powershell.exe -File (cmd, build scripts): split it ourselves.
if ($Only) { $Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
New-Item -ItemType Directory -Force $Out | Out-Null

# Text -> SSML. Non-ASCII characters are written as code points so this file stays plain ASCII (PowerShell 5.1 safe).
function ConvertTo-Ssml([string]$text) {
  $t = $text.Trim().TrimStart([char]0x2026, '.', ' ')          # a leading ellipsis continues the previous line: no pause
  $t = [Security.SecurityElement]::Escape($t)
  $sb = New-Object Text.StringBuilder
  foreach ($c in $t.ToCharArray()) {
    $code = [int]$c
    if ($code -ge 0x2080 -and $code -le 0x2089) { [void]$sb.Append([char](48 + $code - 0x2080)) }   # CO2, H2O, C6H12O6
    elseif ($code -eq 0x2026) { [void]$sb.Append('<break time="420ms"/>') }                       # ...
    elseif ($code -eq 0x2014 -or $code -eq 0x2013) { [void]$sb.Append('<break time="250ms"/>') }   # dashes
    elseif ($code -eq 0x2192) { [void]$sb.Append(' to ') }                                          # arrow
    elseif ($code -eq 0x00D7) { [void]$sb.Append(' times ') }
    elseif ($code -eq 0x2713 -or $code -eq 0x2715 -or $code -eq 0x2022) { [void]$sb.Append(' ') }  # check / cross / bullet
    else { [void]$sb.Append($c) }
  }
  '<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="en-US"><prosody pitch="high" rate="+8%">' +
    $sb.ToString() + '</prosody></speak>'
}

# SAPI ends every clip with ~0.5-0.8 s of silence; Dialogue waits for the clip length, so trim it to $keep seconds.
function Limit-TrailingSilence([string]$path, [double]$keep) {
  $b = [IO.File]::ReadAllBytes($path)
  $pos = 12; $data = -1
  while ($pos + 8 -le $b.Length) {                                # walk RIFF chunks to 'data' (fmt chunk size varies)
    $size = [BitConverter]::ToInt32($b, $pos + 4)
    if ([Text.Encoding]::ASCII.GetString($b, $pos, 4) -eq 'data') { $data = $pos + 8; break }
    $pos += 8 + $size + ($size % 2)
  }
  if ($data -lt 0) { return }
  $i = $b.Length - 2
  while ($i -ge $data -and [Math]::Abs([int][BitConverter]::ToInt16($b, $i)) -lt 250) { $i -= 2 }
  $end = [Math]::Min($b.Length, $i + 2 + [int]($keep * 22050) * 2)
  if ($end -ge $b.Length) { return }
  [BitConverter]::GetBytes([int]($end - 8)).CopyTo($b, 4)
  [BitConverter]::GetBytes([int]($end - $data)).CopyTo($b, $data - 4)
  $trimmed = New-Object byte[] $end; [Array]::Copy($b, $trimmed, $end); [IO.File]::WriteAllBytes($path, $trimmed)
}

Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try { $synth.SelectVoice($Voice) } catch { Write-Warning "Voice '$Voice' is not installed - using '$($synth.Voice.Name)'." }
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050,
  [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)

$made = 0; $skipped = 0; $failed = @(); $seconds = 0.0; $seen = 0
try {
  foreach ($raw in [IO.File]::ReadAllLines($Lines, [Text.Encoding]::UTF8)) {
    $line = $raw.Trim()
    if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }
    $bar = $line.IndexOf('|'); if ($bar -le 0) { continue }
    $id = $line.Substring(0, $bar).Trim(); $text = $line.Substring($bar + 1).Trim()
    if ($Only -and ($Only -notcontains $id)) { continue }
    $seen++
    $wav = Join-Path $Out "$id.wav"
    if ((Test-Path $wav) -and -not $Force) { $skipped++; continue }
    try {
      $synth.SetOutputToWaveFile($wav, $format)
      $synth.SpeakSsml((ConvertTo-Ssml $text))
      $synth.SetOutputToNull()                                   # closes the file
      Limit-TrailingSilence $wav ($(if ($text.EndsWith([string][char]0x2026)) { 0.55 } else { 0.22 }))   # keep the dramatic '...' pause
      $len = ((Get-Item $wav).Length - 46) / (22050 * 2)
      $seconds += $len; $made++
      '{0,-22} {1,5:0.0}s  {2}' -f $id, $len, $text
    } catch {
      $synth.SetOutputToNull()
      Remove-Item $wav -ErrorAction SilentlyContinue
      $failed += "$id ($($_.Exception.Message))"
    }
  }
} finally { $synth.Dispose() }

''
"VO summary: $made generated ({0:0.0} s of audio), $skipped skipped (already exist - use -Force), $($failed.Count) failed, $seen lines considered." -f $seconds
"Output: $Out"
if ($failed.Count) { 'Failed:'; $failed | ForEach-Object { "  $_" }; exit 1 }
