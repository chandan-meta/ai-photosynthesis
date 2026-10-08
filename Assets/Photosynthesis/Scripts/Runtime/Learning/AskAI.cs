using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Photosynthesis
{
    /// What the learner wants. Mapped to safe predefined world actions by Story.WorldAction.
    /// Order matters: everything from ShowCO2 on is a world action (Hint stays below it; new Show* go at the end).
    public enum Intent
    {
        None, Greeting, WhatIsThis, ExplainAgain, WhyNeed, WhatNext, Help, Hint,
        ShowCO2, ShowStomata, ShowWater, ShowRoots, ShowXylem, ShowSunlight, WherePhotosynthesis,
        ShowChloroplast, ShowChlorophyll, ShowGlucose, ShowOxygen, ShowPhloem, ShowEquation,
        ShowLeaf, ShowCell, ShowStem
    }

    /// Ask Dr. Curio: typed or spoken questions → intent → reply (+ optional backend) → world action.
    /// Flow: Hud.ChatSubmitted / chips / voice → Ask(text) → IntentRouter → CurioKnowledge (offline, context-aware)
    /// → Story.WorldAction(intent) → chat bubble + "WORLD ACTION" tag → Dr. Curio speaks. The backend (if configured)
    /// only words free-form (Intent.None) replies; it can never change scores or progression.
    public class AskAI : MonoBehaviour
    {
        [Tooltip("Optional chat backend (same JSON contract as the Ohm's Law Wattson proxy). Empty = offline replies.")]
        public string endpoint = "";
        [Tooltip("Microphone device for voice questions. Empty = system default.")]
        public string micDevice = "";

        /// 0..1 live microphone level while the learner holds the mic (drives the HUD meter).
        public float MicLevel => voice.Level;

        [Serializable] public class Turn { public string role, text; }
        /// Last 12 chat turns (in memory only), oldest first. Roles: "user" / "assistant".
        public IReadOnlyList<Turn> History => history;

        const int HistoryMax = 12, TimeoutSeconds = 20;
        readonly List<Turn> history = new List<Turn>();
        readonly CurioVoice voice = new CurioVoice();
        Hud hud; Coroutine answering; UnityWebRequest web; string spokenReply;
        CurioVoice.Phase phase; bool chatWasOpen, wired;
        VoiceState answerState = VoiceState.Idle;   // Thinking / Speaking while an answer runs; the mic state wins over it

        void Start() => Wire();

        void Wire()
        {
            if (wired || !Module.I || !Module.I.hud) return;
            wired = true; hud = Module.I.hud;
            hud.ChatSubmitted += Ask; hud.MicDown += MicDown; hud.MicUp += MicUp; hud.ResetRequested += DropAnswer;
            voice.Heard += Ask;
            voice.Failed += msg => { hud.AddChat(false, msg); ShowState(); };
        }

        void Update()
        {
            voice.Tick();
            if (!hud) return;
            if (voice.State != phase) { phase = voice.State; ShowState(); }   // recording → transcribing can also happen on the 15 s auto-stop
            bool open = hud.ChatOpen;
            if (chatWasOpen && !open && voice.State != CurioVoice.Phase.Idle) { voice.Cancel(); ShowState(); }
            chatWasOpen = open;
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && voice.State == CurioVoice.Phase.Recording) { voice.Cancel(); ShowState(); }
        }

        void OnDisable()
        {
            voice.Cancel(); CancelAnswer(); ShowState();
        }

        /// One owner for the HUD voice indicator: Listening/Transcribing while the mic is busy, else the answer's state.
        void ShowState()
        {
            if (!hud) return;
            hud.SetVoiceState(voice.State == CurioVoice.Phase.Recording ? VoiceState.Listening
                : voice.State == CurioVoice.Phase.Transcribing ? VoiceState.Transcribing : answerState);
        }

        void SetAnswerState(VoiceState s) { answerState = s; ShowState(); }

        void OnDestroy()
        {
            if (hud) { hud.ChatSubmitted -= Ask; hud.MicDown -= MicDown; hud.MicUp -= MicUp; hud.ResetRequested -= DropAnswer; }
            voice.Dispose(); CancelWeb();
        }

        // ------------------------------------------------------------------ voice
        void MicDown()
        {
            Wire();
            if (voice.State != CurioVoice.Phase.Idle || !isActiveAndEnabled) return;
            if (MineSpeaking) Module.I.audioDirector.StopVoice();   // the learner wants to talk — Curio stops mid-answer
            voice.Begin(micDevice);
        }

        void MicUp() => voice.End();

        // ------------------------------------------------------------------ questions
        public void Ask(string text)
        {
            Wire();
            if (!hud || !isActiveAndEnabled || string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length > 300) text = text.Substring(0, 300);
            CancelAnswer();   // the newest question wins
            answering = StartCoroutine(Answer(text));
        }

        IEnumerator Answer(string text)
        {
            var m = Module.I; var dlg = m.dialogue;
            hud.AddChat(true, text);
            SetAnswerState(VoiceState.Thinking);
            Emote(CurioEmote.Think);
            var past = new List<Turn>(history);   // backend history excludes the current message
            Remember("user", text);

            var q = IntentRouter.Parse(text);
            // Anything but a greeting asked while a question card is open counts as help: a correct answer then earns no credit.
            if (m.story.OpenQuestion != null && q.intent != Intent.Greeting) m.story.NoteHelp();
            // Hint-only mode (final assessment / an open card): Dr. Curio nudges instead of answering, and the backend stays out of it.
            bool hintOnly = m.Beat == Beat.Assessment || m.story.OpenQuestion != null;
            float t0 = Time.unscaledTime;
            string remote = null;
            if (q.intent == Intent.None && !hintOnly && ValidEndpoint) yield return Remote(text, past, r => remote = r);   // free-form questions only
            while (Time.unscaledTime - t0 < 0.45f) yield return null;   // a visible beat of "thinking"
            while (hud.Paused && !m.story.AutoPlay) yield return null;   // never act or speak behind the pause menu: the world is read below, once play resumes

            // Read the world after the wait: the story may have moved on while we were thinking.
            bool hub = m.HubOpen; var beat = m.Beat; var open = m.story.OpenQuestion;   // Question is a plain class, so ?. is safe on it
            hintOnly = beat == Beat.Assessment || open != null;
            if (hintOnly) remote = null;   // a card opened while the backend was answering: no spoilers
            string task = hintOnly || hub ? null : hud.CurrentTask;   // the story is waiting on the learner: say exactly that (hub and question cards keep their own nudges)
            string reply = !string.IsNullOrWhiteSpace(remote) ? Clean(remote) : CurioKnowledge.Reply(q, beat, hub, m.story.ContextTopic, m.clues, hintOnly, open?.concept, task);
            // "Show me / take me to / where ..." acts anywhere; a plain "what is X?" only acts where navigating is harmless (hub / free explore).
            // While a card is open only "explain again" may act: no demo or callout is allowed to answer the question.
            bool act = q.intent == Intent.ExplainAgain || open == null && q.intent >= Intent.ShowCO2 && (q.show || hub);
            bool wasSpeaking = dlg.Speaking;
            string extra = null;
            if (act)
            {
                try { extra = m.story.WorldAction(q.intent); }
                catch (Exception e) { Debug.LogException(e); act = false; }   // a world glitch must not leave Curio stuck "thinking"
            }
            bool replay = q.intent == Intent.ExplainAgain && !wasSpeaking && dlg.Speaking;   // Story re-says its last scripted line
            if (replay) reply = "Of course — listen again.";
            if (!string.IsNullOrEmpty(extra)) reply += " " + extra;
            hud.AddChat(false, reply);
            Remember("assistant", reply);
            var action = act ? Describe(q.intent, extra, hub, beat, replay) : null;
            if (action != null) hud.ShowWorldAction("WORLD ACTION · " + action);
            Emote(CurioEmote.Nod);

            if (replay) { SetAnswerState(VoiceState.Speaking); var line = dlg.LastLine; while (dlg.Speaking && dlg.LastLine == line) yield return null; }
            else yield return Speak(CurioKnowledge.Spoken(reply));
            answering = null;
            SetAnswerState(VoiceState.Idle);
        }

        /// Speaks through Dialogue (subtitles + device TTS) without trampling scripted narration: waits up to 6 s for a pause
        /// (longer for our own previous reply, which ends by itself). Never runs two Dialogue lines at once.
        /// ponytail: if the story keeps talking the chat bubble carries the answer; upgrade path = a Dialogue queue with priorities.
        IEnumerator Speak(string text)
        {
            var m = Module.I; var dlg = m.dialogue;
            if (voice.State != CurioVoice.Phase.Idle) yield break;   // the learner is already asking something new: chat carries this one
            bool mine = MineSpeaking;
            if (mine) m.audioDirector.StopVoice();   // a newer answer replaces an older one; its line closes after a short hold
            for (float t0 = Time.unscaledTime, limit = mine ? 16f : 6f; dlg.Speaking && Time.unscaledTime - t0 < limit;) yield return null;
            if (dlg.Speaking || voice.State != CurioVoice.Phase.Idle) yield break;
            SetAnswerState(VoiceState.Speaking);
            spokenReply = text;
            dlg.StartCoroutine(dlg.SayText(text));   // runs on Dialogue so cancelling this answer can't leave it stuck mid-line
            while (dlg.Speaking && dlg.LastLine == text) yield return null;
        }

        bool MineSpeaking => spokenReply != null && Module.I.dialogue.Speaking && Module.I.dialogue.LastLine == spokenReply;

        void Emote(CurioEmote e) { var c = Module.I.curio; if (c && c.isActiveAndEnabled) c.Emote(e); }

        void Remember(string role, string text)
        {
            history.Add(new Turn { role = role, text = text });
            while (history.Count > HistoryMax) history.RemoveAt(0);
        }

        /// Short label for the HUD "WORLD ACTION" tag, or null when Story.WorldAction did nothing / deferred.
        /// ponytail: mirrors Story.WorldAction's branches; upgrade path = WorldAction reports what it did.
        static string Describe(Intent i, string extra, bool hub, Beat beat, bool replay)
        {
            if (i == Intent.ExplainAgain) return replay ? "Replaying Dr. Curio's last explanation" : null;
            if (extra != null && (extra.Contains("later") || extra.Contains("finish") || extra.Contains("soon") || extra.Contains("first") || extra.Contains("finale"))) return null;
            if (beat == Beat.Reaction && (i == Intent.ShowCO2 || i == Intent.ShowWater)) return "Sending CO₂ and water into the chloroplast";   // Scene 10 feed assist (every other Reaction path for these says "finish…")
            bool launch = extra != null && extra.Contains("Follow me"), explore = beat >= Beat.FreeExplore;
            switch (i)
            {
                case Intent.ShowCO2:
                case Intent.ShowStomata:
                    if (launch) return "Opening Clue 01 · Carbon dioxide";
                    return beat == Beat.Stomata || beat == Beat.CO2 ? "Highlighting the stoma where CO₂ enters" : "Zooming to the stomata · CO₂ in";
                case Intent.ShowWater:
                case Intent.ShowRoots:
                case Intent.ShowXylem:
                    if (launch) return "Opening Clue 02 · Water";
                    if (beat == Beat.Water && i != Intent.ShowXylem) return extra == null ? "Picking a water molecule for the root to absorb" : null;   // a reply means no pick was forced
                    return explore && i == Intent.ShowRoots ? "Flying down to the roots" : "Showing water rising from the roots through the xylem";
                case Intent.ShowSunlight:
                    return launch ? "Opening Clue 03 · Sunlight" : "Beaming sunlight onto the leaf";
                case Intent.WherePhotosynthesis:
                case Intent.ShowChloroplast:
                case Intent.ShowChlorophyll:
                    if (launch) return "Diving into the leaf";   // Story pressed ENTER LEAF for the learner
                    if (explore) return i == Intent.WherePhotosynthesis ? "Zooming into a leaf" : "Diving into a chloroplast";
                    return beat < Beat.EnterLeaf ? "Highlighting the leaf where food is made" : null;   // hubs before EnterLeaf only highlight: no spoilers
                case Intent.ShowLeaf:
                case Intent.ShowCell:
                case Intent.ShowStem:
                    if (launch) return "Diving into the leaf";
                    if (explore) return i == Intent.ShowLeaf ? "Showing the leaf" : i == Intent.ShowCell ? "Entering a plant cell" : "Showing the stem";
                    return i == Intent.ShowLeaf && beat < Beat.EnterLeaf ? "Highlighting the leaf where food is made" : null;
                case Intent.ShowOxygen:
                    return explore ? "Following oxygen out through a stoma" : hub ? "Showing oxygen leaving through the stomata" : null;
                case Intent.ShowGlucose:
                case Intent.ShowPhloem:
                    if (explore) return i == Intent.ShowGlucose ? "Following food through the phloem" : "Showing the phloem highway";
                    return hub ? "Showing food travelling through the phloem" : null;
                case Intent.ShowEquation:
                    return hub ? "Building the photosynthesis equation" : null;
            }
            return null;
        }

        // ------------------------------------------------------------------ optional backend (Wattson contract)
        [Serializable] class Ctx { public string scene, beat, topic; public string[] clues; }
        [Serializable] class Req { public string message; public Ctx context; public List<Turn> history; }
        [Serializable] class Res { public string reply = ""; }

        bool ValidEndpoint => !string.IsNullOrWhiteSpace(endpoint) && Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var u)
                              && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp && u.IsLoopback);

        IEnumerator Remote(string message, List<Turn> past, Action<string> done)
        {
            var m = Module.I; var c = m.clues;
            var body = new Req
            {
                message = message, history = past,
                context = new Ctx
                {
                    scene = SceneManager.GetActiveScene().name, beat = m.Beat.ToString(), topic = m.story.ContextTopic,
                    clues = new[] { "CO2:" + c[ClueId.CO2], "Water:" + c[ClueId.Water], "Sunlight:" + c[ClueId.Sunlight] }
                }
            };
            web = new UnityWebRequest(endpoint.Trim(), UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body))),
                downloadHandler = new DownloadHandlerBuffer(), timeout = TimeoutSeconds
            };
            web.SetRequestHeader("Content-Type", "application/json");
            yield return web.SendWebRequest();
            string reply = null;
            if (web.result == UnityWebRequest.Result.Success && web.downloadHandler.data != null && web.downloadHandler.data.Length < 64000)
            {
                try { reply = JsonUtility.FromJson<Res>(web.downloadHandler.text)?.reply; }
                catch (ArgumentException) { }
            }
            else Debug.LogWarning($"[AskAI] Backend unavailable ({web.error}) — using Dr. Curio's offline reply.");
            CancelWeb();
            done(reply);
        }

        /// Plain text only (no UI rich-text tags), kept short.
        static string Clean(string s)
        {
            s = s.Replace("<", "").Replace(">", "").Trim();
            return s.Length > 700 ? s.Substring(0, 700).TrimEnd() + "…" : s;
        }

        void DropAnswer() { voice.Cancel(); CancelAnswer(); ShowState(); }   // Restart: a held or in-flight answer belongs to the old run

        void CancelAnswer()
        {
            if (answering != null) StopCoroutine(answering);
            answering = null; answerState = VoiceState.Idle; CancelWeb();
        }

        void CancelWeb()
        {
            if (web == null) return;
            web.Abort(); web.Dispose(); web = null;
        }
    }
}
