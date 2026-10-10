using UnityEngine;
using UnityEngine.Audio;

namespace Photosynthesis
{
    [CreateAssetMenu(menuName = "Photosynthesis/Soundtrack")]
    public sealed class Soundtrack : ScriptableObject
    {
        public AudioClip cinematic, exploration, water, scientific, playful, completion, forest;
        public AudioMixerGroup musicGroup, voiceGroup, sfxGroup, ambienceGroup;
        [Range(0, 1)] public float musicLevel = .25f; // Existing 0.6 user setting gives 0.15 source gain.
        [Range(0, 1)] public float ambienceLevel = .125f;
        [Range(1.5f, 2.5f)] public float crossfade = 2f;
        [Range(-12, -8)] public float voiceDuckDb = -10f;
        [Range(.15f, .3f)] public float duckAttack = .2f;
        [Range(.6f, 1.2f)] public float duckRelease = .9f;
        public AudioClip Track(MusicState state) => state switch
        {
            MusicState.NewWorld or MusicState.SunlightReveal => cinematic,
            MusicState.WaterJourney => water,
            MusicState.Reaction => scientific,
            MusicState.Playful => playful,
            MusicState.Resolution => completion,
            MusicState.None => null,
            _ => exploration
        };
    }
}
