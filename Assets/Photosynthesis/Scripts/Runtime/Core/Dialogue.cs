using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Photosynthesis
{
    /// Dr. Curio's voice + subtitles. Lines live in Resources/curio_lines.txt; voice clips in Resources/VO/{id}.
    /// Progression waits on the clip (or a reading-time estimate), never on fixed sleeps. Space / Next skips a line.
    public class Dialogue : MonoBehaviour
    {
        static Dictionary<string, string> lines;
        public static string Text(string id)
        {
            if (lines == null)
            {
                lines = new Dictionary<string, string>();
                var ta = Resources.Load<TextAsset>("curio_lines");
                if (ta) foreach (var raw in ta.text.Split('\n'))
                {
                    var l = raw.Trim(); if (l.Length == 0 || l[0] == '#') continue;
                    int bar = l.IndexOf('|'); if (bar > 0) lines[l.Substring(0, bar)] = l.Substring(bar + 1);
                }
            }
            return lines.TryGetValue(id, out var t) ? t : id;
        }

        public bool SkipVoice { get; set; }      // debug: subtitles only, fast pacing
        public string LastLine { get; private set; } = "";
        public bool Speaking { get; private set; }
        bool next; int gen;                      // gen: a Speak owns the shared state only while it is the newest line
        string lastId;                           // last scripted line id (null after dynamic text): Replay re-says it with its VO clip

        void Start()
        {
            var h = Module.I.hud;
            h.NextClicked += () => next = true;
            // Replay what the panel shows. It runs here, so Cancel() (restart / debug jump) kills it, and the story's next line outranks it through gen.
            h.ReplayClicked += () => { if (!Speaking && !string.IsNullOrEmpty(LastLine)) StartCoroutine(lastId != null ? Say(lastId) : SayText(LastLine)); };
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k != null && k.spaceKey.wasPressedThisFrame && Speaking && !Module.I.hud.TypingInChat && !Module.I.hud.Paused) next = true;
        }

        /// Speak a scripted line by id and wait until it finishes (or is skipped).
        /// Not an iterator: the id is recorded at call time.
        public IEnumerator Say(string id, float hold = 0.2f) { lastId = id; return Speak(Text(id), SkipVoice ? null : Resources.Load<AudioClip>("VO/" + id), hold, false); }

        /// Speak dynamic text (AI replies). Uses device TTS when available.
        public IEnumerator SayText(string text, float hold = 0.3f) { lastId = null; var kb = SkipVoice ? null : KbClip(text); return Speak(text, kb, hold, kb == null && !SkipVoice); }

        // ------------------------------------------------------------------ pre-generated voice for dynamic replies
        /// Stable key of one sentence: FNV-1a over its lower-case ASCII letters and digits. Tools/GenerateVO.py --kb computes the same.
        public static string KbKey(string sentence)
        {
            uint h = 2166136261u; int n = 0;
            foreach (char ch in sentence.ToLowerInvariant())
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) { h ^= ch; h *= 16777619u; n++; }
            return n == 0 ? "" : h.ToString("x8");
        }

        static readonly Regex Sentences = new Regex(@"(?<=[.!?…:])\s+");
        static AudioClip kbJoined;

        /// The reply as one clip joined from Resources/VO/kb sentence clips, or null when any sentence has none (then the whole reply
        /// uses the device voice, so one answer never switches voices half-way).
        public static AudioClip KbClip(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var clips = new List<AudioClip>(); int frames = 0;
            foreach (var s in Sentences.Split(text.Trim()))
            {
                var k = KbKey(s); if (k.Length == 0) continue;
                var c = Resources.Load<AudioClip>("VO/kb/" + k); if (!c) return null;
                clips.Add(c); frames += c.samples;
            }
            if (clips.Count == 0) return null;
            if (clips.Count == 1) return clips[0];
            int chans = clips[0].channels, hz = clips[0].frequency; var data = new float[frames * chans]; int at = 0;
            foreach (var c in clips)
            {
                if (c.channels != chans || c.frequency != hz) return null;
                if (c.loadState != AudioDataLoadState.Loaded) c.LoadAudioData();
                var d = new float[c.samples * chans]; if (!c.GetData(d, 0)) return null;
                Array.Copy(d, 0, data, at, d.Length); at += d.Length;
            }
            if (kbJoined) Destroy(kbJoined);
            kbJoined = AudioClip.Create("kb-reply", frames, chans, hz, false); kbJoined.SetData(data, 0);
            return kbJoined;
        }

        IEnumerator Speak(string text, AudioClip clip, float hold, bool tts)
        {
            var m = Module.I; int my = ++gen; LastLine = text; next = false; Speaking = true;
            m.hud.ShowLine("Dr. Curio", text);
            if (m.curio) m.curio.Talking = true;
            float dur = Mathf.Clamp(text.Length * 0.052f, 1.4f, 9f);
            if (clip) dur = m.audioDirector.PlayVoice(clip);
            else if (tts) { m.audioDirector.Speak(text); float t0 = Time.time; while (my == gen && !m.audioDirector.VoicePlaying && Time.time - t0 < 6f && !next) yield return null; if (m.audioDirector.VoicePlaying) dur = 0; }
            for (float t = 0; my == gen && (t < dur || m.audioDirector.VoicePlaying) && !next; t += Time.deltaTime)
            { if (Module.Skipping) break; yield return null; }
            if (my != gen) yield break;                                   // a newer line owns the voice, Speaking, next and Talking
            if (next || Module.Skipping) m.audioDirector.StopVoice();
            if (m.curio) m.curio.Talking = false;
            for (float t = 0; t < hold && !next && !Module.Skipping && my == gen; t += Time.deltaTime) yield return null;
            if (my == gen) { Speaking = false; next = false; }
        }

        /// Beat jump / restart: stops any AI line running here and clears the state a killed line never resets.
        public void Cancel() { gen++; StopAllCoroutines(); Speaking = false; next = false; }

        public void Hide() => Module.I.hud.HideLine();
    }
}
