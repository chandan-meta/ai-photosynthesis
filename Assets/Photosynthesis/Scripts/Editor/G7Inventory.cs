using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Photosynthesis.EditorTools
{
    /// Inventory + preview renders of the client's G7 ZIP models (Art/G7/**.fbx).
    /// Batch: Unity.exe -batchmode -projectPath . -executeMethod Photosynthesis.EditorTools.G7Inventory.Run -quit
    /// Writes Logs/g7/inventory.md and Logs/g7/<name>_<view>.png (neutral grey URP Lit, auto-framed, 3 views).
    public static class G7Inventory
    {
        const string Dir = "Assets/Photosynthesis/Art/G7";

        [MenuItem("Photosynthesis/G7 Inventory", priority = 40)]
        public static void Run()
        {
            Directory.CreateDirectory("Logs/g7");
            var sb = new StringBuilder("# G7 ZIP model inventory (auto-generated)\n\n");
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.SetColor("_BaseColor", new Color(0.72f, 0.76f, 0.72f)); mat.SetFloat("_Smoothness", 0.45f);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); rt.Create();
            var camGo = new GameObject("G7Cam"); var cam = camGo.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.08f, 0.1f, 0.1f); cam.targetTexture = rt; cam.fieldOfView = 40f; cam.nearClipPlane = 0.01f; cam.farClipPlane = 100000f;
            var key = new GameObject("Key").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 2.5f; key.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            var fill = new GameObject("Fill").AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 0.8f; fill.color = new Color(0.7f, 0.85f, 1f); fill.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.25f, 0.27f, 0.28f);
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.isReadable == false))
                {   // no generated materials (runtime re-materials everything); readable so meshes can be inspected / combined
                    mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.isReadable = true; mi.importCameras = false; mi.importLights = false;
                    mi.SaveAndReimport();
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!prefab) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                var inst = Object.Instantiate(prefab); inst.transform.position = Vector3.zero;
                int tris = 0, verts = 0; var b = new Bounds(); bool first = true;
                var rows = new StringBuilder();
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterials = System.Array.ConvertAll(r.sharedMaterials, _ => mat);
                    var mf = r.GetComponent<MeshFilter>(); var sm = r as SkinnedMeshRenderer;
                    var mesh = mf ? mf.sharedMesh : sm ? sm.sharedMesh : null; if (!mesh) continue;
                    int t = 0; for (int s = 0; s < mesh.subMeshCount; s++) t += (int)mesh.GetIndexCount(s) / 3;
                    tris += t; verts += mesh.vertexCount;
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                    rows.Append($"| {r.name} | {mesh.vertexCount} | {t} | {mesh.subMeshCount} | {Fmt(r.bounds.size)} | {Fmt(r.bounds.center)} |\n");
                }
                var clips = AnimationUtility.GetAnimationClips(inst); var anim = inst.GetComponent<Animation>(); var animator = inst.GetComponent<Animator>();
                string clipInfo = "";
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is AnimationClip c) clipInfo += $"{c.name} ({c.length:0.00}s, {c.frameRate}fps) ";
                sb.Append($"## {name}\n`{path}` · {new FileInfo(path).Length / 1e6:0.0} MB · renderers tris={tris} verts={verts} · bounds size {Fmt(b.size)} centre {Fmt(b.center)} · scale {inst.transform.localScale} · clips: {(clipInfo == "" ? "none" : clipInfo)} · animator={(animator ? "yes" : "no")} legacy={(anim ? "yes" : "no")}\n\n| renderer | verts | tris | submeshes | size | centre |\n|---|---|---|---|---|---|\n{rows}\n");
                float R = Mathf.Max(b.extents.magnitude, 0.001f);
                var views = new[] { ("front", new Vector3(0.35f, 0.3f, -1f)), ("side", new Vector3(1f, 0.25f, 0.2f)), ("top", new Vector3(0.2f, 1f, -0.3f)) };
                foreach (var (vn, dir) in views)
                {
                    cam.transform.position = b.center + dir.normalized * R * 2.4f; cam.transform.LookAt(b.center);
                    cam.Render();
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes($"Logs/g7/{name}_{vn}.png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
                }
                Object.DestroyImmediate(inst);
            }
            File.WriteAllText("Logs/g7/inventory.md", sb.ToString());
            Debug.Log("[Photosynthesis] G7 inventory written: Logs/g7/inventory.md");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static string Fmt(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";
    }
}
