using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Photosynthesis
{
    public class CurioRealtimeClient : MonoBehaviour
    {
        [Serializable] public class Event { public string type, text; public float level, mic, firstAudioMs, totalMs; }
        public event Action<Event> Received;
        public bool Ready { get; private set; }
        public float Level { get; private set; }
        public float MicLevel { get; private set; }
        public static bool Supported {
            get {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CurioConnect(string target, string url, string context, int seconds);
        [DllImport("__Internal")] static extern void CurioCommand(string type, string value);
#endif
        public void Connect(CurioAISettings settings, CurioLessonContextProvider context)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            CurioConnect(gameObject.name, settings.backendUrl.TrimEnd('/'), JsonUtility.ToJson(context), settings.sessionSeconds);
#endif
        }
        public void Command(string type, string value = "")
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            CurioCommand(type, value);
#endif
        }
        // Called only by the browser bridge; model output is never interpreted as commands.
        public void OnCurioEvent(string json)
        {
            Event e;
            try { e = JsonUtility.FromJson<Event>(json); } catch (ArgumentException) { return; }
            if (e == null) return;
            if (e.type == "ready") Ready = true;
            if (e.type == "closed" || e.type == "error") Ready = false;
            if (e.type == "level") { Level = Mathf.Clamp01(e.level); MicLevel = Mathf.Clamp01(e.mic); }
            if (e.type == "closed" || e.type == "error" || e.type == "stopped") Level = MicLevel = 0;
            Received?.Invoke(e);
        }
        public void Close() { Command("close"); Ready = false; Level = MicLevel = 0; }
        void OnDisable() => Close();
    }
}
