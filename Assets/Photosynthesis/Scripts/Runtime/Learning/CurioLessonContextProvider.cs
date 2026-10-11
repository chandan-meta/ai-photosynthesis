using System;
using UnityEngine.SceneManagement;

namespace Photosynthesis
{
    [Serializable]
    public class CurioLessonContextProvider
    {
        public string scene, beat, topic, task, language;
        public string[] clues;
        public bool hintOnly;

        public static CurioLessonContextProvider Capture(string language)
        {
            var m = Module.I;
            return new CurioLessonContextProvider {
                scene = SceneManager.GetActiveScene().name, beat = m.Beat.ToString(),
                topic = m.story.ContextTopic, task = m.hud.CurrentTask, language = language,
                hintOnly = m.Beat == Beat.Assessment || m.story.OpenQuestion != null,
                clues = new[] { "CO2:" + m.clues[ClueId.CO2], "Water:" + m.clues[ClueId.Water], "Sunlight:" + m.clues[ClueId.Sunlight] }
            };
        }
    }
}
