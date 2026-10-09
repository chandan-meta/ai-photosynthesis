using UnityEngine;

namespace Photosynthesis
{
    /// UV-specific authored maps; shader variants and import flags are prepared in the editor.
    public static class SurfaceArt
    {
        public static Texture2D Map(string family, string map) => Resources.Load<Texture2D>("SurfaceMaps/" + family + "_" + map);

        public static void Apply(Material material, string family, bool albedo = true, float normalStrength = 0.6f)
        {
            if (!material || !material.HasProperty("_BumpMap")) return;
            var normal = Map(family, "Normal");
            if (!normal) return;
            if (albedo) Fx.SetTexture(material, Map(family, "Albedo"));
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", normalStrength);
            material.SetTexture("_MetallicGlossMap", Map(family, "MetallicSmoothness"));
            material.SetTexture("_OcclusionMap", Map(family, "AO"));
            material.SetFloat("_OcclusionStrength", 0.65f);
            material.SetFloat("_Smoothness", 1f);
        }

        public static void DressWorld(MicroWorld world)
        {
            int assigned = 0;
            foreach (var r in world.GetComponentsInChildren<MeshRenderer>(true))
            {
                string family = r.name switch
                {
                    "Guard Cells" => "Guard", "Root" or "Root Hairs" or "Root Hair (hero)" or "Vascular Core (stele)" => "Root",
                    "Soil Particles" or "Mineral Grains" => "Soil",
                    "Xylem Vessel" or "Lignin Spiral" or "Neighbour Vessel" or "Main Stem Vessel (continues)" or "Xylem" or "XylemInner" or "Phloem" or "PhloemInner" or "Tube" or "TubeInner" => "Vascular",
                    "Thylakoids" or "Lamellae" or "Grana" or "Chloroplasts" or "DiveChloroplasts" => "Grana",
                    "OuterMembrane" or "InnerMembrane" or "OuterCap" or "InnerCap" or "Skin" or "WallOuter" or "WallInner" or "Membrane" or "Tonoplast" or "Sap" or "UpperCuticle" or "LowerCuticle" or "Palisade" or "Spongy" or "UpperEpidermis" or "LowerEpidermis" or "DiveCell" => "Membrane",
                    _ => null
                };
                if (family == null) continue;
                foreach (var material in r.sharedMaterials)
                {
                    // Root cell boundaries and imported atlas colors remain useful scientific detail.
                    bool replaceAlbedo = family != "Root" && r.name != "WallOuter" && r.name != "WallInner";
                    Apply(material, family, replaceAlbedo); assigned++;
                    if (r.name == "Soil Particles" || r.name == "Mineral Grains") Fx.SetColor(material, new Color(.85f, .85f, .85f));
                }
            }
            Debug.Log($"[Polish] {world.name}: {assigned} PBR surface bindings");
        }
    }
}
