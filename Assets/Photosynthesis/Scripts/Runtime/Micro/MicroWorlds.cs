using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Base for the enlarged microscopic environments. Each lives far from the chamber at `Origin` and is hidden
    /// until shown. Show() applies its own lighting mood (fog, ambient). CONTRACT (base is final; subclasses are stubs).
    public abstract class MicroWorld : MonoBehaviour
    {
        public Vector3 Origin => transform.position;
        bool built;

        public static T Create<T>(Transform parent, Vector3 origin) where T : MicroWorld
        {
            var g = new GameObject(typeof(T).Name); g.transform.SetParent(parent, false); g.transform.position = origin;
            var w = g.AddComponent<T>(); g.SetActive(false); return w;
        }

        public bool Built => built;

        public virtual void Show(bool on)
        {
            if (on) Prebuild();
            gameObject.SetActive(on);
            if (on) ApplyMood();
        }

        /// Construct now without showing it (no activation, no mood change). Show() calls this lazily too.
        public void Prebuild() { if (built) return; built = true; Build(); SurfaceArt.DressWorld(this); Fx.Sanitize(gameObject); }

        /// Construct all geometry (called once, lazily).
        protected abstract void Build();
        /// Fog / ambient for this world. Chamber restores its own when shown.
        protected virtual void ApplyMood() { }
        /// Default camera pose (world space) when entering this world.
        public virtual (Vector3 pos, Vector3 look) Pose() => (Origin + new Vector3(0, 2, -10), Origin);
        /// Return to the initial state (molecules home, highlights off). Called before each visit.
        public virtual void ResetWorld() { }
    }
}
