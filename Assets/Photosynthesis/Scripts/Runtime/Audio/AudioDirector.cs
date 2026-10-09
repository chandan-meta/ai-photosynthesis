using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Photosynthesis
{
    /// Layered audio: procedural SFX, dynamic music states, ambience beds, Dr. Curio voice + device TTS.
    /// No audio assets except the VO clips: Synth renders every SFX / music loop / ambience bed on a worker thread at
    /// startup (SFX first, then loops in story order; a requested state jumps the queue) and clips are created on the
    /// main thread as renders arrive. Music and ambience each cross-fade between two looping sources; music ducks under voice.
    public class AudioDirector : MonoBehaviour
    {
        public float MusicVolume { get; set; } = 0.6f;
        public float SfxVolume { get; set; } = 0.8f;
        public float VoiceVolume { get; set; } = 1f;

        const int SfxVoices = 14;
        static readonly int SfxCount = Enum.GetValues(typeof(Sfx)).Length;

        AudioSource voice; AudioSource[] sfx; float[] sfxBase, lastPlay, lastVol; int[] lastSrc; int sfxNext;
        readonly Bus music = new Bus(), amb = new Bus();
        readonly AudioClip[] sfxClips = new AudioClip[SfxCount];
        readonly Dictionary<string, AudioClip> loops = new Dictionary<string, AudioClip>();
        readonly List<(string key, Func<float[]> make)> jobs = new List<(string, Func<float[]>)>();
        readonly ConcurrentQueue<(string key, float[] data)> rendered = new ConcurrentQueue<(string, float[])>();
        volatile bool quitting;
        Task renderWorker;
        bool inited;
        MusicState musicState = MusicState.None; string bed = "none";
        float duck = 1f, sfxDuck = 1f, voiceSeen = -10f, level, holdUntil;
        readonly float[] probe = new float[256];

        /// Two looping sources cross-fading (music bus / ambience bus).
        sealed class Bus
        {
            readonly AudioSource[] src = new AudioSource[2];
            readonly float[] gain = new float[2], target = new float[2], rate = { 1f, 1f };
            int cur;
            public string want; public float fade = 2f;

            public void Init(Transform host, string name, int priority)
            {
                for (int i = 0; i < 2; i++) src[i] = Source(host, name + i, priority, true);
            }

            public void Play(AudioClip clip, float fadeTime)
            {
                float r = 1f / Mathf.Max(0.05f, fadeTime);
                int n = -1;
                for (int i = 0; i < 2; i++) if (src[i].clip == clip && src[i].isPlaying) n = i;   // already on (or fading out): bring it back
                if (n < 0)
                {   // Start on the quieter source. Quick successive changes (debug jumps, Prepare → GoTo) then only ever
                    // cut a barely-audible tail, never the louder bed that is still fading out (audible click).
                    n = gain[0] <= gain[1] ? 0 : 1;
                    var s = src[n]; s.clip = clip; s.time = 0f; s.Play(); gain[n] = 0f;
                }
                cur = n; target[n] = 1f; rate[n] = r; target[1 - n] = 0f; rate[1 - n] = r;
            }

            public void Stop(float fadeTime) { for (int i = 0; i < 2; i++) { target[i] = 0f; rate[i] = 1f / Mathf.Max(0.05f, fadeTime); } }

            public void Tick(float dt, float volume)
            {
                for (int i = 0; i < 2; i++)
                {
                    gain[i] = Mathf.MoveTowards(gain[i], target[i], rate[i] * dt);
                    var s = src[i];
                    s.volume = gain[i] * gain[i] * (3f - 2f * gain[i]) * volume;   // smoothstep → equal-ish power cross-fade
                    if (gain[i] <= 0f && target[i] <= 0f && s.isPlaying) s.Stop();
                }
            }
        }

        static AudioSource Source(Transform host, string name, int priority, bool loop)
        {
            var g = new GameObject(name); g.transform.SetParent(host, false);
            var s = g.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f; s.priority = priority; s.volume = 0f;
            return s;
        }

        // ================================================================== lifecycle
        void Awake() => Init();

        void Init()
        {
            if (inited) return; inited = true;
            MusicVolume = PlayerPrefs.GetFloat("ps.vol.music", MusicVolume); SfxVolume = PlayerPrefs.GetFloat("ps.vol.sfx", SfxVolume);
            VoiceVolume = PlayerPrefs.GetFloat("ps.vol.voice", VoiceVolume);   // Settings ▸ Audio survives a relaunch (HudSystem stores them)
            var host = new GameObject("Audio").transform; host.SetParent(transform, false);
            voice = Source(host, "Voice", 0, false);
            sfx = new AudioSource[SfxVoices]; sfxBase = new float[SfxVoices];
            for (int i = 0; i < SfxVoices; i++) sfx[i] = Source(host, "Sfx" + i, 128, false);
            music.Init(host, "Music", 16); amb.Init(host, "Ambience", 64);
            lastPlay = new float[SfxCount]; lastVol = new float[SfxCount]; lastSrc = new int[SfxCount];
            for (int i = 0; i < SfxCount; i++) lastPlay[i] = -1f;

            // Render queue: SFX, then loops in the order the story first needs them.
            foreach (Sfx s in Enum.GetValues(typeof(Sfx))) { var k = s; jobs.Add(("sfx:" + (int)k, () => Synth.MakeSfx(k))); }
            void Mus(MusicState m) => jobs.Add(("mus:" + m, () => Synth.MakeMusic(m)));
            void Amb(string b) => jobs.Add(("amb:" + b, () => Synth.MakeAmbience(b)));
            Mus(MusicState.NewWorld); Amb("chamber"); Mus(MusicState.Investigation); Amb("micro"); Mus(MusicState.Microscopic);
            Amb("soil"); Mus(MusicState.WaterJourney); Amb("xylem"); Amb("dark"); Mus(MusicState.SunlightReveal); Amb("cell");
            Mus(MusicState.Reaction); Mus(MusicState.Resolution); Mus(MusicState.FreeExplore);
            // ponytail: one background Task renders everything (~3–8 s total, all off the main thread). WebGL has no threads —
            // render inline in small chunks there if that platform is ever targeted.
            renderWorker = Task.Run(Work);
        }

        void Work()
        {
            while (!quitting)
            {
                (string key, Func<float[]> make) job;
                lock (jobs) { if (jobs.Count == 0) return; job = jobs[0]; jobs.RemoveAt(0); }
                try { var data = job.make(); if (!quitting) rendered.Enqueue((job.key, data)); }
                catch (Exception e) { if (!quitting) Debug.LogWarning($"[Photosynthesis] audio render '{job.key}' failed: {e.Message}"); }
            }
        }

        void Prioritise(string key)
        {
            lock (jobs)
                for (int i = 1; i < jobs.Count; i++)
                    if (jobs[i].key == key) { var j = jobs[i]; jobs.RemoveAt(i); jobs.Insert(0, j); return; }
        }

        void Accept(string key, float[] data)
        {
            bool isSfx = key.StartsWith("sfx:", StringComparison.Ordinal);
            int ch = isSfx ? 1 : 2;
            var clip = AudioClip.Create(key, data.Length / ch, ch, Synth.SR, false);
            clip.SetData(data, 0);
            if (isSfx) sfxClips[int.Parse(key.Substring(4))] = clip;
            else { loops[key] = clip; TryStart(music); TryStart(amb); }
        }

        void TryStart(Bus b)
        {
            if (b.want != null && loops.TryGetValue(b.want, out var clip)) { b.Play(clip, b.fade); b.want = null; }
        }

        void Update()
        {
            if (!inited || quitting) return;
            float dt = Time.unscaledDeltaTime;
            // Clips arrive from the worker: several small SFX per frame, at most one long loop (SetData cost).
            for (int n = 0; n < 4 && rendered.TryDequeue(out var r); n++) { Accept(r.key, r.data); if (!r.key.StartsWith("sfx:", StringComparison.Ordinal)) break; }

            // Voice loudness (RMS of the latest output block, fast attack / slower release) → Curio's talking pulse.
            if (voice.isPlaying)
            {
                voice.GetOutputData(probe, 0);
                float s = 0f; for (int i = 0; i < probe.Length; i++) s += probe[i] * probe[i];
                float v = Mathf.Clamp01(Mathf.Sqrt(s / probe.Length) / Mathf.Max(0.1f, voice.volume) * 3.5f);
                level = Mathf.Lerp(level, v, 1f - Mathf.Exp(-dt * (v > level ? 30f : 10f)));
            }
            else level = Mathf.MoveTowards(level, 0f, dt * 4f);
            voice.volume = VoiceVolume;

            float now = Time.unscaledTime;
            if (VoicePlaying) voiceSeen = now;
            bool speaking = now - voiceSeen < 0.6f;   // release hold: bridges Dialogue's ~0.2 s between lines, so the music no longer swells +2.5 dB per sentence
            duck = Mathf.MoveTowards(duck, speaking ? 0.42f : 1f, dt * (speaking ? 2.2f : 0.7f));
            sfxDuck = Mathf.MoveTowards(sfxDuck, speaking ? 0.55f : 1f, dt * (speaking ? 5f : 0.8f));   // world/feedback cues -5.2 dB under Curio; 0.09 s attack lands before his first word (clips lead ~0.1 s)
            music.Tick(dt, MusicVolume * duck * (Module.I && Module.I.AtStartScreen ? 0.32f : 0.9f));
            amb.Tick(dt, SfxVolume * (0.7f + 0.3f * duck) * 0.85f);
            for (int i = 0; i < SfxVoices; i++) if (sfx[i].isPlaying) sfx[i].volume = SfxGain(i);   // also carries SfxVolume changes
        }

        void OnDisable() { if (inited) StopVoice(); }

        void OnApplicationQuit()
        {
            // Stop producers before native audio/logging shuts down, rather than waiting for OnDestroy.
            quitting = true; lock (jobs) jobs.Clear();
            // Finish the current pure-PCM render before Unity tears down its managed/native bridge.
            if (renderWorker != null && !renderWorker.IsCompleted)
                try { renderWorker.Wait(1500); } catch (AggregateException) { }
        }

        void OnDestroy()
        {
            OnApplicationQuit();
            foreach (var c in sfxClips) if (c) Destroy(c);
            foreach (var c in loops.Values) if (c) Destroy(c);
        }

        // ================================================================== SFX / music / ambience
        public void Play(Sfx s, float volume = 1f, float pitch = 1f)
        {
            Init();
            int id = (int)s; if (id < 0 || id >= SfxCount) return;
            var clip = sfxClips[id]; if (!clip) return;              // still rendering (first ~0.5 s after launch)
            float now = Time.unscaledTime; volume = Mathf.Max(0f, volume); pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            if (now - lastPlay[id] < 0.05f)
            {   // the same cue fired twice in one beat (e.g. Curio.Scan + Story both play Sfx.Scan) → play once, as the louder request
                var prev = sfx[lastSrc[id]];
                if (volume > lastVol[id] && prev.clip == clip && prev.isPlaying)
                { lastVol[id] = sfxBase[lastSrc[id]] = volume; prev.volume = SfxGain(lastSrc[id]); prev.pitch = pitch; }
                return;
            }
            lastPlay[id] = now;
            int k = sfxNext;
            for (int j = 0; j < SfxVoices; j++) { int c = (sfxNext + j) % SfxVoices; if (!sfx[c].isPlaying) { k = c; break; } }
            sfxNext = (k + 1) % SfxVoices;
            var src = sfx[k];
            src.Stop(); src.clip = clip; src.pitch = pitch; src.ignoreListenerPause = s <= Sfx.UiSelect;   // menus click while paused
            lastSrc[id] = k; lastVol[id] = sfxBase[k] = volume; src.volume = SfxGain(k);
            src.Play();
        }

        /// UI clicks (ignoreListenerPause, s <= UiSelect) never duck; everything else sits under Curio's voice.
        float SfxGain(int i) => sfxBase[i] * SfxVolume * (sfx[i].ignoreListenerPause ? 1f : sfxDuck);

        public void Music(MusicState s, float fade = 2f)
        {
            Init();
            if (s == musicState) return;
            musicState = s; music.fade = Mathf.Max(0.05f, fade);
            if (s == MusicState.None) { music.want = null; music.Stop(music.fade); return; }
            music.want = "mus:" + s; Prioritise(music.want); TryStart(music);
        }

        /// "chamber", "micro", "soil", "xylem", "cell", "none"
        public void Ambience(string bedName)
        {
            Init();
            bedName = string.IsNullOrEmpty(bedName) ? "none" : bedName.ToLowerInvariant();
            if (bedName == bed) return;
            bed = bedName; amb.fade = 1.6f;
            if (!Synth.HasBed(bed)) { amb.want = null; amb.Stop(amb.fade); return; }
            amb.want = "amb:" + bed; Prioritise(amb.want); TryStart(amb);
        }

        // ================================================================== voice
        /// Plays a voice clip, returns its length in seconds.
        public float PlayVoice(AudioClip clip)
        {
            if (!clip) return 0f;
            Init(); StopVoice();
            voice.clip = clip; voice.volume = VoiceVolume; voice.Play();
            return clip.length;
        }

        public void StopVoice()
        {
            if (!inited) return;
            CancelTts();
            voice.Stop(); holdUntil = 0f;
        }

        public bool VoicePlaying => inited && (ttsBusy || voice.isPlaying || Time.unscaledTime < holdUntil);
        /// 0..1 loudness of the current voice (drives Curio's talking pulse).
        public float VoiceLevel => level;
        /// Voice audio is actually sounding (VoiceLevel is meaningful). False while TTS synthesises or a subtitle is held.
        internal bool VoiceAudible => inited && voice.isPlaying;
        internal bool VoiceSynthesizing => ttsBusy;
        static float ReadTime(string text) => Mathf.Clamp(text.Length * 0.055f, 1.5f, 12f);

        // ------------------------------------------------------------------ device TTS (Windows SAPI)
        bool ttsBusy;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        Coroutine ttsJob; System.Diagnostics.Process ttsProcess; string ttsFile; AudioClip ttsClip;
#endif

        /// Device text-to-speech for dynamic AI replies (Windows SAPI). Sets VoicePlaying while speaking.
        public void Speak(string text)
        {
            Init(); StopVoice();
            if (string.IsNullOrWhiteSpace(text)) return;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (isActiveAndEnabled) { ttsBusy = true; ttsJob = StartCoroutine(Synthesize(text)); return; }
#endif
            // No device voice here: hold VoicePlaying for the reading time, so Dialogue shows the subtitle for that long
            // (instead of waiting 6 s for a voice that never starts) and Curio mimes the line.
            holdUntil = Time.unscaledTime + ReadTime(text);
        }

        /// Dynamic replies (device SAPI, Zira at a raised pitch — the nearest offline match to the generated VO's neural child voice,
        /// Tools/GenerateVO.py): plain digits for subscripts, spoken symbols, natural pauses on … and —.
        static string Ssml(string text)
        {
            var sb = new StringBuilder(text.Length + 160);
            sb.Append("<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\"><prosody pitch=\"high\">");
            string body = System.Security.SecurityElement.Escape(text.Trim().TrimStart('…', '.', ' ')) ?? "";
            foreach (char c in body)
            {
                if (c >= '₀' && c <= '₉') sb.Append((char)('0' + (c - '₀')));
                else if (c == '…') sb.Append("<break time=\"420ms\"/>");
                else if (c == '—') sb.Append("<break time=\"250ms\"/>");
                else if (c == '→') sb.Append(" to ");
                else if (c == '×') sb.Append(" times ");
                else if (c == '✓' || c == '✕' || c == '•') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append("</prosody></speak>").ToString();
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        IEnumerator Synthesize(string text)
        {
            ttsFile = Path.Combine(Application.temporaryCachePath, "curio-" + Guid.NewGuid().ToString("N") + ".wav");
            // Text and path travel base64-encoded, so they are never interpreted as PowerShell source.
            string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
            string Dec(string b) => "[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + b + "'))";
            string script = "$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Speech; $s=New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                "try { try { $s.SelectVoice('Microsoft Zira Desktop') } catch {}; $s.Rate=1; $s.SetOutputToWaveFile(" + Dec(B64(ttsFile)) + "); " +
                "$s.SpeakSsml(" + Dec(B64(Ssml(text))) + ") } finally { $s.Dispose() }";
            try
            {
                ttsProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"),
                    Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                });
            }
            catch (Exception e) { Debug.LogWarning("[Photosynthesis] device voice could not start: " + e.Message); }
            if (ttsProcess == null) { TtsFailed(text); yield break; }

            float deadline = Time.realtimeSinceStartup + 20f;
            while (!ttsProcess.HasExited && Time.realtimeSinceStartup < deadline) yield return null;
            if (!ttsProcess.HasExited || ttsProcess.ExitCode != 0 || !File.Exists(ttsFile)) { TtsFailed(text); yield break; }

            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(ttsFile).AbsoluteUri, AudioType.WAV))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    ttsClip = DownloadHandlerAudioClip.GetContent(req);
                    voice.clip = ttsClip; voice.volume = VoiceVolume; voice.Play();
                }
                else { TtsFailed(text); yield break; }
            }
            CleanupTts(false);
            ttsJob = null; ttsBusy = false;   // voice.isPlaying now carries VoicePlaying
        }

        /// Synthesis failed: keep VoicePlaying true for a reading-time estimate so the subtitle stays up.
        void TtsFailed(string text)
        {
            CleanupTts(false);
            ttsJob = null; ttsBusy = false;
            holdUntil = Time.unscaledTime + ReadTime(text);
        }

        void CleanupTts(bool destroyClip)
        {
            if (ttsProcess != null)
            {
                try { if (!ttsProcess.HasExited) ttsProcess.Kill(); } catch (Exception) { }
                ttsProcess.Dispose(); ttsProcess = null;
            }
            if (!string.IsNullOrEmpty(ttsFile)) { try { File.Delete(ttsFile); } catch (Exception) { } ttsFile = null; }
            if (destroyClip && ttsClip) { if (voice.clip == ttsClip) voice.clip = null; Destroy(ttsClip); ttsClip = null; }
        }
#endif

        void CancelTts()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (ttsJob != null) { StopCoroutine(ttsJob); ttsJob = null; }
            CleanupTts(true);
#endif
            ttsBusy = false;
        }
    }
}
