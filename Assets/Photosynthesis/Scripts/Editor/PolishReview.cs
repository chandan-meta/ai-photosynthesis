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
        [Serializable] class ModelManifest { public ModelAnchor[] anchors; }
        [Serializable] class ModelAnchor { public string name; public float[] center; public float radius, half_height; }
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
                    case "biology": ImportBiology(); break;
                    case "modelaudit": AuditModels(); break;
                    case "models": ImportModels(); break;
                    case "build": PhotosynthesisBuild.BuildPlayer(); break;
                    case "release": PhotosynthesisBuild.BuildPlayerRelease(); break;
                    default: throw new ArgumentException("Unknown polish command " + command);
                }
                File.WriteAllText("Logs/Polish/editor-result.txt", command + " succeeded " + DateTime.UtcNow.ToString("O"));
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Logs/Polish/editor-result.txt", command + " FAILED " + e); }
        }

        static void AuditModels()
        {
            var lines = new System.Collections.Generic.List<string> { "asset\tpart\tvertices\ttriangles\tuv-count\tclips" };
            foreach (var path in new[] { "Assets/Photosynthesis/Resources/Models/LeafInside.fbx", "Assets/Photosynthesis/Art/G7/neww/LeafInside.fbx", "Assets/Photosynthesis/Art/G7/neww/chloroplast.glb" })
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!model) throw new InvalidOperationException("Model audit cannot load " + path);
                var clips = AnimationUtility.GetAnimationClips(model).Length;
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = filter.sharedMesh;
                    lines.Add($"{path}\t{filter.name}\t{mesh.vertexCount}\t{mesh.triangles.Length / 3}\t{mesh.uv.Length}\t{clips}");
                }
            }
            File.WriteAllLines("Logs/ReferenceStudy/2026-10-10/unity_model_audit.tsv", lines);
        }

        [MenuItem("Photosynthesis/Polish/Integrate neww models")]
        public static void ImportModels()
        {
            const string art = "Assets/Photosynthesis/Art/Upgraded/Models/";
            const string output = "Assets/Photosynthesis/Art/Upgraded/Resources/UpgradedModels/";
            Directory.CreateDirectory(output); AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(art + "Textures", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                bool normal = path.Contains("_Normal") || path.Contains("Chloroplast_Image_1");
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = path.Contains("BaseColor") || path.Contains("Chloroplast_Image_0");
                importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false;
                importer.maxTextureSize = 2048; importer.mipmapEnabled = true; importer.anisoLevel = 4;
                importer.isReadable = false; importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            }
            foreach (var path in new[] { "Assets/Photosynthesis/Art/G7/neww/LeafInside.fbx", art + "ChloroplastAnimatedParts.fbx" })
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.isReadable = true; importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
                importer.importTangents = ModelImporterTangents.CalculateMikk; importer.SaveAndReimport();
            }
            Texture2D Tex(string pattern)
            {
                var files = Directory.GetFiles(art + "Textures", pattern);
                if (files.Length != 1) throw new InvalidOperationException("Expected one native map " + pattern);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(files[0].Replace('\\', '/'));
            }
            Material Native(string name, float alpha, Texture2D albedo, Texture2D normal, Texture2D mask = null)
            {
                string path = output + name + ".mat";
                var template = Fx.Mat(alpha < 1 ? MatKind.LitTransparent : MatKind.LitEmissive, new Color(1, 1, 1, alpha), Color.black, .65f, 0);
                template.SetTexture("_BaseMap", albedo); template.SetTexture("_BumpMap", normal); template.SetFloat("_BumpScale", .35f);
                if (mask) template.SetTexture("_MetallicGlossMap", mask);
                template.SetFloat("_Cull", 0); template.enableInstancing = true;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material) { EditorUtility.CopySerialized(template, material); UnityEngine.Object.DestroyImmediate(template); }
                else { material = template; AssetDatabase.CreateAsset(material, path); }
                EditorUtility.SetDirty(material); return material;
            }
            GameObject Copy(string path, string name)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!source) throw new InvalidOperationException("Cannot load supplied model " + path);
                var go = UnityEngine.Object.Instantiate(source); go.name = name;
                foreach (var animator in go.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
                foreach (var light in go.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light.gameObject);
                return go;
            }
            var leaf = Copy("Assets/Photosynthesis/Art/G7/neww/LeafInside.fbx", "LeafInside neww");
            var atlas = new[] { 9, 9, 11, 2, 4, 4, 5, 5, 7, 1, 9 };
            foreach (var renderer in leaf.GetComponentsInChildren<MeshRenderer>(true))
            {
                int part = int.Parse(renderer.name.Substring("_gltfNode_".Length));
                int id = atlas[part]; float alpha = part == 4 ? .62f : part == 9 ? .75f : 1f;
                var material = Native("LeafPart" + part, alpha, Tex($"Leaf_*_RGB_{id}_BaseColor.png"), Tex($"Leaf_*_N_{id}_Normal.png"), Tex($"Leaf_*_R_{id}_MetallicSmoothness.png"));
                renderer.sharedMaterials = Array.ConvertAll(renderer.sharedMaterials, _ => material);
                renderer.name = part == 4 ? "ImportedPalisade" : part == 9 ? "ImportedSpongy" : "ImportedLeafPart" + part;
            }
            PrefabUtility.SaveAsPrefabAsset(leaf, output + "LeafInside.prefab"); UnityEngine.Object.DestroyImmediate(leaf);
            var chloroplast = Copy(art + "ChloroplastAnimatedParts.fbx", "Chloroplast neww");
            var manifest = JsonUtility.FromJson<ModelManifest>(File.ReadAllText(art + "model_manifest.json"));
            foreach (var anchor in manifest.anchors)
            {
                var target = Array.Find(chloroplast.GetComponentsInChildren<Transform>(), t => t.name == anchor.name);
                if (!target) throw new InvalidOperationException("Missing imported stack " + anchor.name);
                // Keep the FBX importer's handedness conversion; only empty-object scale needs correction.
                target.localRotation = Quaternion.identity; target.localScale = new Vector3(anchor.radius, anchor.half_height, anchor.radius);
            }
            int triangles = 0;
            Bounds bounds = default; bool hasBounds = false;
            foreach (var renderer in chloroplast.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool envelope = renderer.name.Contains("Membrane") || renderer.name.Contains("Cap");
                var material = Native("Chloroplast" + renderer.name, envelope ? .55f : 1, Tex("Chloroplast_Image_0.png"), Tex("Chloroplast_Image_1.png"));
                renderer.sharedMaterials = Array.ConvertAll(renderer.sharedMaterials, _ => material);
                renderer.name = "Imported" + renderer.name;
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (mesh.uv.Length != mesh.vertexCount || mesh.tangents.Length != mesh.vertexCount) throw new InvalidOperationException("Missing UV/tangents " + renderer.name);
                triangles += mesh.triangles.Length / 3;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; } else bounds.Encapsulate(renderer.bounds);
            }
            if (Mathf.Abs(bounds.size.x - 15.6f) > .2f || Mathf.Abs(bounds.size.y - 6f) > .2f || Mathf.Abs(bounds.size.z - 9.2f) > .2f)
                throw new InvalidOperationException("Unexpected neww chloroplast scale/orientation " + bounds);
            PrefabUtility.SaveAsPrefabAsset(chloroplast, output + "Chloroplast.prefab"); UnityEngine.Object.DestroyImmediate(chloroplast);
            AssetDatabase.SaveAssets(); Debug.Log($"[Models] Supplied leaf integrated; supplied chloroplast split and bound ({triangles} triangles); original sources preserved");
        }

        [MenuItem("Photosynthesis/Polish/Import biology upgrade")]
        public static void ImportBiology()
        {
            const string textures = "Assets/Photosynthesis/Art/Upgraded/Textures/";
            const string materials = "Assets/Photosynthesis/Art/Upgraded/Resources/UpgradedMaterials/";
            Directory.CreateDirectory(materials); AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(textures, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                bool normal = path.EndsWith("_Normal.png");
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = path.EndsWith("_Albedo.png"); importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat; importer.anisoLevel = 4; importer.isReadable = false;
                importer.maxTextureSize = path.Contains("RootEpidermis") ? 2048 : path.Contains("VesselWall") ? 1024 : 512;
                importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            }
            Texture2D Tex(string family, string channel) => AssetDatabase.LoadAssetAtPath<Texture2D>(textures + family + "_" + channel + ".png");
            Material Make(string name, Shader shader)
            {
                if (!shader) throw new InvalidOperationException("Missing shader for " + name);
                var path = materials + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
                else m.shader = shader;
                m.enableInstancing = true; return m;
            }
            foreach (var name in new[] { "RootEpidermis", "RootHair", "VesselWall", "AtomC", "AtomO", "AtomH", "Connector" })
            {
                bool tissue = name == "RootEpidermis" || name == "RootHair";
                var m = Make(name, Shader.Find(tissue ? "Photosynthesis/Biological Surface" : "Universal Render Pipeline/Lit"));
                m.SetTexture("_BaseMap", Tex(name, "Albedo")); m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BumpMap", Tex(name, "Normal")); m.SetFloat("_BumpScale", tissue ? .3f : .1f);
                m.SetTexture("_MetallicGlossMap", Tex(name, "MetallicSmoothness")); m.SetTexture("_OcclusionMap", Tex(name, "AO"));
                m.SetFloat("_Smoothness", 1); m.renderQueue = name == "RootEpidermis" ? 3000 : 2000;
                if (tissue)
                {
                    m.SetTexture("_ThicknessMap", Tex(name, "Thickness")); m.SetFloat("_Transmission", name == "RootHair" ? .12f : .08f);
                    m.SetFloat("_SrcBlend", name == "RootEpidermis" ? 5 : 1); m.SetFloat("_DstBlend", name == "RootEpidermis" ? 10 : 0);
                    m.SetFloat("_ZWrite", name == "RootEpidermis" ? 0 : 1);
                }
                else
                {
                    m.SetFloat("_Metallic", 0); m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP");
                    if (name == "VesselWall") { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(.025f, .022f, .012f)); }
                }
                EditorUtility.SetDirty(m);
            }
            var water = Make("ClearWater", Shader.Find("Photosynthesis/Clear Water"));
            water.SetTexture("_BumpMap", Tex("WaterFlow", "Normal")); water.SetFloat("_BumpScale", .22f);
            water.SetColor("_BaseColor", new Color(.08f, .32f, .42f, .08f)); water.renderQueue = 3010; EditorUtility.SetDirty(water);
            foreach (var shader in new[] { water.shader, SurfaceArt.Upgraded("RootHair").shader })
            {
                var messages = ShaderUtil.GetShaderMessages(shader);
                foreach (var message in messages)
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        throw new InvalidOperationException(shader.name + ": " + message.message);
            }
            AssetDatabase.SaveAssets(); Debug.Log("[Biology] Imported 50 maps, 8 materials and 2 URP shaders; existing scene untouched");
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
