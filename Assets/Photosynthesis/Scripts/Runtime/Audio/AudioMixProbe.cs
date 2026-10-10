using System;
using UnityEngine;

namespace Photosynthesis
{
    // Test-only listener probe: never changes samples or adds gain to the learner's output.
    public sealed class AudioMixProbe : MonoBehaviour
    {
        public volatile float peak;
        public long samples, clipped;
        void OnAudioFilterRead(float[] data, int channels)
        {
            foreach (float sample in data)
            {
                float value = Math.Abs(sample); if (value > peak) peak = value;
                if (value >= 1f) clipped++;
            }
            samples += data.Length;
        }
    }
}
