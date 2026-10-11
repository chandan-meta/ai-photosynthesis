param([Parameter(Mandatory=$true)][string]$ResultPath,[Parameter(Mandatory=$true)][string]$StatusPath,[string]$WavePath)
$ErrorActionPreference='Stop'
$taskEngine=$null
try {
    Add-Type -AssemblyName System.Speech
    Add-Type -ReferencedAssemblies System.Speech -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Speech.Recognition;
using System.Threading;
public static class CurioSpeechReader {
    public static RecognitionResult[] Read(SpeechRecognitionEngine engine) {
        var phrases = new List<RecognitionResult>();
        Exception failure = null;
        using (var finished = new ManualResetEvent(false)) {
            EventHandler<SpeechRecognizedEventArgs> recognized = (s,e) => { lock(phrases) phrases.Add(e.Result); };
            EventHandler<RecognizeCompletedEventArgs> completed = (s,e) => { failure = e.Error; finished.Set(); };
            engine.SpeechRecognized += recognized; engine.RecognizeCompleted += completed;
            try {
                engine.RecognizeAsync(RecognizeMode.Multiple);
                if (!finished.WaitOne(20000)) {
                    engine.RecognizeAsyncCancel(); finished.WaitOne(2000);
                    throw new TimeoutException("Speech recognition timed out");
                }
                if (failure != null) throw failure;
                lock(phrases) return phrases.ToArray();
            } finally { engine.SpeechRecognized -= recognized; engine.RecognizeCompleted -= completed; }
        }
    }
}
'@
    $taskRecognizer=[System.Speech.Recognition.SpeechRecognitionEngine]::InstalledRecognizers() | Where-Object {$_.Culture.Name -like 'en-*'} | Select-Object -First 1
    if($null -eq $taskRecognizer){throw 'NO_ENGLISH_RECOGNIZER'}
    $taskEngine=New-Object System.Speech.Recognition.SpeechRecognitionEngine($taskRecognizer)
    $taskGrammar=New-Object System.Speech.Recognition.DictationGrammar
    $taskGrammar.Enabled=$false
    $taskEngine.LoadGrammar($taskGrammar)
    # Bias common lesson questions without disabling free-form dictation.
    $taskPhrases=New-Object System.Speech.Recognition.Choices
    $taskPhrases.Add([string[]]@('What am I looking at','Explain this again','Why does the plant need this','Show me water','Take me to carbon dioxide','Where is photosynthesis happening','What is xylem','What is phloem','Show me the stomata','Take me to the chloroplast','Where does oxygen go','What is chlorophyll','What is glucose','Show me sunlight','Show me the roots','What should I do next','I do not understand'))
    foreach($taskTopic in @('photosynthesis','carbon dioxide','water','oxygen','sunlight','chlorophyll','chloroplast','glucose','xylem','phloem','stomata','roots','leaves','stem')) {
        foreach($taskPrefix in @('What is','What are','Explain','Tell me about','Why does the plant need')) { $taskPhrases.Add("$taskPrefix $taskTopic") }
    }
    $taskPhrases.Add('Why are leaves green')
    $taskPhrases.Add([string[]]@('What is sigh lum','What is zylum','Explain sigh lum'))
    $taskBuilder=New-Object System.Speech.Recognition.GrammarBuilder
    $taskBuilder.Culture=$taskRecognizer.Culture
    $taskBuilder.Append($taskPhrases)
    $taskTopicGrammar=New-Object System.Speech.Recognition.Grammar($taskBuilder)
    $taskTopicGrammar.Name='Curio lesson questions'
    $taskTopicGrammar.Priority=1
    $taskTopicGrammar.Weight=1.0
    $taskEngine.LoadGrammar($taskTopicGrammar)
    $taskEngine.InitialSilenceTimeout=[TimeSpan]::FromSeconds(12)
    $taskEngine.BabbleTimeout=[TimeSpan]::FromSeconds(12)
    $taskEngine.EndSilenceTimeout=[TimeSpan]::FromSeconds(1)
    if($WavePath){$taskEngine.SetInputToWaveFile($WavePath)}else{$taskEngine.SetInputToDefaultAudioDevice()}
    [IO.File]::WriteAllText($StatusPath,'Listening')
    $taskParts=New-Object 'System.Collections.Generic.List[string]'
    $taskUncertain=$false
    # Try lesson vocabulary first: general dictation otherwise hears words like xylem as 'silent'.
    for($taskPass=0;$taskPass -lt 2;$taskPass++) {
        if($taskPass -gt 0) {
            if($taskParts.Count -gt 0 -or -not $WavePath){break}
            $taskTopicGrammar.Enabled=$false;$taskGrammar.Enabled=$true
            $taskEngine.SetInputToWaveFile($WavePath)
        }
        # Multiple mode reads to WAV EOF; synchronous Recognize discards audio after its first phrase.
        foreach($taskRecognition in [CurioSpeechReader]::Read($taskEngine)) {
            if($taskRecognition.Confidence -lt 0.45){$taskUncertain=$true;continue}
            $taskParts.Add(($taskRecognition.Text -replace '\b(sigh lum|zylum)\b','xylem'))
        }
    }
    if($taskParts.Count -eq 0){$taskResult=@{text='';error=$(if($taskUncertain){'LOW_CONFIDENCE'}else{'NO_SPEECH'});code=0}}else{$taskResult=@{text=($taskParts -join ' ');error='';code=0}}
} catch {
    $taskFailure=$_.Exception
    while($taskFailure.InnerException){$taskFailure=$taskFailure.InnerException}
    $taskResult=@{text='';error=$(if($taskFailure.Message -eq 'NO_ENGLISH_RECOGNIZER'){'NO_ENGLISH_RECOGNIZER'}else{'DEVICE_ERROR'});code=$taskFailure.HResult}
} finally {if($null -ne $taskEngine){$taskEngine.Dispose()}}
[IO.File]::WriteAllText($ResultPath,($taskResult | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
