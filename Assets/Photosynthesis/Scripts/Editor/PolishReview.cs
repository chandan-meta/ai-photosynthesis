using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Photosynthesis.EditorTools
{
    /// Local editor command inbox lets builds run in the already-open editor without replacing the user's scene.
    [InitializeOnLoad]
    public static class PolishReview
    {
        const string Maps = "Assets/Photosynthesis/Art/Generated/Resources/SurfaceMaps/";
        const string Inbox = "Logs/Polish/editor-command.txt";
        static PolishReview() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying || !File.Exists(Inbox)) return;
            string command = File.ReadAllText(Inbox).Trim(); File.Delete(Inbox);
            try
            {
                switch (command)
                {
                    case "sources": ExportSurfaceSources(); break;
                    case "import": ImportSurfaces(); break;
                    case "build": PhotosynthesisBuild.BuildPlayer(); break;
                    case "release": PhotosynthesisBuild.BuildPlayerRelease(); break;
                    default: throw new ArgumentException("Unknown polish command " + command);
                }
                File.WriteAllText("Logs/Polish/editor-result.txt", command + " succeeded " + DateTime.UtcNow.ToString("O"));
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Logs/Polish/editor-result.txt", command + " FAILED " + e); }
        }

        [MenuItem("Photosynthesis/Polish/Export surface sources")]
        public static void ExportSurfaceSources()
        {
            Directory.CreateDirectory(Maps);
            const string source = "Assets/Photosynthesis/Art/Generated/Sources/"; Directory.CreateDirectory(source);
            void Save(Texture2D t, string name) { File.WriteAllBytes(source + name + "_Source.png", t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t); }
            Save(PlantLeafShape.BladeTexture(2048, true), "LeafTop");
            Save(PlantLeafShape.BladeTexture(1024, false), "LeafUnder");
            // Build only the isolated surface, without story/camera/audio or an active scene mutation.
            var go = new GameObject("Surface source export") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false); var surface = go.AddComponent<LeafSurfaceWorld>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(LeafSurfaceWorld);
            type.GetMethod("MakeSites", flags).Invoke(surface, null);
            type.GetMethod("PlaceStomata", flags).Invoke(surface, null);
            var color = (Func<float, float, Color>)Delegate.CreateDelegate(typeof(Func<float, float, Color>), surface, type.GetMethod("CellColor", flags));
            foreach (var name in new[] { "Epidermis", "EpidermisPatch" })
            {
                int n = name == "Epidermis" ? 1024 : 640; float extent = name == "Epidermis" ? 22f : 4.5f;
                var tex = new Texture2D(n, n, TextureFormat.RGB24, false); var pixels = new Color32[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) pixels[y * n + x] = color(-extent + (x + 0.5f) / n * 2 * extent, -extent + (y + 0.5f) / n * 2 * extent);
                tex.SetPixels32(pixels); tex.Apply(); Save(tex, name);
            }
            UnityEngine.Object.DestroyImmediate(go);
            foreach (var pair in new[] { ("wooden_drawer_02", "LabWood"), ("Floor1", "LabFloor"), ("Rack_and_Beam", "LabBeam") })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Photosynthesis/Art/Environment/Materials/" + pair.Item1 + ".mat");
                if (!material || !material.GetTexture("_BaseMap")) continue;
                var src = material.GetTexture("_BaseMap"); int w = Mathf.Min(2048, src.width), h = Mathf.Min(2048, src.height);
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var previous = RenderTexture.active; Graphics.Blit(src, rt); RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                Save(tex, pair.Item2); RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt);
            }
            AssetDatabase.Refresh(); Debug.Log("[Polish] Exact leaf and epidermis UV sources exported");
        }

        [MenuItem("Photosynthesis/Polish/Import materials and lighting")]
        public static void ImportSurfaces()
        {
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(Maps, "*.png"))
            {
                var t = AssetImporter.GetAtPath(path.Replace('\\', '/')) as TextureImporter;
                if (!t) continue;
                bool normal = path.EndsWith("_Normal.png"), color = path.EndsWith("_Albedo.png") || path.EndsWith("_Source.png") || path.EndsWith("_Emission.png");
                t.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                t.sRGBTexture = color; t.mipmapEnabled = true; t.isReadable = false;
                t.maxTextureSize = path.Contains("LeafTop") || path.Contains("Soil") ? 2048 : 1024;
                t.wrapMode = path.Contains("LeafTop") || path.Contains("LeafUnder") || path.Contains("Epidermis") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                t.anisoLevel = 4; t.textureCompression = TextureImporterCompression.CompressedHQ; t.SaveAndReimport();
            }
            // Preauthor all supported URP map variants. Neutral maps keep untextured surfaces nonmetallic.
            var lib = AssetDatabase.LoadAssetAtPath<MatLib>("Assets/Photosynthesis/Resources/PhotosynthesisMaterials.asset");
            foreach (var m in new[] { lib.lit, lib.litEmissive, lib.litTransparent, lib.leaf, lib.glass })
            {
                m.SetTexture("_BumpMap", SurfaceArt.Map("Neutral", "Normal"));
                m.SetTexture("_MetallicGlossMap", SurfaceArt.Map("Neutral", "MetallicSmoothness"));
                m.SetTexture("_OcclusionMap", SurfaceArt.Map("Neutral", "AO"));
                m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP");
                m.SetFloat("_SmoothnessTextureChannel", 0); m.enableInstancing = true; EditorUtility.SetDirty(m);
            }
            // Edit the user's saved scene in place; never call BuildAll/regenerate the environment.
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != PhotosynthesisBuild.ScenePath) throw new InvalidOperationException("Open the Photosynthesis entry scene before importing polish");
            var chamber = UnityEngine.Object.FindFirstObjectByType<Chamber>();
            if (chamber && chamber.volume && chamber.volume.sharedProfile)
            {
                var p = chamber.volume.sharedProfile;
                if (p.TryGet(out Bloom b)) { b.intensity.Override(0.32f); b.threshold.Override(1.25f); b.scatter.Override(0.58f); }
                if (p.TryGet(out ColorAdjustments c)) { c.contrast.Override(8f); c.saturation.Override(4f); c.postExposure.Override(0.18f); }
                if (p.TryGet(out Vignette v)) v.intensity.Override(0.17f);
                EditorUtility.SetDirty(p);
            }
            // Existing imported atlases and albedo remain intact; add only compatible surface detail.
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Photosynthesis/Art/Environment/Materials" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!m || !m.HasProperty("_Smoothness")) continue;
                var n = m.name.ToLowerInvariant();
                if (n.Contains("wood")) m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.4f));
                if (n.Contains("floor") || n.Contains("marble")) m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.58f));
                string family = m.name == "wooden_drawer_02" ? "LabWood" : m.name == "Floor1" ? "LabFloor" : m.name == "Rack_and_Beam" ? "LabBeam" : null;
                if (family != null && SurfaceArt.Map(family, "MetallicSmoothness"))
                {
                    // Keep the imported UV atlas, color and normal; derived masks follow that exact atlas.
                    m.SetTexture("_MetallicGlossMap", SurfaceArt.Map(family, "MetallicSmoothness"));
                    m.SetTexture("_OcclusionMap", SurfaceArt.Map(family, "AO")); m.SetFloat("_Smoothness", 1f);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP");
                }
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("[Polish] Maps imported; variants authored; scene preserved and lighting refined");
        }
    }
}
