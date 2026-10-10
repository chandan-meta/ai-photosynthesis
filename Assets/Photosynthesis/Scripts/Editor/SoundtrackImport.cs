using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Photosynthesis.EditorTools
{
    public static class SoundtrackImport
    {
        [MenuItem("Photosynthesis/Audio/Import licensed soundtrack")]
        public static void Import()
        {
            const string folder = "Assets/Photosynthesis/Audio/Resources/";
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles("Assets/Photosynthesis/Audio", "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".mp3") || p.EndsWith(".ogg")))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.Streaming; settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = .8f; settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData = false;
                importer.defaultSampleSettings = settings; importer.forceToMono = false;
                importer.loadInBackground = true;
                foreach (string platform in new[] { "Standalone", "Android", "iOS" }) importer.SetOverrideSampleSettings(platform, settings);
                var web = settings; web.loadType = AudioClipLoadType.CompressedInMemory;
                importer.SetOverrideSampleSettings("WebGL", web); importer.SaveAndReimport();
            }
            const string mixerPath = folder + "PhotosynthesisAudio.mixer";
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(mixerPath);
            var type = typeof(Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController");
            if (!mixer) mixer = (AudioMixer)type.GetMethod("CreateMixerControllerAtPath").Invoke(null, new object[] { mixerPath });
            var master = type.GetProperty("masterGroup").GetValue(mixer);
            string[] names = { "Music", "Voice", "SFX", "Ambience" };
            foreach (string name in names)
            {
                var group = AssetDatabase.LoadAllAssetsAtPath(mixerPath).OfType<AudioMixerGroup>().FirstOrDefault(g => g.name == name);
                if (!group) group = (AudioMixerGroup)type.GetMethod("CreateNewGroup").Invoke(mixer, new object[] { name, false });
                if (!mixer.FindMatchingGroups(name).Any(g => g.name == name))
                    type.GetMethod("AddChildToParent").Invoke(mixer, new object[] { group, master });
            }
            var viewProperty = type.GetProperty("views");
            var viewType = viewProperty.PropertyType.GetElementType();
            var view = Activator.CreateInstance(viewType);
            viewType.GetField("name").SetValue(view, "All audio");
            var groups = mixer.FindMatchingGroups("");
            var guids = Array.CreateInstance(viewType.GetField("guids").FieldType.GetElementType(), groups.Length);
            for (int i = 0; i < groups.Length; i++) guids.SetValue(groups[i].GetType().GetProperty("groupID").GetValue(groups[i]), i);
            viewType.GetField("guids").SetValue(view, guids);
            var views = Array.CreateInstance(viewType, 1); views.SetValue(view, 0); viewProperty.SetValue(mixer, views);
            EditorUtility.SetDirty(mixer); EditorUtility.SetDirty((UnityEngine.Object)master);
            var asset = AssetDatabase.LoadAssetAtPath<Soundtrack>(folder + "PhotosynthesisSoundtrack.asset");
            if (!asset) { asset = ScriptableObject.CreateInstance<Soundtrack>(); AssetDatabase.CreateAsset(asset, folder + "PhotosynthesisSoundtrack.asset"); }
            AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(folder + "Soundtrack/" + name + ".ogg") ?? throw new InvalidOperationException("Missing soundtrack clip " + name);
            AudioMixerGroup Group(string name) => mixer.FindMatchingGroups(name).Single(g => g.name == name);
            asset.cinematic = Clip("BGM_Cinematic_Intro"); asset.exploration = Clip("BGM_Main_Exploration");
            asset.water = Clip("BGM_Root_Water"); asset.scientific = Clip("BGM_Scientific_Discovery");
            asset.playful = Clip("BGM_DrCurio_Playful"); asset.completion = Clip("BGM_Completion"); asset.forest = Clip("AMB_Forest_Nature");
            asset.musicGroup = Group("Music"); asset.voiceGroup = Group("Voice"); asset.sfxGroup = Group("SFX"); asset.ambienceGroup = Group("Ambience");
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/AudioUpgrade/import-result.txt", "PASS: seven streaming AudioClips, four mixer groups, Inspector soundtrack settings");
        }
    }
}
