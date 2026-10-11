using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Photosynthesis
{
    public enum Intent
    {
        None, Greeting, WhatIsThis, ExplainAgain, WhyNeed, WhatNext, Help, Hint,
        ShowCO2, ShowStomata, ShowWater, ShowRoots, ShowXylem, ShowSunlight, WherePhotosynthesis,
        ShowChloroplast, ShowChlorophyll, ShowGlucose, ShowOxygen, ShowPhloem, ShowEquation,
        ShowLeaf, ShowCell, ShowStem
    }

    /// Owns one conversation and borrows the existing lesson, audio, UI and character systems.
    public class AskAI : MonoBehaviour
    {
        public string micDevice = "";
        [Serializable] public class Turn { public string role, text; }
        [Serializable] class Request { public string message; public CurioLessonContextProvider context; public List<Turn> history; }
        [Serializable] class Reply { public string reply; }
        public IReadOnlyList<Turn> History => history;
        public float MicLevel => realtime && live ? realtime.MicLevel : voice.Level;
        public bool ConversationHeld { get; private set; }
        public bool LiveSpeaking { get; private set; }
        public float LiveLevel => LiveSpeaking && !muted ? realtime.Level : 0;
        public bool BrowserVoice => live;
        readonly List<Turn> history = new List<Turn>();
        readonly CurioVoice voice = new CurioVoice();
        CurioAISettings settings;
        CurioRealtimeClient realtime;
        Hud hud;
        Coroutine answer;
        UnityWebRequest request;
        bool wired, wasOpen, live, muted, savedCamera;
        float savedScale, openedAt;
        Beat heldBeat;
        string pending, streamText = "";

        void Start()
        {
            settings = CurioAISettings.Load(); hud = Module.I.hud;
            realtime = Module.Ensure<CurioRealtimeClient>(gameObject);
            realtime.Received += OnRealtime;
            hud.ChatSubmitted += Ask; hud.MicDown += MicDown; hud.MicUp += MicUp;
            hud.ResetRequested += ResetConversation; hud.ChatClosed += Close;
            hud.VoiceToggled += ToggleMute;
            voice.Heard += Ask; voice.Failed += Notice;
            wired = true;
        }

        void Update()
        {
            if (!wired) return;
            voice.Tick();
            if (!hud.ChatOpen) { if (wasOpen) Close(); wasOpen = false; return; }
            if (!wasOpen)
            {
                wasOpen = true;
                // A submitted question or a dialogue boundary can acquire the pause before Update runs.
                if (!ConversationHeld) hud.SetAIStatus("Waiting for a safe pause in the lesson…");
            }
            if (hud.Paused || Module.I.AtStartScreen) { if (ConversationHeld) hud.CloseChat(); return; }
            if (!ConversationHeld)
            {
                if (hud.Cinematic) hud.SetAIStatus("A story moment is in progress. Curio will be ready afterward.");
                else TryHold();
            }
            if (ConversationHeld && (Module.I.Beat != heldBeat || Time.unscaledTime - openedAt > Mathf.Clamp(settings.sessionSeconds, 30, 300)))
            { hud.CloseChat(); return; }
            if (voice.State == CurioVoice.Phase.Recording) hud.SetVoiceState(VoiceState.Listening);
            else if (voice.State == CurioVoice.Phase.Transcribing) { hud.SetVoiceState(VoiceState.Transcribing); hud.SetAIStatus("Recognizing your question…"); }
        }

        public bool TryHold()
        {
            if (!wired || !hud.ChatOpen || hud.Paused || hud.Cinematic || Module.I.AtStartScreen) return false;
            if (ConversationHeld) return true;
            if (Module.I.dialogue.Speaking) return false;
            ConversationHeld = true; heldBeat = Module.I.Beat; openedAt = Time.unscaledTime;
            savedScale = Time.timeScale; savedCamera = Module.I.cam.UserControl;
            Time.timeScale = 0; Module.I.cam.UserControl = false;
            live = !settings.mock && settings.Configured && CurioRealtimeClient.Supported;
            Module.I.curio.LookAtCamera();
            hud.SetAIStatus(settings.mock ? "Offline practice · ready to ask" : "Ready to ask");
            if (live) { hud.SetAIStatus("Connecting… typed questions remain available"); realtime.Connect(settings, CurioLessonContextProvider.Capture(settings.language)); }
            if (pending != null && !live) { var text = pending; pending = null; Ask(text); }
            return true;
        }

        public void Ask(string text)
        {
            if (!wired || string.IsNullOrWhiteSpace(text)) return;
            if (!hud.ChatOpen) hud.OpenChat();
            text = text.Trim(); if (text.Length > 500) text = text.Substring(0, 500);
            hud.AddChat(true, text);
            if (!TryHold()) { pending = text; return; }
            CancelAnswer();
            if (Module.I.story.OpenQuestion != null) Module.I.story.NoteHelp();
            if (live)
            {
                if (!realtime.Ready) { pending = text; return; }
                Remember("user", text); realtime.Command("ask", text); return;
            }
            answer = StartCoroutine(Answer(text));
        }

        IEnumerator Answer(string text)
        {
            hud.SetAIStatus("Finding a lesson answer…");
            hud.SetVoiceState(VoiceState.Thinking); Module.I.curio.Emote(CurioEmote.Think);
            string reply = null;
            var context = CurioLessonContextProvider.Capture(settings.language);
            var past = new List<Turn>(history); Remember("user", text);
            string url = settings.backendUrl.TrimEnd('/') + "/chat";
            if (!settings.mock && settings.Configured && !context.hintOnly)
            {
                request = new UnityWebRequest(url, "POST") {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Request { message = text, context = context, history = past }))),
                    downloadHandler = new DownloadHandlerBuffer(), timeout = 25
                };
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success && request.downloadHandler.data.Length < 32000)
                    try { reply = JsonUtility.FromJson<Reply>(request.downloadHandler.text)?.reply; } catch (ArgumentException) { }
                request.Dispose(); request = null;
                if (string.IsNullOrWhiteSpace(reply)) Notice("Connection unavailable. Here is an offline lesson answer; try again when connected.");
            }
            if (string.IsNullOrWhiteSpace(reply))
            {
                var m = Module.I;
                reply = CurioKnowledge.Reply(IntentRouter.Parse(text), m.Beat, m.HubOpen, m.story.ContextTopic, m.clues,
                    context.hintOnly, m.story.OpenQuestion?.concept, hud.CurrentTask);
            }
            reply = Plain(reply); Remember("assistant", reply); hud.AddChat(false, reply);
            Module.I.curio.Emote(CurioEmote.Nod);
            if (!muted)
            {
                var clip = Dialogue.KbClip(CurioKnowledge.Spoken(reply));
                if (clip) Module.I.audioDirector.PlayVoice(clip); else Module.I.audioDirector.Speak(CurioKnowledge.Spoken(reply));
                hud.SetVoiceState(VoiceState.Speaking); Module.I.curio.Talking = true;
                while (Module.I.audioDirector.VoicePlaying) yield return null;
            }
            Module.I.curio.Talking = false; hud.SetVoiceState(VoiceState.Idle); answer = null;
            hud.SetAIStatus(settings.mock ? "Offline practice · ready to ask" : "Ready to ask");
        }

        void MicDown()
        {
            if (!TryHold()) { Notice("I'll listen after this explanation. You can type your question now."); return; }
            CancelAnswer();
            if (live) { if (realtime.Ready) realtime.Command("mic", "on"); else Notice("Connecting — hold the microphone again when ready."); }
            else { voice.Begin(micDevice); if (voice.State == CurioVoice.Phase.Recording) hud.SetAIStatus("Listening… hold the microphone while you speak, then release."); }
        }
        void MicUp() { if (live) realtime.Command("mic", "off"); else voice.End(); }
        void ToggleMute(bool value)
        {
            muted = value; if (live) realtime.Command("mute", value ? "on" : "off");
            else if (value) Module.I.audioDirector.StopVoice();
        }
        void OnRealtime(CurioRealtimeClient.Event e)
        {
            if (!ConversationHeld) return;
            switch (e.type)
            {
                case "ready":
                    hud.SetAIStatus("Ready to ask · hold the microphone or type");
                    realtime.Command("mute", muted ? "on" : "off");
                    if (pending != null) { var text = pending; pending = null; Ask(text); }
                    break;
                case "listening": hud.SetVoiceState(VoiceState.Listening); hud.SetAIStatus("Listening… release to send"); Module.I.curio.Emote(CurioEmote.Nod); break;
                case "thinking": hud.SetVoiceState(VoiceState.Thinking); hud.SetAIStatus("Finding a lesson answer…"); Module.I.curio.Emote(CurioEmote.Think); streamText = ""; break;
                case "question": hud.AddChat(true, Plain(e.text)); Remember("user", Plain(e.text)); if (Module.I.story.OpenQuestion != null) Module.I.story.NoteHelp(); break;
                case "delta": streamText = Plain(e.text); hud.SetAITranscript(streamText); break;
                case "answer": hud.AddChat(false, Plain(e.text)); Remember("assistant", Plain(e.text)); hud.SetAITranscript(""); break;
                case "speaking": LiveSpeaking = true; hud.SetVoiceState(VoiceState.Speaking); hud.SetAIStatus("Dr. Curio is speaking"); break;
                case "stopped": LiveSpeaking = false; hud.SetVoiceState(VoiceState.Idle); hud.SetAIStatus("Ready to ask"); break;
                case "notice": Notice(e.text); break;
                case "error":
                    LiveSpeaking = false; live = false; hud.SetVoiceState(VoiceState.Idle);
                    Notice("Live voice is unavailable. Type a question to retry using text, or close and reopen to reconnect.");
                    if (pending != null) { var text = pending; pending = null; Ask(text); }
                    break;
                case "closed": hud.CloseChat(); break;
                case "latency": Debug.Log($"[Curio] First audible response: {e.firstAudioMs:0} ms; total answer: {e.totalMs:0} ms"); break;
            }
        }
        static string Plain(string text) { text = (text ?? "").Replace("<", "").Replace(">", ""); return text.Length > 4000 ? text.Substring(0, 4000) : text; }
        void Remember(string role, string text) { history.Add(new Turn { role = role, text = text }); while (history.Count > 12) history.RemoveAt(0); }
        void Notice(string text) { if (hud) hud.SetAIStatus(text); }
        void CancelAnswer()
        {
            if (answer != null) StopCoroutine(answer); answer = null;
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (ConversationHeld) { Module.I.audioDirector.StopVoice(); Module.I.curio.Talking = false; }
        }
        void ResetConversation() { if (hud) hud.CloseChat(); Close(); history.Clear(); }
        void Close()
        {
            wasOpen = false;
            pending = null; voice.Cancel(); CancelAnswer();
            if (realtime) realtime.Close(); live = false; LiveSpeaking = false;
            if (ConversationHeld)
            {
                ConversationHeld = false;
                if (Module.I && Module.I.cam) Module.I.cam.UserControl = savedCamera;
                // A pause menu owns timeScale until it closes; tell it the original lesson speed.
                if (hud && hud.Paused) hud.RestoreLessonSpeed(savedScale); else Time.timeScale = savedScale;
            }
            if (hud) { hud.SetVoiceState(VoiceState.Idle); hud.SetAITranscript(""); }
        }
        void OnApplicationFocus(bool focus) { if (!focus && hud && hud.ChatOpen) hud.CloseChat(); }
        void OnDisable() { if (wired) Close(); }
        void OnDestroy()
        {
            if (hud) { hud.ChatSubmitted -= Ask; hud.MicDown -= MicDown; hud.MicUp -= MicUp; hud.ResetRequested -= ResetConversation; hud.ChatClosed -= Close; hud.VoiceToggled -= ToggleMute; }
            if (realtime) realtime.Received -= OnRealtime;
            voice.Dispose();
        }
    }
}
