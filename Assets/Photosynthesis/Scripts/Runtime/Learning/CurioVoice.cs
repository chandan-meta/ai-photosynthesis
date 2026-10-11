using System;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Diagnostics;
using System.IO;
using System.Text;
#endif

namespace Photosynthesis
{
    /// Push-to-talk for Ask Dr. Curio. Windows: records the microphone (16 kHz, ≤ 15 s) to a temporary WAV, then transcribes it
    /// with the installed offline English recogniser (StreamingAssets/CurioSpeech/Recognize.ps1) in a hidden powershell.exe,
    /// polled from Tick() so nothing blocks. Other platforms: typed questions only. Temp files are always deleted.
    public sealed class CurioVoice : IDisposable
    {
        public enum Phase { Idle, Recording, Transcribing }
        public Phase State { get; private set; }
        /// 0..1 live input level while recording (for the HUD's mic meter).
        public float Level { get; private set; }
        /// Exactly one of these fires per Begin(): recognised text, or a friendly message for the chat.
#pragma warning disable CS0067   // Heard is never raised on typed-only platforms
        public event Action<string> Heard, Failed;
#pragma warning restore CS0067

        const int Rate = 16000, MaxSeconds = 15;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [Serializable] class Result { public string text = "", error = ""; public int code; }

        AudioClip clip; string device; float startedAt, deadline, releaseAt;
        Process proc; string wavPath, resultPath, statusPath;
        readonly float[] meter = new float[512];

        public void Begin(string mic)
        {
            if (State != Phase.Idle) return;
            try
            {
                if (Microphone.devices.Length == 0) { Fail("I can't find a microphone. Plug one in — or just type your question below."); return; }
                device = string.IsNullOrEmpty(mic) || Array.IndexOf(Microphone.devices, mic) < 0 ? null : mic;   // null = system default
                int minHz = 0, maxHz = 0;   // 0/0 = any rate; else stay inside the device's range
                try { Microphone.GetDeviceCaps(device, out minHz, out maxHz); } catch (Exception) { }
                clip = Microphone.Start(device, false, MaxSeconds, maxHz > 0 ? Mathf.Clamp(Rate, minHz, maxHz) : Rate);
                if (!clip) { Fail("I couldn't open the microphone. Check Windows Sound settings and microphone privacy, then try again."); return; }
                State = Phase.Recording; startedAt = Time.realtimeSinceStartup; releaseAt = 0; Level = 0;
            }
            catch (Exception e) { Fail("I couldn't open the microphone (" + e.GetType().Name + "). You can always type your question instead."); }
        }

        /// Release: stop recording and start transcription.
        public void End()
        {
            // Keep the last consonant when the learner releases the button while finishing a word.
            if (State == Phase.Recording && releaseAt == 0) releaseAt = Time.realtimeSinceStartup + .2f;
        }

