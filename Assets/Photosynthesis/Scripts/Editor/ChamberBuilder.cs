using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Photosynthesis.EditorTools
{
    /// Chamber population for the scene builder: the environment FBX (the glass-roofed lab, made by Tools/Chamber/build_env.py
    /// from the user's model) is unpacked under Chamber/Dome, re-materialed (cutout foliage, glass, glowing screens and lamps),
    /// the roof split into angle × height sectors and everything flattened so the Scene 2 formation can sweep it in
    /// (Chamber.domePieces). The floor, the centre planter ring and the floor's outer ring stay put: they are the stage
    /// the formation lights up, and the runtime fades the inner floor + planter for the roots x-ray (by name).
    /// ponytail: no static-batching flags here — every piece animates during the formation (the SRP Batcher keeps the draw
    /// cost low); the runtime static-batches the procedural decor instead.
    public static partial class PhotosynthesisBuild
    {
        const string EnvDir = Root + "/Art/Environment", EnvFbx = EnvDir + "/Chamber.fbx", EnvMats = EnvDir + "/Materials", RoofPiecesPath = EnvDir + "/RoofPieces.asset";
        const int Sectors = 12, Bands = 3;
        // material names (lower case, prefix match) from the source model
        static readonly string[] Foliage = { "plants1", "hanging_plant", "monstera", "pasted__plug_folhas", "pasted__plug_monstera", "pasted__plug_olive" };
        static readonly string[] Glassy = { "lambert11", "mi_glass_rect", "lab_beaker_a", "lab_cylinder_a", "lab_erlenmeyer_a", "pasted__acid_base" };
        static readonly string[] Screens = { "ui1", "ui2" };
        static readonly string[] Lamps = { "emmison1", "light1" };

        static partial void ConfigureImports()
        {
            // textures first, so the model's materials bind them with the right type
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { EnvDir + "/Textures" }))
            {
                if (!(AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter ti)) continue;
                var type = System.IO.Path.GetFileName(ti.assetPath).ToLowerInvariant().Contains("normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != type) { ti.textureType = type; ti.SaveAndReimport(); }
            }
            if (AssetImporter.GetAtPath(EnvFbx) is ModelImporter mi)
            {
                bool dirty = mi.addCollider || mi.isReadable || mi.importCameras || mi.importLights || mi.importAnimation || mi.importBlendShapes || !mi.bakeAxisConversion
                             || mi.animationType != ModelImporterAnimationType.None
                             || mi.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription
                             || mi.materialLocation != ModelImporterMaterialLocation.InPrefab;
                if (dirty)
                {
                    mi.addCollider = false; mi.isReadable = false;   // no colliders: nothing may swallow the plant-inspect raycast
                    mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.importBlendShapes = false;
                    mi.bakeAxisConversion = true;   // Blender export (-Z forward, Y up): door at -Z, identity root
                    mi.animationType = ModelImporterAnimationType.None;
                    mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                    mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                    mi.SaveAndReimport();
                }
            }
            else Debug.LogWarning("[Photosynthesis] Environment model not found at " + EnvFbx + " (run Tools/Chamber/build_env.py)");
            // Client G7 models used at runtime (Resources/Models/*.fbx): no generated materials (LeafInteriorWorld re-materials by part name),
            // no animation (the old VR sequences), readable.
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Resources/Models" }))
            {
                if (!(AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is ModelImporter g7)) continue;
                if (g7.materialImportMode == ModelImporterMaterialImportMode.None && !g7.importAnimation && g7.isReadable) continue;
                g7.materialImportMode = ModelImporterMaterialImportMode.None; g7.importAnimation = false; g7.animationType = ModelImporterAnimationType.None;
                g7.isReadable = true; g7.importCameras = false; g7.importLights = false; g7.importBlendShapes = false;   // readable: LeafInteriorWorld seeds chloroplasts on the cell surfaces
                g7.SaveAndReimport();
            }
        }

        static partial void PopulateChamber(Chamber chamber)
        {
            BuildEnvironment(chamber);
            EditorUtility.SetDirty(chamber);
            AssetDatabase.SaveAssets();
        }

        static void BuildEnvironment(Chamber chamber)
        {
            var root = new GameObject("Dome").transform; root.SetParent(chamber.transform, false);
            chamber.dome = root; chamber.domePieces = new Renderer[0];
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(EnvFbx);
            if (!src) { Debug.LogWarning("[Photosynthesis] Environment model missing — the chamber has no architecture."); return; }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, root);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var rs = inst.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Debug.LogWarning("[Photosynthesis] Environment model has no renderers."); return; }
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (Mathf.Abs(b.size.x - 27.6f) > 1f || Mathf.Abs(b.size.y - 13.2f) > 1f) Debug.LogWarning($"[Photosynthesis] Environment bounds {b.size} — expected about 27.6 × 13.2 m (units or axis conversion?).");

            var mats = new Dictionary<Material, Material>();
            var pieces = new List<Renderer>(); var meshes = new List<Mesh>();
            foreach (var r in rs)
            {
                var ms = r.sharedMaterials; bool clear = false;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (!ms[i]) continue;
                    if (!mats.TryGetValue(ms[i], out var d)) mats[ms[i]] = d = EnvMaterial(ms[i]);
                    ms[i] = d; clear |= d.renderQueue >= (int)RenderQueue.Transparent;
                }
                r.sharedMaterials = ms;
                r.shadowCastingMode = clear ? ShadowCastingMode.Off : ShadowCastingMode.On; r.receiveShadows = !clear;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                string n = r.name;
                if (n == "Roof" && Split(r, root, b.size.y, pieces, meshes)) continue;
                r.transform.SetParent(root, true);
                if (!(n.StartsWith("Floor") || n.StartsWith("Console") || n == "polySurface3")) pieces.Add(r);   // the stage stays: floor, planter ring, outer floor ring
            }
            Object.DestroyImmediate(inst);   // leftover empty hierarchy + the split original

            AssetDatabase.DeleteAsset(RoofPiecesPath);
            if (meshes.Count > 0)
            {
                AssetDatabase.CreateAsset(meshes[0], RoofPiecesPath);
                for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshes[0]);
            }
            chamber.domePieces = pieces.ToArray();
            var door = root.Find("Door2");
            Debug.Log($"[Photosynthesis] Environment: {pieces.Count} pieces ({meshes.Count} roof sectors), {mats.Count} materials, {b.size.x:0.0} × {b.size.y:0.0} m, door at {(door ? door.GetComponent<Renderer>().bounds.center : Vector3.zero)}.");
        }

        /// Material asset derived from the model's imported one (keeps its maps), set up by kind.
        static Material EnvMaterial(Material src)
        {
            string n = src.name.ToLowerInvariant(), path = $"{EnvMats}/{src.name}.mat";
            bool Is(string[] keys) => System.Array.Exists(keys, k => n.StartsWith(k));
            bool leaf = Is(Foliage), glass = Is(Glassy), screen = Is(Screens), lamp = Is(Lamps);
            System.IO.Directory.CreateDirectory(EnvMats);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
            m.SetFloat("_Surface", glass || screen ? 1 : 0); m.SetFloat("_Blend", 0);
            m.SetFloat("_Cull", leaf || glass || screen ? 0 : 2);
            m.SetFloat("_AlphaClip", leaf ? 1 : 0); m.SetFloat("_Cutoff", 0.45f);
            if (glass)
            {
                var c = m.GetTexture("_BaseMap") ? Color.white : new Color(0.82f, 0.94f, 0.95f, 0.22f);   // textured lab glass: its texture alpha (corks stay opaque)
                if (n.StartsWith("mi_glass")) c = new Color(0.3f, 0.42f, 0.45f, 0.45f);   // door window
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.95f); m.SetFloat("_Metallic", 0f);
            }
            if (lamp) { m.SetColor("_BaseColor", new Color(1f, 0.96f, 0.88f)); m.SetColor("_EmissionColor", new Color(1f, 0.86f, 0.62f) * 2.5f); }
            if (screen)
            {   // the hologram glows its own art; straight alpha (no preserve-specular premultiply) so transparent texels emit nothing
                m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap")); m.SetColor("_EmissionColor", new Color(0.8f, 1f, 1f) * 1.1f);
                m.SetFloat("_BlendModePreserveSpecular", 0);
            }
            BaseShaderGUI.SetupMaterialBlendMode(m);
            BaseShaderGUI.SetMaterialKeywords(m);
            if (lamp || screen) { m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// Split a renderer's mesh into Sectors × Bands pieces by triangle centroid (angle around the chamber, height).
        /// Each piece is pivoted on its own centre so the formation can rise / scale-pop it. False → keep whole.
        static bool Split(Renderer r, Transform root, float height, List<Renderer> pieces, List<Mesh> meshes)
        {
            var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null;
            if (!mesh) return false;
            Vector3[] v = mesh.vertices;   // edit mode can read non-readable model meshes
            if (v == null || v.Length == 0) return false;
            Vector3[] nr = mesh.normals; Vector4[] tg = mesh.tangents; Vector2[] uv = mesh.uv;
            int subs = mesh.subMeshCount;
            var l2w = r.transform.localToWorldMatrix; Vector3 origin = root.position;
            var buckets = new SortedDictionary<int, List<int>[]>();
            for (int sm = 0; sm < subs; sm++)
            {
                var tri = mesh.GetTriangles(sm);
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector3 c = l2w.MultiplyPoint3x4((v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f) - origin;
                    int sector = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(c.z, c.x) / (2f * Mathf.PI) + 0.5f) * Sectors), 0, Sectors - 1);
                    int band = Mathf.Clamp(Mathf.FloorToInt(c.y / height * Bands), 0, Bands - 1);
                    int key = band * Sectors + sector;
                    if (!buckets.TryGetValue(key, out var lists))
                    {
                        lists = new List<int>[subs]; for (int k = 0; k < subs; k++) lists[k] = new List<int>();
                        buckets[key] = lists;
                    }
                    lists[sm].Add(tri[i]); lists[sm].Add(tri[i + 1]); lists[sm].Add(tri[i + 2]);
                }
            }
            if (buckets.Count <= 1) return false;

            var remap = new int[v.Length];
            foreach (var kv in buckets)
            {
                for (int i = 0; i < remap.Length; i++) remap[i] = -1;
                var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pt = new List<Vector4>(); var pu = new List<Vector2>();
                var tris = new List<int>[subs];
                for (int sm = 0; sm < subs; sm++)
                {
                    tris[sm] = new List<int>(kv.Value[sm].Count);
                    foreach (int idx in kv.Value[sm])
                    {
                        if (remap[idx] < 0)
                        {
                            remap[idx] = pv.Count; pv.Add(v[idx]);
                            if (nr.Length == v.Length) pn.Add(nr[idx]);
                            if (tg.Length == v.Length) pt.Add(tg[idx]);
                            if (uv.Length == v.Length) pu.Add(uv[idx]);
                        }
                        tris[sm].Add(remap[idx]);
                    }
                }
                Vector3 centre = Vector3.zero; foreach (var p in pv) centre += p; centre /= pv.Count;
                for (int i = 0; i < pv.Count; i++) pv[i] -= centre;

                var m = new Mesh { name = $"{r.name} {kv.Key / Sectors}-{kv.Key % Sectors}" };
                if (pv.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(pv);
                if (pn.Count == pv.Count) m.SetNormals(pn);
                if (pt.Count == pv.Count) m.SetTangents(pt);
                if (pu.Count == pv.Count) m.SetUVs(0, pu);
                m.subMeshCount = subs;
                for (int sm = 0; sm < subs; sm++) m.SetTriangles(tris[sm], sm);
                m.RecalculateBounds();
                meshes.Add(m);

                var go = new GameObject(m.name);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(r.transform.TransformPoint(centre), r.transform.rotation);
                go.transform.localScale = r.transform.lossyScale;   // Dome root is unrotated & unscaled
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var pr = go.AddComponent<MeshRenderer>();
                pr.sharedMaterials = r.sharedMaterials; pr.shadowCastingMode = r.shadowCastingMode; pr.receiveShadows = r.receiveShadows;
                pr.lightProbeUsage = r.lightProbeUsage; pr.reflectionProbeUsage = r.reflectionProbeUsage;
                pieces.Add(pr);
            }
            return true;
        }
    }
}
