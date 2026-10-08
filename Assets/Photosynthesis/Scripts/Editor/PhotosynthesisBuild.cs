using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Photosynthesis.EditorTools
{
    /// One-click (or batch-mode) construction of the Scene 2+ module scene and its shared assets.
    ///   Menu: Photosynthesis ▸ Build Scene / Build Windows Player
    ///   Batch: Unity.exe -batchmode -projectPath . -executeMethod Photosynthesis.EditorTools.PhotosynthesisBuild.BuildAll -quit
    /// Scene 1 is never touched: this only writes Assets/Photosynthesis/Scenes/Scene02_BotanicalChamber.unity.
    public static partial class PhotosynthesisBuild
    {
        public const string Root = "Assets/Photosynthesis";
        public const string ScenePath = Root + "/Scenes/Scene02_BotanicalChamber.unity";
        const string MatDir = Root + "/Materials/Templates";

        /// Implemented in ChamberBuilder.cs: place dome + vegetation + displays under the chamber and assign refs.
        static partial void PopulateChamber(Chamber chamber);
        /// Implemented in ChamberBuilder.cs: importer settings for the dome FBX / vegetation GLB.
        static partial void ConfigureImports();

        [MenuItem("Photosynthesis/Build Scene", priority = 0)]
        public static void BuildAll()
        {
            ConfigureImports();
            BuildMaterialLibrary();
            BuildPanelSettings();
            BuildScene();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("[Photosynthesis] Scene built: " + ScenePath);
        }

        [MenuItem("Photosynthesis/Build Windows Player (development)", priority = 20)]
        public static void BuildPlayer() => Build("Builds/Win/Photosynthesis.exe", BuildOptions.Development);

        /// What students get: no watermark, no profiler connection, no F1 panel. Batch: -executeMethod Photosynthesis.EditorTools.PhotosynthesisBuild.BuildPlayerRelease
        /// Leave the *_BurstDebugInformation_DoNotShip folder out of the zip given to schools.
        [MenuItem("Photosynthesis/Build Windows Player (release)", priority = 21)]
        public static void BuildPlayerRelease() => Build("Builds/WinRelease/Photosynthesis.exe", BuildOptions.None);

        static void Build(string path, BuildOptions options)
        {
            if (!File.Exists(ScenePath)) BuildAll();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = options
            });
            Debug.Log($"[Photosynthesis] Player build ({options}) -> {path}: {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        // ------------------------------------------------------------------ materials
        static void BuildMaterialLibrary()
        {
            Directory.CreateDirectory(MatDir); Directory.CreateDirectory(Root + "/Resources");
            const string lit = "Universal Render Pipeline/Lit", unlit = "Universal Render Pipeline/Unlit", particles = "Universal Render Pipeline/Particles/Unlit";
            var libPath = Root + "/Resources/PhotosynthesisMaterials.asset";
            var lib = AssetDatabase.LoadAssetAtPath<MatLib>(libPath);
            if (!lib) { lib = ScriptableObject.CreateInstance<MatLib>(); AssetDatabase.CreateAsset(lib, libPath); }
            lib.lit = Mat("Lit", lit, false, 0, 2, false, 0.45f);
            lib.litEmissive = Mat("LitEmissive", lit, false, 0, 2, true, 0.5f);
            lib.litTransparent = Mat("LitTransparent", lit, true, 0, 2, true, 0.6f);
            lib.glass = Mat("Glass", lit, true, 0, 0, true, 0.96f);
            lib.unlit = Mat("Unlit", unlit, false, 0, 2, false, 0);
            lib.unlitTransparent = Mat("UnlitTransparent", unlit, true, 0, 0, false, 0);
            lib.additive = Mat("Additive", unlit, true, 2, 0, false, 0);
            lib.leaf = Mat("Leaf", lit, false, 0, 0, true, 0.55f);
            lib.particleAdditive = Mat("ParticleAdditive", particles, true, 2, 0, false, 0);
            lib.particleAlpha = Mat("ParticleAlpha", particles, true, 0, 0, false, 0);
            EditorUtility.SetDirty(lib);
        }

        /// blend: 0 alpha · 2 additive. cull: 0 off (double-sided) · 2 back.
        static Material Mat(string name, string shader, bool transparent, int blend, int cull, bool emission, float smoothness)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find(shader);
            m.SetFloat("_Surface", transparent ? 1 : 0);
            m.SetFloat("_Blend", blend);
            m.SetFloat("_Cull", cull);
            m.SetFloat("_AlphaClip", 0);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_ReceiveShadows")) m.SetFloat("_ReceiveShadows", transparent ? 0 : 1);
            m.SetColor("_BaseColor", Color.white);
            if (emission) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
            UnityEditor.BaseShaderGUI.SetupMaterialBlendMode(m);
            UnityEditor.BaseShaderGUI.SetMaterialKeywords(m);
            if (emission) m.EnableKeyword("_EMISSION");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ UI Toolkit
        static void BuildPanelSettings()
        {
            var tss = Root + "/Resources/PhotosynthesisTheme.tss";
            if (!File.Exists(tss)) { File.WriteAllText(tss, "@import url(\"unity-theme://default\");\n"); AssetDatabase.ImportAsset(tss); }
            var path = Root + "/Resources/PhotosynthesisPanel.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (!ps) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, path); }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tss);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
        }

        // ------------------------------------------------------------------ scene
        static void BuildScene()
        {
            Directory.CreateDirectory(Root + "/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Sky & ambient (trilight needs no baking → identical in editor and builds)
            var sky = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Sky.mat");
            if (!sky) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, Root + "/Materials/Sky.mat"); }
            sky.SetFloat("_SunSize", 0.045f); sky.SetFloat("_SunSizeConvergence", 6f); sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", new Color(0.55f, 0.72f, 0.82f)); sky.SetColor("_GroundColor", new Color(0.28f, 0.33f, 0.27f)); sky.SetFloat("_Exposure", 1.15f);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.70f); RenderSettings.ambientEquatorColor = new Color(0.36f, 0.45f, 0.36f); RenderSettings.ambientGroundColor = new Color(0.16f, 0.17f, 0.13f);
            RenderSettings.fog = false;
            Lightmapping.bakedGI = false; Lightmapping.realtimeGI = false;

            // Sun
            var sunGo = new GameObject("Sun"); var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1f, 0.93f, 0.8f); sun.intensity = 2.2f; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.8f;
            sunGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            RenderSettings.sun = sun;

            // Post-processing
            var profilePath = Root + "/Settings/ChamberVolume.asset"; Directory.CreateDirectory(Root + "/Settings");
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile) AssetDatabase.DeleteAsset(profilePath);
            profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, profilePath);
            T Fx<T>() where T : VolumeComponent { var c = profile.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, profile); return c; }
            var bloom = Fx<Bloom>(); bloom.threshold.Override(1.0f); bloom.intensity.Override(0.75f); bloom.scatter.Override(0.68f); bloom.tint.Override(new Color(1f, 0.95f, 0.85f));
            var tone = Fx<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            var ca = Fx<ColorAdjustments>(); ca.postExposure.Override(0.25f); ca.contrast.Override(14f); ca.saturation.Override(12f);
            var vig = Fx<Vignette>(); vig.intensity.Override(0.26f); vig.smoothness.Override(0.45f);
            var wb = Fx<WhiteBalance>(); wb.temperature.Override(6f);
            var dof = Fx<DepthOfField>(); dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focusDistance.Override(8f); dof.aperture.Override(5.6f); dof.focalLength.Override(50f);
            dof.active = false;   // CameraDirector switches it on for scripted shots only (High quality), focused on the shot's subject
            var volGo = new GameObject("Global Volume"); var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = profile;

            // Camera
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.03f; cam.farClipPlane = 900f; cam.fieldOfView = 50f; cam.allowHDR = true;
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.stopNaN = true;   // one bad pixel must never bloom across the frame
            var director = camGo.AddComponent<CameraDirector>();
            camGo.transform.position = new Vector3(0, 2.6f, -11.5f); camGo.transform.LookAt(new Vector3(0, 2f, 0));

            // UI input
            var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            // Chamber
            var chamberGo = new GameObject("Botanical Investigation Chamber");
            var chamber = chamberGo.AddComponent<Chamber>(); chamber.sun = sun; chamber.volume = vol;
            var probe = new GameObject("Reflection Probe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(chamberGo.transform, false); probe.transform.localPosition = new Vector3(0, 3f, 0);
            probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting; probe.size = new Vector3(40, 20, 40); probe.resolution = 256;
            PopulateChamber(chamber);

            // Module
            var moduleGo = new GameObject("Photosynthesis Module");
            var module = moduleGo.AddComponent<Module>(); module.chamber = chamber; module.cam = director;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigurePlayer()
        {
            // Fog is off in the chamber, so automatic stripping would remove it from every shader; micro worlds need it.
            var gs = new SerializedObject(AssetDatabase.LoadMainAssetAtPath("ProjectSettings/GraphicsSettings.asset"));
            gs.FindProperty("m_FogStripping").intValue = 1;
            gs.FindProperty("m_FogKeepLinear").boolValue = true; gs.FindProperty("m_FogKeepExp").boolValue = true; gs.FindProperty("m_FogKeepExp2").boolValue = true;
            gs.ApplyModifiedPropertiesWithoutUndo();
            PlayerSettings.productName = "Photosynthesis — The Hidden Food Factory";
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true; PlayerSettings.resizableWindow = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
        }
    }
}
