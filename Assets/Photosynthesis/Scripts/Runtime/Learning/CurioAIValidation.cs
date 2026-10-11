using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Photosynthesis
{
    /// Explicit -curiocheck smoke test; never installed during normal play.
    public class CurioAIValidation : MonoBehaviour
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("[CurioCheck] FAIL: " + message); Debug.Log("[CurioCheck] PASS: " + message); }
        IEnumerator Until(Func<bool> ready, float seconds = 30)
        {
            float end = Time.unscaledTime + seconds;
            while (!ready() && Time.unscaledTime < end) yield return null;
            Check(ready(), "operation completed before timeout");
        }
        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(6);
            var m = Module.I; var h = m.hud; var ai = m.ai;
            Check(CurioAISettings.Load().mock, "smoke test uses offline practice, no API spending");
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var stereo = new float[48000 * 2];
            for (int i = 0; i < 48000; i++) stereo[i * 2] = stereo[i * 2 + 1] = .1f + .02f * Mathf.Sin(i * Mathf.PI * 2 * 400 / 48000);
            var pcm = CurioVoice.PrepareAudio(stereo, 48000, 2);
            float peak = 0, average = 0;
            foreach (float value in pcm) { peak = Mathf.Max(peak, Mathf.Abs(value)); average += value; }
            Check(pcm.Length == 16000 && peak > .15f && peak < .18f && Mathf.Abs(average / pcm.Length) < .001f,
                "microphone audio preserves duration, becomes mono 16 kHz and boosts quiet speech without DC offset");
            Check(CurioVoice.PrepareAudio(new float[16000], 16000, 1)[8000] == 0, "silent audio stays silent");
#endif
            yield return Until(() => !m.dialogue.Speaking && !h.Cinematic);
            var beat = m.Beat; float speed = Time.timeScale;
            h.OpenChat(); ai.Ask("Why are leaves green?");
            Check(ai.ConversationHeld, "question acquires pause before the first chat Update");
            yield return null; yield return null;
            Check(h.AIStatus.Contains("ready to ask") || h.AIStatus == "Finding a lesson answer…", "first chat Update preserves the active conversation status");
            yield return Until(() => ai.ConversationHeld);
            Check(Time.timeScale == 0 && !m.cam.UserControl, "conversation holds lesson and camera");
            yield return Until(() => ai.History.Count >= 2);
            Check(ai.History[ai.History.Count - 1].text.ToLowerInvariant().Contains("chlorophyll") && ai.History[ai.History.Count - 1].text.ToLowerInvariant().Contains("green"), "offline science answer reaches history and chat");
            yield return new WaitForSecondsRealtime(1);
            string dir = Path.Combine(Application.persistentDataPath, "CurioCheck"); Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "conversation.png"));
            yield return new WaitForSecondsRealtime(.5f);
            h.CloseChat(); yield return null;
            Check(!ai.ConversationHeld && !m.audioDirector.VoicePlaying && Time.timeScale == speed && m.Beat == beat, "closing stops speech and restores exact lesson beat and speed");
            h.SetCinematic(true); h.OpenChat(); yield return null;
            Check(!ai.ConversationHeld, "critical cinematic cannot be paused by chat");
            h.SetCinematic(false); yield return Until(() => ai.ConversationHeld);
            h.CloseChat();
            var narration = m.dialogue.StartCoroutine(m.dialogue.Say("s4_stomata"));
            yield return null; h.OpenChat(); yield return null;
            Check(!ai.ConversationHeld, "ordinary narration finishes before borrowing voice");
            yield return Until(() => ai.ConversationHeld, 20);
            h.DebugSystem(0); yield return null; yield return null;
            Check(!h.ChatOpen && !ai.ConversationHeld && h.Paused, "pause menu closes conversation");
            h.DebugSystem(-1); yield return null;
            Check(Time.timeScale == speed, "pause menu restores lesson speed");
            h.OpenChat(); yield return Until(() => ai.ConversationHeld);
            m.story.Run(Beat.FreeExplore); yield return null;
            Check(!ai.ConversationHeld && !h.ChatOpen && Time.timeScale == speed, "beat restart cleans up conversation synchronously");
            Debug.Log("[CurioCheck] ALL PASSED; screenshot: " + dir);
            Application.Quit();
        }
    }
}
