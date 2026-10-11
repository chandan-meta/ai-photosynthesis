using System;
using UnityEngine;

namespace Photosynthesis
{
    [Serializable]
    public class CurioAISettings
    {
        public string backendUrl = "";
        public bool mock = true;
        public string language = "en";
        public int sessionSeconds = 180;

        public static CurioAISettings Load()
        {
            var file = Resources.Load<TextAsset>("curio_ai_settings");
            return file ? JsonUtility.FromJson<CurioAISettings>(file.text) : new CurioAISettings();
        }
        public bool Configured => Uri.TryCreate(backendUrl, UriKind.Absolute, out var u)
            && (u.Scheme == "https" || u.Scheme == "http" && u.IsLoopback);
    }
}
