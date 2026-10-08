using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Photosynthesis.EditorTools
{
    /// Chamber population for the scene builder:
    ///  · Dome FBX → unpacked, de-cluttered (floor disc, walkway, railings, mirror spheres, tables go — the procedural
    ///    chamber replaces them), normalised to ~26 m, glass swapped for DomeGlass.mat, big shells split into
    ///    angle × height sector pieces and flattened so the Scene 2 formation can sweep them in (Chamber.domePieces).
    ///  · Tropical plant pack (glTFast) → templates re-pivoted to their base, cloned into layered rings that match the
    ///    procedural beds/pots in Chamber (wall bed, mid planters, platform-edge bed), entrance + camera sightline kept open.
    /// ponytail: no static-batching flags here — every dome piece and plant animates during the formation (the SRP Batcher
    /// keeps the draw cost low); the runtime static-batches the procedural decor instead.
    public static partial class PhotosynthesisBuild
    {
        const string DomeFbx = Root + "/Art/Dome/DomeInterior.fbx", PlantsGlb = Root + "/Art/Vegetation/TropicalPlants.glb";
        const string DomeGlassPath = Root + "/Materials/DomeGlass.mat", DomeLampPath = Root + "/Materials/DomeLamp.mat";
        const string DomePiecesPath = Root + "/Art/Dome/DomePieces.asset";
        const float DomeDiameter = 26.2f, DomeInnerR = 12.8f;   // just inside the glass shell (13.1 m); crowns may reach between the rib fins (~12.35 m)
        const int DomeSectors = 12, DomeBands = 3;

        static partial void ConfigureImports()
        {
            if (AssetImporter.GetAtPath(DomeFbx) is ModelImporter mi)
            {
                bool dirty = mi.addCollider || mi.isReadable || mi.importCameras || mi.importLights || mi.importAnimation || mi.importBlendShapes
                             || mi.animationType != ModelImporterAnimationType.None
                             || mi.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription
                             || mi.materialLocation != ModelImporterMaterialLocation.InPrefab;
                if (dirty)
                {
                    mi.addCollider = false; mi.isReadable = false;
                    mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.importBlendShapes = false;
                    mi.animationType = ModelImporterAnimationType.None;
                    mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                    mi.materialLocation = ModelImporterMaterialLocation.InPrefab;   // embedded textures resolve as sub-assets
                    mi.SaveAndReimport();
                }
            }
            else Debug.LogWarning("[Photosynthesis] Dome model not found at " + DomeFbx);
            // TropicalPlants.glb: glTFast defaults are right (meters, alpha-mask materials, no colliders).
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
            var lib = AssetDatabase.LoadAssetAtPath<MatLib>(Root + "/Resources/PhotosynthesisMaterials.asset");
            BuildDome(chamber, lib);
            BuildVegetation(chamber);
            EditorUtility.SetDirty(chamber);
            AssetDatabase.SaveAssets();
        }

        // ================================================================== dome
        static void BuildDome(Chamber chamber, MatLib lib)
        {
            var root = new GameObject("Dome").transform; root.SetParent(chamber.transform, false);
            chamber.dome = root; chamber.domePieces = new Renderer[0];
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(DomeFbx);
            if (!src) { Debug.LogWarning("[Photosynthesis] Dome model missing — chamber has no glass architecture."); return; }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, root);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // 1 · strip furniture that clashes with the procedural chamber
            var doomed = new List<GameObject>();
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                if (t == inst.transform) continue;
                string n = t.name.ToLowerInvariant();
                var r = t.GetComponent<Renderer>();
                bool mirror = r && System.Array.Exists(r.sharedMaterials, m => m && m.name.ToLowerInvariant().StartsWith("lambert2"));
                if (n == "base" || n == "path" || n.StartsWith("railing") || n.StartsWith("mirr") || n == "l_table" || n.StartsWith("table")
                    || mirror || t.GetComponent<Light>() || t.GetComponent<Camera>())
                    doomed.Add(t.gameObject);
            }
            foreach (var g in doomed) if (g) Object.DestroyImmediate(g);

            var rs = inst.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Debug.LogWarning("[Photosynthesis] Dome model has no renderers."); return; }

            // 2 · normalise: ~26 m diameter, centred on the chamber, floor at y = 0
            var b = ArchitectureBounds(rs);
            inst.transform.localScale *= DomeDiameter / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            b = ArchitectureBounds(rs);
            inst.transform.position += chamber.transform.position - new Vector3(b.center.x, b.min.y, b.center.z);
            b = ArchitectureBounds(rs);
            if (b.size.y > b.size.x * 0.8f) Debug.LogWarning($"[Photosynthesis] Dome bounds look rotated ({b.size}) — check the FBX axis conversion.");

            // 3 · materials: tinted glass, warm lamp lenses; glass never casts shadows
            var glass = DerivedMaterial(DomeGlassPath, lib ? lib.glass : null, new Color(0.86f, 0.96f, 0.9f, 0.18f), Color.black, 0.95f);
            var lamp = DerivedMaterial(DomeLampPath, lib ? lib.litEmissive : null, new Color(1f, 0.95f, 0.86f), new Color(1f, 0.82f, 0.55f) * 4f, 0.6f);
            foreach (var r in rs)
            {
                var ms = r.sharedMaterials; bool isGlass = false;
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i] ? ms[i].name.ToLowerInvariant() : "";
                    if (n == "mt5" || n.StartsWith("glass")) { ms[i] = glass; isGlass = true; }
                    else if (n.StartsWith("emissive")) ms[i] = lamp;
                }
                r.sharedMaterials = ms;
                r.shadowCastingMode = isGlass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                r.receiveShadows = !isGlass;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            }

            // 4 · flatten every renderer under Dome/ and split the three big shells into sectors (formation sweep)
            float height = Mathf.Max(1f, b.size.y);
            var pieces = new List<Renderer>(); var meshes = new List<Mesh>();
            foreach (var r in rs)
            {
                string n = r.name.ToLowerInvariant();
                if ((n == "shell" || n == "pattern" || n == "structure") && Split(r, root, height, pieces, meshes)) continue;
                r.transform.SetParent(root, true);
                pieces.Add(r);
            }
            Object.DestroyImmediate(inst);   // leftover empty hierarchy + the split originals

            AssetDatabase.DeleteAsset(DomePiecesPath);
            if (meshes.Count > 0)
            {
                AssetDatabase.CreateAsset(meshes[0], DomePiecesPath);
                for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshes[0]);
            }
            chamber.domePieces = pieces.ToArray();
            Debug.Log($"[Photosynthesis] Dome: {pieces.Count} pieces ({meshes.Count} split sectors), {b.size.x:0.0} m × {b.size.y:0.0} m.");
        }

        static Bounds ArchitectureBounds(Renderer[] rs)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in rs)
            {
                if (!r) continue;
                string n = r.name.ToLowerInvariant();
                if (n != "shell" && n != "structure" && n != "pattern") continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any) return b;
            foreach (var r in rs) if (r) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }

        /// Split a renderer's mesh into DomeSectors × DomeBands pieces by triangle centroid (angle around the chamber,
        /// height). Each piece is pivoted on its own centre so the formation can rise / scale-pop it. False → keep whole.
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
                    int sector = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(c.z, c.x) / (2f * Mathf.PI) + 0.5f) * DomeSectors), 0, DomeSectors - 1);
                    int band = Mathf.Clamp(Mathf.FloorToInt(c.y / height * DomeBands), 0, DomeBands - 1);
                    int key = band * DomeSectors + sector;
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

                var m = new Mesh { name = $"{r.name} {kv.Key / DomeSectors}-{kv.Key % DomeSectors}" };
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

        /// Material asset cloned from a MatLib template (keeps the template's URP keyword setup), recoloured.
        static Material DerivedMaterial(string path, Material template, Color color, Color emission, float smoothness)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = template ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            else if (template)
            {
                m.shader = template.shader; m.CopyPropertiesFromMaterial(template);
                m.shaderKeywords = template.shaderKeywords; m.renderQueue = template.renderQueue;
            }
            m.SetColor("_BaseColor", color);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ================================================================== vegetation
        sealed class PlantTemplate { public GameObject go; public float height, radius; }

        static void BuildVegetation(Chamber chamber)
        {
            var root = new GameObject("Vegetation").transform; root.SetParent(chamber.transform, false);
            chamber.vegetation = root;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(PlantsGlb);
            if (!src) { Debug.LogWarning("[Photosynthesis] " + PlantsGlb + " did not import (glTFast?) — chamber vegetation skipped."); return; }

            var temp = Object.Instantiate(src);
            temp.transform.position = Vector3.zero;   // keep the root's rotation: Sketchfab_model carries the -90° X that cancels the fbx node's +90° X (identity laid every plant on its side)
            var kinds = new Dictionary<string, List<PlantTemplate>>();
            var matched = new HashSet<Transform>();
            foreach (var t in temp.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.Contains("_MI_") || n.Contains("Circle")) continue;
                string kind = n.Contains("Palm_B08") ? "Palm" : n.Contains("Banana_B09") ? "Banana" : n.Contains("Monstera_B07") ? "Monstera" : n.Contains("Fern_B05") ? "Fern" : null;
                if (kind == null || t.GetComponentsInChildren<Renderer>(true).Length == 0 || HasAncestor(t, matched)) continue;
                matched.Add(t);
                var tpl = MakeTemplate(t, kind);
                if (kind == "Fern" && tpl.height < 1f) kind = "FernSmall";
                if (!kinds.TryGetValue(kind, out var list)) kinds[kind] = list = new List<PlantTemplate>();
                list.Add(tpl);
            }
            Object.DestroyImmediate(temp);
            if (kinds.Count == 0) { Debug.LogWarning("[Photosynthesis] No plant nodes found in " + PlantsGlb); return; }

            var rnd = new System.Random(20241006);
            float R(float a, float b2) => a + (float)rnd.NextDouble() * (b2 - a);
            PlantTemplate Pick(string kind, bool compact = false)
            {
                if (!kinds.TryGetValue(kind, out var l)) return null;
                if (!compact) return l[rnd.Next(l.Count)];
                PlantTemplate best = l[0]; foreach (var p in l) if (p.radius / p.height < best.radius / best.height) best = p;
                return best;
            }
            int count = 0;
            void Place(string kind, float deg, float r, float y, float height, string fallback = null, bool compact = false)
            {
                var tpl = Pick(kind, compact) ?? (fallback != null ? Pick(fallback, compact) : null);
                if (tpl == null) return;
                float s = height / Mathf.Max(0.05f, tpl.height);
                // wall-bed crowns (palms ≈ 4 m wide) must stay inside the glass: crown at ~¾ height clears the shell
                float crownY = y + 0.75f * height, rMax = Mathf.Sqrt(Mathf.Max(0f, DomeInnerR * DomeInnerR - crownY * crownY)) - 0.85f * tpl.radius * s;
                if (r > Chamber.PerimeterIn && r > rMax) r = Mathf.Max(rMax, Chamber.PerimeterIn + 0.45f);
                var g = Object.Instantiate(tpl.go, root);
                g.name = $"{kind} {++count:00}";
                g.transform.localPosition = Chamber.Polar(deg, r, y);
                g.transform.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                g.transform.localScale = Vector3.one * s;
            }
            bool NearBanner(float a, float tol) { foreach (var b in Chamber.Banners) if (Mathf.Abs(Mathf.DeltaAngle(a, b)) < tol) return true; return false; }
            float S() => R(0.85f, 1.2f);
            const float wallSoil = Chamber.PerimeterSoil;

            // wall bed: palms with bananas between, monsteras / ferns layered in front (open toward the entrance)
            for (int i = 0; i < 12; i++)
            {
                float a = Chamber.Entrance + 28f + i * (304f / 11f);
                if (NearBanner(a, 7f)) a += Mathf.Sign(Mathf.DeltaAngle(NearestBanner(a), a) + 0.01f) * 8f;
                Place("Palm", a + R(-3f, 3f), R(10.8f, 11.5f), wallSoil, 4.3f * S());
                float ab = a + 304f / 22f;
                if (i < 11 && !NearBanner(ab, 6f)) Place("Banana", ab + R(-3f, 3f), R(10.2f, 11f), wallSoil, 2.7f * S(), "Monstera");
                float af = a + R(4f, 9f);
                if (!Chamber.InEntrance(af, 22f)) { if (i % 2 == 0) Place("Monstera", af, R(10.0f, 10.6f), wallSoil, 2.1f * S()); else Place("Fern", af, R(9.9f, 10.4f), wallSoil, 1.3f * S(), "FernSmall"); }
                float an = a - R(5f, 9f);
                if (!Chamber.InEntrance(an, 22f)) Place("FernSmall", an, R(9.8f, 10.2f), wallSoil, 0.65f * S(), "Fern");
            }
            // mid planters
            for (int i = 0; i < Chamber.Pots.Length; i++)
            {
                var p = Chamber.Pots[i];
                if (i % 2 == 0) Place("Monstera", p.x, p.y, Chamber.PotSoil, 1.55f * R(0.9f, 1.15f), "Fern", true);
                else Place("Fern", p.x, p.y, Chamber.PotSoil, 1.1f * R(0.9f, 1.1f), "FernSmall");
            }
            // platform-edge bed: low ferns framing the platform, gap toward the entrance
            const int edge = 15;
            float e0 = Chamber.Entrance + Chamber.EdgeBedGap + 6f, e1 = Chamber.Entrance + 360f - Chamber.EdgeBedGap - 6f;
            for (int i = 0; i < edge; i++)
            {
                float a = Mathf.Lerp(e0, e1, i / (edge - 1f)) + R(-2f, 2f);
                bool big = i % 2 == 0;
                Place(big ? "Fern" : "FernSmall", a, R(4.12f, 4.28f), Chamber.EdgeBedSoil, (big ? 0.85f : 0.5f) * S(), big ? "FernSmall" : "Fern");
            }

            foreach (var l in kinds.Values) foreach (var t in l) Object.DestroyImmediate(t.go);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            }
            Debug.Log($"[Photosynthesis] Vegetation: {count} plants from {matched.Count} templates.");
        }

        static float NearestBanner(float a)
        {
            float best = Chamber.Banners[0];
            foreach (var b in Chamber.Banners) if (Mathf.Abs(Mathf.DeltaAngle(a, b)) < Mathf.Abs(Mathf.DeltaAngle(a, best))) best = b;
            return best;
        }

        static bool HasAncestor(Transform t, HashSet<Transform> set)
        {
            for (var p = t.parent; p; p = p.parent) if (set.Contains(p)) return true;
            return false;
        }

        /// Clone a plant node with its world rotation / scale (the pack stores cm under a 0.01 parent) and re-pivot it so
        /// the template origin sits at the base centre of its bounds.
        static PlantTemplate MakeTemplate(Transform node, string kind)
        {
            var wrap = new GameObject(kind + " " + node.name);
            var clone = Object.Instantiate(node.gameObject, node.position, node.rotation);
            clone.transform.localScale = node.lossyScale;
            clone.transform.SetParent(wrap.transform, true);
            var rs = clone.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            clone.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            return new PlantTemplate { go = wrap, height = Mathf.Max(0.05f, b.size.y), radius = Mathf.Max(b.extents.x, b.extents.z) };
        }
    }
}
