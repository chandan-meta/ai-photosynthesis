$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Speech
$taskRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskOutput=Join-Path $taskRoot 'Logs/CurioAI/speech'
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskScript=Join-Path $taskRoot 'Assets/StreamingAssets/CurioSpeech/Recognize.ps1'
$taskSynth=New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $taskCases=@(
        @{name='topic';ssml='What is water?';words=@('water')},
        @{name='xylem';ssml='What is <phoneme alphabet="sapi" ph="Z AY L AH M">xylem</phoneme>?';words=@('xylem')},
        @{name='xylem-default';ssml='What is xylem?';words=@('xylem')},
        @{name='question';ssml='Why are leaves green?';words=@('leaves','green')},
        @{name='two-phrases';ssml='What is water? <break time="1400ms"/> Where does oxygen go?';words=@('water','oxygen')}
    )
    foreach($taskCase in $taskCases) {
        $taskWave=Join-Path $taskOutput ($taskCase.name+'.wav')
        $taskJson=Join-Path $taskOutput ($taskCase.name+'.json')
        $taskFormat=New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000,[System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,[System.Speech.AudioFormat.AudioChannel]::Mono)
        $taskSynth.SetOutputToWaveFile($taskWave,$taskFormat)
        $taskSynth.SpeakSsml('<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="en-US"><break time="300ms"/>'+$taskCase.ssml+'<break time="300ms"/></speak>')
        $taskSynth.SetOutputToNull()
        & "$env:SystemRoot/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $taskScript -WavePath $taskWave -ResultPath $taskJson -StatusPath ($taskJson+'.status')
        $taskResult=Get-Content -LiteralPath $taskJson -Raw | ConvertFrom-Json
        foreach($taskWord in $taskCase.words) { if($taskResult.text -notmatch [regex]::Escape($taskWord)){throw "FAIL $($taskCase.name): $($taskResult | ConvertTo-Json -Compress)"} }
        Write-Output "PASS $($taskCase.name): $($taskResult.text)"
    }
    $taskSilent=Join-Path $taskOutput 'silence.wav'
    $taskWriter=New-Object IO.BinaryWriter([IO.File]::Create($taskSilent))
    try {
        $taskWriter.Write([Text.Encoding]::ASCII.GetBytes('RIFF'));$taskWriter.Write([int]64036)
        $taskWriter.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '));$taskWriter.Write([int]16)
        $taskWriter.Write([int16]1);$taskWriter.Write([int16]1);$taskWriter.Write([int]16000);$taskWriter.Write([int]32000)
        $taskWriter.Write([int16]2);$taskWriter.Write([int16]16)
        $taskWriter.Write([Text.Encoding]::ASCII.GetBytes('data'));$taskWriter.Write([int]64000);$taskWriter.Write([byte[]]::new(64000))
    } finally {$taskWriter.Dispose()}
    $taskJson=Join-Path $taskOutput 'silence.json'
    & "$env:SystemRoot/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $taskScript -WavePath $taskSilent -ResultPath $taskJson -StatusPath ($taskJson+'.status')
    $taskResult=Get-Content -LiteralPath $taskJson -Raw | ConvertFrom-Json
    if($taskResult.text -or $taskResult.error -ne 'NO_SPEECH'){throw 'FAIL: silence produced a question'}
    Write-Output 'PASS silence: no fabricated question'
} finally {$taskSynth.Dispose()}