        void FinishRecording()
        {
            if (State != Phase.Recording) return;
            try
            {
                bool live = Microphone.IsRecording(device);
                int frames = live ? Microphone.GetPosition(device) : clip.samples;
                Microphone.End(device); Level = 0;
                int rate = clip.frequency;
                if (frames < rate * 0.35f) { Fail("That was a bit short! Hold the microphone button while you speak, then let go."); return; }
                var samples = new float[frames * clip.channels];
                clip.GetData(samples, 0);
                float peak = 0; foreach (var s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
                if (peak < 0.002f) { Fail("I couldn't hear anything — your microphone may be muted. Check it and try again, or type your question."); return; }
                int channels = clip.channels;
                UnityEngine.Object.Destroy(clip); clip = null;

                var id = Guid.NewGuid().ToString("N");
                wavPath = Path.Combine(Application.temporaryCachePath, "curio-" + id + ".wav");
                resultPath = Path.Combine(Application.temporaryCachePath, "curio-" + id + ".json");
                statusPath = resultPath + ".status";
                WriteWav(wavPath, PrepareAudio(samples, rate, channels), Rate, 1);

                var script = Path.Combine(Application.streamingAssetsPath, "CurioSpeech", "Recognize.ps1");
                if (!File.Exists(script)) { Fail("Voice recognition isn't installed with this build. Please type your question."); return; }
                string command = "& ([ScriptBlock]::Create(" + Data(File.ReadAllText(script)) + ")) -ResultPath " + Data(resultPath) + " -StatusPath " + Data(statusPath) + " -WavePath " + Data(wavPath);
                proc = Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                    Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command)),
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                });
                State = Phase.Transcribing; deadline = Time.realtimeSinceStartup + 50f;
            }
            catch (Exception e) { Fail("Something went wrong with voice recognition (" + e.GetType().Name + "). Try again, or type your question."); }
        }

        /// Call every frame: mic meter, 15 s auto-stop, and non-blocking polling of the recogniser.
        public void Tick()
        {
            if (State == Phase.Recording)
            {
                if (!Microphone.IsRecording(device) || Time.realtimeSinceStartup - startedAt >= MaxSeconds || releaseAt > 0 && Time.realtimeSinceStartup >= releaseAt) { FinishRecording(); return; }
                int pos = Microphone.GetPosition(device);
                if (pos >= meter.Length && clip)
                {
                    clip.GetData(meter, pos - meter.Length);
                    float peak = 0; for (int i = 0; i < meter.Length; i++) { float a = Mathf.Abs(meter[i]); if (a > peak) peak = a; }
                    Level = Mathf.Lerp(Level, Mathf.Clamp01(peak * 4f), 0.5f);
                }
            }
            else if (State == Phase.Transcribing)
            {
                bool done; try { done = proc == null || proc.HasExited; } catch (InvalidOperationException) { done = true; }
                if (!done && Time.realtimeSinceStartup < deadline) return;
                if (!done) { Fail("Speech recognition took too long. Try again, or type your question."); return; }
                Result r = null;
                try { if (File.Exists(resultPath)) r = JsonUtility.FromJson<Result>(File.ReadAllText(resultPath)); } catch (Exception) { }
                Cleanup(); State = Phase.Idle;
                if (r != null && !string.IsNullOrWhiteSpace(r.text)) { Heard?.Invoke(r.text.Trim()); return; }
                string err = r?.error;
                Failed?.Invoke(err == "LOW_CONFIDENCE" ? "I heard speech but couldn't identify the question reliably. Try a short question like 'What is xylem?'"
                    : err == "NO_SPEECH" ? "I didn't catch any words that time. Hold the microphone, speak clearly, then let go — or type your question."
                    : err == "NO_ENGLISH_RECOGNIZER" ? "This computer doesn't have an English speech recogniser installed, so please type your question instead."
                    : "Something went wrong with voice recognition. Try again, or type your question.");
            }
        }

        public void Cancel()
        {
            if (clip) { try { Microphone.End(device); } catch (Exception) { } UnityEngine.Object.Destroy(clip); clip = null; }
            Cleanup(); State = Phase.Idle; Level = 0; releaseAt = 0;
        }

        void Fail(string message) { Cancel(); Failed?.Invoke(message); }

        void Cleanup()
        {
            if (proc != null)
            {
                try { if (!proc.HasExited) { proc.Kill(); proc.WaitForExit(300); } } catch (Exception) { }
                proc.Dispose(); proc = null;
            }
            Delete(wavPath); Delete(resultPath); Delete(statusPath); wavPath = resultPath = statusPath = null;
        }

        static void Delete(string path) { if (!string.IsNullOrEmpty(path)) try { File.Delete(path); } catch (Exception) { } }

        // Embeds a string in the PowerShell command as base64 so paths/script text need no quoting.
        static string Data(string text) => "([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "')))";

        internal static float[] PrepareAudio(float[] samples, int rate, int channels)
        {
            int frames = samples.Length / channels;
            var mono = new float[frames]; float mean = 0;
            for (int i = 0; i < frames; i++)
            {
                for (int c = 0; c < channels; c++) mono[i] += samples[i * channels + c] / channels;
                mean += mono[i];
            }
            mean /= Mathf.Max(1, frames);
            float peak = 0;
            for (int i = 0; i < frames; i++) { mono[i] -= mean; peak = Mathf.Max(peak, Mathf.Abs(mono[i])); }
            float gain = peak > .002f ? Mathf.Min(8f, .85f / peak) : 1f;
            var pcm = new float[Mathf.Max(1, (int)((long)frames * Rate / rate))];
            for (int i = 0; i < pcm.Length && frames > 0; i++)
            {
                float pos = (float)i * rate / Rate; int a = Mathf.Min((int)pos, frames - 1);
                pcm[i] = Mathf.Lerp(mono[a], mono[Mathf.Min(a + 1, frames - 1)], pos - a) * gain;
            }
            return pcm;
        }

        static void WriteWav(string path, float[] samples, int rate, int channels)
        {
            using var w = new BinaryWriter(File.Create(path));
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples.Length * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(samples.Length * 2);
            foreach (var s in samples) w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767));
        }
#else
        public void Begin(string mic) => Failed?.Invoke("Voice questions work on Windows. Type your question below and I'll answer!");
        public void End() { }
        public void Tick() { }
        public void Cancel() { State = Phase.Idle; Level = 0; }
#endif

        public void Dispose() => Cancel();
    }
}
