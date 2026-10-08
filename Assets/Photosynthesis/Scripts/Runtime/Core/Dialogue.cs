using System.Collections;
using System.Collections.Generic;
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
        public IEnumerator SayText(string text, float hold = 0.3f) { lastId = null; return Speak(text, null, hold, !SkipVoice); }

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
