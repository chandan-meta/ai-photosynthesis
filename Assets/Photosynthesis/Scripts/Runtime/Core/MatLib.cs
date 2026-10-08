using UnityEngine;

namespace Photosynthesis
{
    /// Template materials authored by the editor scene builder (so URP keeps every keyword variant in builds).
    /// Runtime code clones a template and only changes colours — never keywords.
    /// Must live in MatLib.cs: Unity links ScriptableObject assets to scripts by file name.
    public class MatLib : ScriptableObject
    {
        public Material lit, litEmissive, litTransparent, glass, unlit, unlitTransparent, additive, leaf, particleAdditive, particleAlpha;
    }
}
