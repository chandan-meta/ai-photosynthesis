using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace Photosynthesis
{
    // Launch-only QA: -audiocheck observes gameplay; -audiotest also exercises routing, loops and pause.
    public sealed class AudioReview : MonoBehaviour
    {
        AudioDirector director; AudioMixProbe probe; float next; int failures;
        readonly float[] block = new float[1024];
        float Rms(AudioSource source)
        {
            if (!source.isPlaying) return 0;
            source.GetOutputData(block, 0); float sum = 0;
            foreach (float sample in block) sum += sample * sample;
            return Mathf.Sqrt(sum / block.Length);
        }
        void Check(bool ok, string description)
        {
            if (!ok) { failures++; Debug.LogError("[AudioQA] FAIL " + description); }
            else Debug.Log("[AudioQA] PASS " + description);
        }
        IEnumerator Start()
        {
            director = Module.I.audioDirector;
            probe = Camera.main.gameObject.AddComponent<AudioMixProbe>();
            yield return new WaitForSecondsRealtime(1f);
            var config = director.soundtrack;
            Check(config && new[] { config.cinematic, config.exploration, config.water, config.scientific, config.playful, config.completion, config.forest }.All(c => c && c.loadType == AudioClipLoadType.Streaming), "seven streaming clips");
            Check(FindObjectsByType<AudioDirector>(FindObjectsSortMode.None).Length == 1, "single director controller");
            Check(director.MusicSources.All(s => s.outputAudioMixerGroup.name == "Music") && director.AmbienceSources.All(s => s.outputAudioMixerGroup.name == "Ambience") && director.VoiceSource.outputAudioMixerGroup.name == "Voice", "mixer routing");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-audiotest") < 0) yield break;
            Module.I.story.StopForBanner();
            float savedMusic = director.MusicVolume; director.MusicVolume = .6f;
            foreach (var state in new[] { MusicState.NewWorld, MusicState.Investigation, MusicState.WaterJourney, MusicState.Reaction, MusicState.Playful, MusicState.Resolution })
            {
                director.Music(state); yield return new WaitForSecondsRealtime(2.7f);
                var source = director.MusicSources.FirstOrDefault(s => s.clip == config.Track(state) && s.isPlaying);
                Check(source && Rms(source) > .00001f, state + " decoded playback");
                if (!source) continue;
                float before = source.time; director.Music(state); yield return new WaitForSecondsRealtime(.25f);
                Check(source.time > before, state + " repeated request does not restart");
                source.time = source.clip.length - .8f; yield return new WaitForSecondsRealtime(1.3f);
                Check(state == MusicState.Resolution ? !source.isPlaying && !source.loop : source.isPlaying && source.time < 3f, state + (state == MusicState.Resolution ? " completion does not loop" : " streaming loop wraps"));
            }
            director.Music(MusicState.Investigation); yield return new WaitForSecondsRealtime(2.7f);
            var main = director.MusicSources.First(s => s.isPlaying && s.clip == config.exploration);
            float position = main.time; director.Music(MusicState.Microscopic); yield return new WaitForSecondsRealtime(.3f);
            Check(main.time > position, "related exploration states retain position");
            AudioListener.pause = true; position = main.time;
            yield return new WaitForSecondsRealtime(1f);
            Check(Mathf.Abs(main.time - position) < .1f, "pause freezes music position");
            AudioListener.pause = false; yield return new WaitForSecondsRealtime(.3f);
            Check(main.time > position, "resume continues music");
            var voice = Resources.Load<AudioClip>("VO/s5_second"); Check(voice, "original Curio voice available");
            if (voice)
            {
                director.PlayVoice(voice); yield return new WaitForSecondsRealtime(.3f);
                Check(director.MusicDuck <= .34f && director.VoiceSource.isPlaying, "10 dB duck within 0.3 seconds without cutting voice");
                yield return new WaitForSecondsRealtime(voice.length + 1.6f);
                Check(director.MusicDuck >= .99f, "narration release restores previous gain");
            }
            director.Ambience("chamber"); yield return new WaitForSecondsRealtime(1.8f);
            Check(director.AmbienceSources.Any(s => s.isPlaying && s.clip == config.forest), "forest ambience separate from music");
            director.Ambience("soil"); yield return new WaitForSecondsRealtime(2f);
            Check(!director.AmbienceSources.Any(s => s.isPlaying && s.clip == config.forest), "forest stops underground");
            director.MusicVolume = savedMusic; director.StopVoice(); Module.I.ShowStartScreen();
            Check(director.MusicSources.Count(s => s.isPlaying) <= 2, "reset creates no extra music players");
            Check(probe.samples > 0 && probe.peak > .0001f && probe.clipped == 0, "listener output is non-silent and unclipped");
            Debug.Log($"[AudioQA] focused checks complete, failures={failures}");
            yield return new WaitForSecondsRealtime(.5f); Application.Quit();
        }
        void Update()
        {
            if (!director || !probe || Time.unscaledTime < next) return; next = Time.unscaledTime + 2f;
            string tracks = string.Join(" / ", director.MusicSources.Where(s => s.isPlaying).Select(s => $"{s.clip.name}@{s.time:0.0}s gain={s.volume:0.000} rms={Rms(s):0.00000}"));
            Debug.Log($"[AudioQA] beat={Module.I.Beat} voice={director.VoicePlaying} duckDB={20f * Mathf.Log10(Mathf.Max(.0001f, director.MusicDuck)):0.0} tracks={tracks} peak={probe.peak:0.000} clipped={probe.clipped} samples={probe.samples} musicClipKB={new[] { director.soundtrack.cinematic, director.soundtrack.exploration, director.soundtrack.water, director.soundtrack.scientific, director.soundtrack.playful, director.soundtrack.completion, director.soundtrack.forest }.Sum(c => Profiler.GetRuntimeMemorySizeLong(c)) / 1024}");
        }
    }
}
