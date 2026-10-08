using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Dr. Curio — procedural companion matching the Figma avatar (white→sage glossy head, dark visor, cyan capsule eyes,
    /// leaf-sprout antenna, gold-rimmed microscope-lens scanner). Never frozen: idle float, sway, blinks, glances, a springy
    /// antenna, head/eye targeting, gestures and a voice-driven talking pulse.
    /// All motion is evaluated every LateUpdate from timestamps, so coroutines that get stopped by a beat jump never leave
    /// him stuck mid-gesture; Place / SetVisible / Companion reset every transient effect (cold-start safe).
    /// Budget ≈ 3k triangles (custom low-poly lathe meshes instead of Unity's 768-tri primitives).
    [DefaultExecutionOrder(100)]   // after CameraDirector, so the companion pose / beam use this frame's camera
    public class Curio : MonoBehaviour
    {
        public static Curio Create(Transform parent)
        {
            var g = new GameObject("Dr. Curio"); g.transform.SetParent(parent, false);
            return g.AddComponent<Curio>();
        }

        // ------------------------------------------------------------------ look
        const float R = 0.225f;                                       // head radius (m)
        const float VisorAz = 50f * Mathf.Deg2Rad, VisorEl = 32f * Mathf.Deg2Rad, VisorEl0 = -5f * Mathf.Deg2Rad;
        const int BeamSides = 16;
        static readonly Color ShellBottom = Palette.Hex(0xBFD3CE), VisorCol = Palette.Hex(0x0A1A1F), Graphite = Palette.Hex(0x22302C),
            CoatCol = Palette.Hex(0xEEF6F3), BudCol = Palette.Hex(0xA6FFD6);
        static readonly Color BeamCol = new Color(0.55f, 1.9f, 2.1f, 0.6f), LineCol = new Color(1.2f, 3.2f, 3.6f, 1f),
            RingCol = new Color(0.8f, 2.6f, 2.9f, 0.9f), HoloCol = new Color(0.6f, 1.9f, 2.1f, 0.9f), GlowCol = new Color(2.6f, 1.9f, 0.7f, 0.9f),
            HoverCol = new Color(0.4f, 1.6f, 1.75f, 0.7f),
            ShellLift = new Color(0.035f, 0.045f, 0.042f);   // faint self-light so the white shell never goes black in dark micro worlds
        static readonly Vector3 RestR = new Vector3(0.215f, -0.045f, 0.075f), RestL = new Vector3(-0.2f, -0.02f, 0.1f),
            ShoulderR = new Vector3(0.13f, 0.11f, 0.02f), ScanHold = new Vector3(-0.19f, 0.2f, 0.17f), ShutHold = new Vector3(-0.12f, 0.17f, 0.21f);
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        // 3×5 bitmap glyphs (0–9 C H + = ↑ ◇) for the scanner's data read-outs.
        static readonly string[] Glyphs = { "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001", "111100111001111",
            "111100111101111", "111001010010010", "111101111101111", "111101111001111", "111100100100111", "101101111101101", "000010111010000",
            "000111000111000", "010111010010010", "000010101010000" };

        enum Look { Face, Camera, Point }

        // ------------------------------------------------------------------ rig
        Transform home, rig, headPivot, head, antenna, eyeR, eyeL, mouth, handR, handL, finger, scanner, lens, iris;
        Transform beam, coreLine, ring, ping, holo, glow;
        ParticleSystem vortex, glyphs;
        Material shellMat, coatMat, eyeMat, mouthMat, coreMat, budMat, hoverMat, lensMat, beamMat, lineMat, ringMat, pingMat, holoMat, glowMat;
        Mesh coneMesh; readonly Vector3[] coneV = new Vector3[(BeamSides + 1) * 2]; readonly Vector2[] coneUV = new Vector2[(BeamSides + 1) * 2];
        readonly float[] coneCos = new float[BeamSides + 1], coneSin = new float[BeamSides + 1];
        readonly List<Object> owned = new List<Object>();
        readonly List<Renderer> casters = new List<Renderer>();
        Quaternion restRotR, restRotL, thinkRot, surpriseRot, cheerRotR, cheerRotL, companionRot;
        Vector3 fingerScale;
        bool built, companion;

        // ------------------------------------------------------------------ state
        /// Set by Dialogue while a line is up. The talking pulse follows the real voice level; with no voice audio
        /// (subtitles only) a fake rhythm runs for at most a line's reading time, so a line cut off by a beat jump
        /// (Story.StopAllCoroutines never reaches Dialogue's `Talking = false`) can't leave him chattering.
        public bool Talking { get => talking; set { talking = value; if (value) talkT0 = Time.time; } }
        bool talking; float talkT0;
        Look look = Look.Face; Vector3 placePos, faceTarget, lookPoint, tempLook, pointTarget;
        float tempLookUntil, pointUntil;
        CurioEmote emote = CurioEmote.Idle; float emoteT0;
        int scanId; float scanStart, scanUntil, sweepT0, sweepDur, nextTick, beamOn, beamGrowT0, shutT0 = -99f, irisS;
        bool sweeping, scannerClosed, sleepy; Vector3 scanTarget, sweepFrom, sweepTo;
        float appear = 1f, voice, gest, bodyYaw, blinkT0 = -1f, nextBlink, glanceUntil, nextGlance;   // gest: 0..1 beat-gesture lift of the free right hand
        Vector2 eyeLook, glanceEye; Vector3 glanceHead;
        bool antInit; Vector3 antPrev, antVel0; Vector2 antAng, antAngVel;
        Camera viewCam;

        bool Visible => gameObject.activeInHierarchy;
        float Scale => Mathf.Max(1e-4f, transform.lossyScale.x);
        static AudioDirector Audio => Module.I ? Module.I.audioDirector : null;
        Camera ViewCam
        {
            get
            {
                if (!viewCam) { var m = Module.I; viewCam = m && m.cam ? m.cam.GetComponent<Camera>() : Camera.main; }
                return viewCam;
            }
        }

        // ================================================================== public API
        public void SetVisible(bool on)
        {
            bool show = on && !gameObject.activeSelf;
            if (!on) ResetTransient();
            gameObject.SetActive(on);
            if (show) ResetTransient();   // re-activation restarts play-on-awake particle systems (the materialise vortex)
        }

        /// Place standing/floating at a world point, facing a target.
        public void Place(Vector3 position, Vector3 face)
        {
            if (companion) Companion(false);
            placePos = position; faceTarget = face; look = Look.Face;
            transform.SetPositionAndRotation(position, Facing(position, face));
            ResetTransient();
        }

        /// Golden particles gather, Curio materialises (~2.2 s).
        public IEnumerator Materialize(Vector3 position, Vector3 face)
        {
            SetVisible(true); Place(position, face);
            if (!built) yield break;
            appear = 0f; sleepy = true; rig.localScale = Vector3.one * 0.001f;
            var au = Audio; if (au) au.Play(Sfx.Materialize);
            vortex.Play(true); glow.gameObject.SetActive(true);
            // glow: soft camera-facing light core (SoftDot billboard, faced in LateUpdate) swelling as the motes arrive
            yield return Fx.Tween(1.25f, k => { glow.localScale = Vector3.one * (0.1f + 0.62f * k); Fx.SetColor(glowMat, GlowCol * (0.2f + 0.8f * k)); }, Ease.In);
            vortex.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            yield return Fx.Tween(0.6f, k =>
            {
                appear = k; float f = Mathf.Clamp01(1f - k);
                glow.localScale = Vector3.one * (0.72f + 1.1f * (1f - f)); Fx.SetColor(glowMat, GlowCol * f);
            }, Ease.Back);
            glow.gameObject.SetActive(false); appear = 1f; sleepy = false; nextBlink = 0f; // eyes "boot" with a blink
            float s = Scale; Vector3 c = head.position;
            Fx.Burst(c, Palette.Gold, 70, 0.07f * s, 2.2f * s, 1.3f); Fx.Burst(c, Palette.Cyan, 26, 0.04f * s, 1.4f * s, 1f);
            if (au) au.Play(Sfx.Sparkle, 0.8f);
            yield return Fx.Wait(0.35f);
        }

        public void LookAt(Vector3 worldPoint) { look = Look.Point; lookPoint = worldPoint; tempLookUntil = 0f; }
        public void LookAtCamera() { look = Look.Camera; tempLookUntil = 0f; }

        /// Scanner beam onto a target for `duration` (beam + scan sound handled here).
        public IEnumerator Scan(Vector3 target, float duration)
        {
            int id = BeginScan(duration); scanTarget = target; sweeping = false;
            var au = Audio; if (au && Visible) au.Play(Sfx.Scan, 0.8f);
            while (scanId == id && Time.time < scanUntil) { if (Module.Skipping) { scanUntil = Time.time; break; } yield return null; }
        }

        /// Beam sweeps from one point to another (top → bottom search).
        public IEnumerator Sweep(Vector3 from, Vector3 to, float duration)
        {
            int id = BeginScan(duration); sweeping = true; sweepFrom = from; sweepTo = to; scanTarget = from;
            sweepT0 = scanStart + 0.15f; sweepDur = Mathf.Max(0.1f, duration - 0.15f); nextTick = scanStart + 0.4f;
            var au = Audio; if (au && Visible) au.Play(Sfx.Scan, 0.7f);
            while (scanId == id && Time.time < scanUntil) { if (Module.Skipping) { scanUntil = Time.time; break; } yield return null; }
        }

        public void Point(Vector3 target)
        {
            float t = Time.time;
            pointTarget = target; pointUntil = t + 3.2f; tempLook = target; tempLookUntil = t + 1.4f;
        }

        public void Emote(CurioEmote e)
        {
            float t = Time.time;
            if (e == CurioEmote.Point)
            {
                if (t >= pointUntil) Point(transform.TransformPoint(new Vector3(0.6f, 0.2f, 2f)));
                else pointUntil = t + 3.2f;
                return;
            }
            emote = e; emoteT0 = t;
            if (e == CurioEmote.Idle) { pointUntil = 0f; tempLookUntil = 0f; }
            if (e == CurioEmote.Celebrate && built && Visible)
            {
                float s = Scale; Vector3 c = head.position + Vector3.up * (R * s);
                Fx.Burst(c, Palette.Gold, 45, 0.05f * s, 1.8f * s, 1.1f); Fx.Burst(c, Palette.Cyan, 20, 0.035f * s, 1.2f * s, 0.9f);
                var au = Audio; if (au) au.Play(Sfx.Sparkle, 0.7f);
            }
        }

        /// Companion mode: hovers in the lower-right of the camera view (micro worlds). Off = back to his world placement.
        public void Companion(bool on)
        {
            if (!built || on == companion) return;
            if (on)
            {
                var cam = ViewCam; if (!cam) return;
                companion = true; look = Look.Camera; CompanionPose(cam);
            }
            else
            {
                companion = false; transform.SetParent(home, false); transform.localScale = Vector3.one;
                transform.SetPositionAndRotation(placePos, Facing(placePos, faceTarget)); look = Look.Face;
            }
            ResetTransient();
            var mode = on ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (var r in casters) r.shadowCastingMode = mode;   // no shadows thrown into micro worlds from the lens
            // Micro-world moods (soil, cell) can be very dark: lift the white shell/coat so his silhouette always reads.
            Fx.SetEmission(shellMat, ShellLift * (on ? 4f : 1f)); Fx.SetEmission(coatMat, ShellLift * (on ? 4f : 1f));
        }

        /// "He shuts his scanner dramatically": raise, twirl, iris snaps shut, satisfied nod.
        public IEnumerator ShutScanner()
        {
            if (!built) yield break;
            scanId++; scanUntil = 0f; sweeping = false; scannerClosed = true; shutT0 = Time.time;
            var au = Audio;
            if (au && Visible) au.Play(Sfx.Whoosh, 0.35f, 1.7f);
            yield return Fx.Wait(0.72f);
            if (Visible)
            {
                if (au) { au.Play(Sfx.Pop, 0.9f, 0.75f); au.Play(Sfx.Beep, 0.3f, 0.5f); }
                float s = Scale; Fx.Burst(lens.position, Palette.Cyan, 18, 0.025f * s, 0.6f * s, 0.5f);
            }
            yield return Fx.Wait(0.3f);
            Emote(CurioEmote.Nod);
            yield return Fx.Wait(0.5f);
        }

        // ================================================================== state helpers
        int BeginScan(float duration)
        {
            scanId++; scanStart = Time.time; scanUntil = scanStart + Mathf.Max(0.2f, duration);
            scannerClosed = false; shutT0 = -99f;
            return scanId;
        }

        Quaternion Facing(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d) : transform.rotation;
        }

        void ResetTransient()
        {
            scanId++; scanUntil = 0f; sweeping = false; beamOn = 0f; scannerClosed = false; shutT0 = -99f; irisS = 0f; gest = 0f;
            emote = CurioEmote.Idle; pointUntil = 0f; tempLookUntil = 0f; appear = 1f; sleepy = false; antInit = false;
            if (!built) return;
            beam.gameObject.SetActive(false); holo.gameObject.SetActive(false); glow.gameObject.SetActive(false);
            var em = glyphs.emission; em.rateOverTime = 0f; glyphs.Clear();
            vortex.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            iris.localScale = Vector3.zero; finger.localScale = Vector3.zero; rig.localScale = Vector3.one;
        }

        void CompanionPose(Camera cam)
        {
            if (transform.parent != cam.transform) transform.SetParent(cam.transform, false);
            const float depth = 0.62f;  // close to the lens so micro-world geometry rarely intersects him
            float halfH = depth * Mathf.Tan(cam.GetGateFittedFieldOfView() * 0.5f * Mathf.Deg2Rad), halfW = halfH * cam.aspect, s = 0.3f * 2f * halfH / 0.95f;   // gate-fitted: the real view height on 4:3 / 5:4
            transform.localScale = Vector3.one * s;                                                       // ≈30 % of view height
            // Lower-right gap between the dialogue panel (bottom centre) and the Ask AI orb (far right): hover glow ~7 % above
            // the bottom edge, head top below the right-hand info card (HUD 1920×1080 reference: ~720 px down). x 0.68 (≈1613 px):
            // ~40 px left of the old 0.72 so the magnifier clears the Ask AI orb; still well right of the 780 px dialogue panel.
            transform.localPosition = new Vector3(0.71f * halfW, -0.86f * halfH + 0.17f * s, depth);
            transform.localRotation = companionRot;
        }

        Vector3 BeamTarget(float t)
        {
            if (!sweeping) return scanTarget;
            float u = Fx.Eval(Mathf.Clamp01((t - sweepT0) / sweepDur), Ease.InOut);
            Vector3 p = Vector3.LerpUnclamped(sweepFrom, sweepTo, u), span = sweepTo - sweepFrom, side = Vector3.Cross(span, lens.position - p);
            float m = side.magnitude;
            return m > 1e-5f ? p + side / m * (Mathf.Sin(t * 5.5f) * span.magnitude * 0.07f) : p;   // zig-zag search across the plant
        }

        // ================================================================== per-frame animation
        void LateUpdate()
        {
            float dt = Time.deltaTime, t = Time.time;
            if (!built || dt <= 0f) return;
            var cam = ViewCam;
            if (companion && cam) CompanionPose(cam);
            Vector3 camPos = cam ? cam.transform.position : transform.position + transform.forward * 4f;
            var au = Audio;

            // voice level (real VO/TTS loudness · still while TTS synthesises · lively fake when subtitles run without audio)
            bool talk = talking && ((au && au.VoicePlaying) || t - talkT0 < 10f);
            float vt = 0f;
            if (talk) vt = au && au.VoiceAudible ? au.VoiceLevel : au && au.VoiceSynthesizing ? 0f
                : 0.3f + 0.45f * Mathf.PerlinNoise(t * 7f, 0.37f) * Mathf.PerlinNoise(t * 2.3f, 3.1f);
            voice = Mathf.Lerp(voice, vt, 1f - Mathf.Exp(-dt * (vt > voice ? 22f : 9f)));

            // blinks & idle glances
            if (t >= nextBlink) { blinkT0 = t; nextBlink = t + (Random.value < 0.18f ? 0.3f : Random.Range(2.4f, 5.5f)); }
            float bk = (t - blinkT0) / 0.16f, blink = bk >= 0f && bk < 1f ? Mathf.Max(0.06f, Mathf.Abs(bk * 2f - 1f)) : 1f;
            if (t >= nextGlance)
            {
                glanceUntil = t + Random.Range(0.5f, 1.3f); nextGlance = glanceUntil + Random.Range(2.5f, 6f);
                glanceEye = Random.insideUnitCircle * 0.8f;
                glanceHead = Random.value < 0.35f ? new Vector3(Random.Range(-7f, 7f), Random.Range(-20f, 20f), Random.Range(-9f, 9f)) : Vector3.zero;
            }
            bool scanning = t < scanUntil;
            bool glancing = t < glanceUntil && !talk && !scanning && t >= pointUntil;

            // ---- emote layer
            float et = t - emoteT0, eyeOpen = 1f, eyeWide = 1f, spin = 0f, wR = 0f, wL = 0f;
            Vector3 rigOff = Vector3.zero, headAdd = Vector3.zero, poseR = RestR, poseL = RestL; Vector2 eyeAdd = Vector2.zero;
            Quaternion rotR = restRotR, rotL = restRotL;
            switch (emote)
            {
                case CurioEmote.Think:
                    {
                        if (et > 7f) { emote = CurioEmote.Idle; break; }
                        float w = Fx.Eval(et / 0.4f, Ease.Out) * Mathf.Clamp01((7f - et) / 0.5f);
                        headAdd = new Vector3(-9f, 8f, 13f) * w; eyeOpen = Mathf.Lerp(1f, 0.45f, w); eyeAdd = new Vector2(0.45f, 0.55f) * w;
                        wR = w; rotR = thinkRot;
                        poseR = new Vector3(0.06f, 0.215f + 0.006f * Mathf.Sin(t * 9f) * Mathf.Max(0f, Mathf.Sin(t * 1.3f)), 0.17f); // hand to chin, tapping
                    }
                    break;
                case CurioEmote.Surprise:
                    {
                        if (et > 1.4f) { emote = CurioEmote.Idle; break; }
                        float k = et < 0.16f ? Fx.Eval(et / 0.16f, Ease.Out) : 1f - Fx.Eval((et - 0.16f) / 1.2f, Ease.InOut);
                        rigOff = new Vector3(0f, 0.09f, -0.24f) * k; headAdd = new Vector3(-14f, 0f, 0f) * k; eyeWide = 1f + 0.38f * k;
                        wR = k; poseR = new Vector3(0.27f, 0.2f, 0.1f); rotR = surpriseRot;
                        wL = k * 0.6f; poseL = RestL + new Vector3(-0.04f, 0.14f, 0f);
                    }
                    break;
                case CurioEmote.Celebrate:
                    {
                        if (et > 1.7f) { emote = CurioEmote.Idle; break; }
                        float u = Mathf.Clamp01(et / 0.95f), w = Mathf.Sin(Mathf.PI * Mathf.Clamp01(et / 1.7f));
                        spin = 360f * Fx.Eval(u, Ease.InOut); rigOff = new Vector3(0f, 0.13f * Mathf.Sin(Mathf.PI * u), 0f);
                        eyeOpen = Mathf.Lerp(1f, 0.55f, w); wR = w; wL = w;
                        poseR = new Vector3(0.22f, 0.33f, 0.05f); poseL = new Vector3(-0.22f, 0.31f, 0.05f); rotR = cheerRotR; rotL = cheerRotL;
                    }
                    break;
                case CurioEmote.Nod:
                    {
                        if (et > 0.9f) { emote = CurioEmote.Idle; break; }
                        float p = et / 0.42f; headAdd.x = p < 2f ? 7f * (1f - Mathf.Cos(p * Mathf.PI * 2f)) : 0f; eyeOpen = 0.7f;
                    }
                    break;
            }

            // ---- beat gestures: irregular open-palm lifts of the free right hand while he explains. Only when no emote, point or scan owns the arms.
            //      Slow envelope (not the syllable-rate voice level), so the hand phrases with the speech instead of jittering.
            bool beatOk = talk && emote == CurioEmote.Idle && t >= pointUntil && !scanning && !Hud.ReduceMotion;
            float gT = beatOk ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.64f, Mathf.PerlinNoise(t * 0.55f, 4.2f))) : 0f;
            gest = Mathf.Lerp(gest, gT, 1f - Mathf.Exp(-dt * 5f));

            // ---- where to look
            Vector3 bt = BeamTarget(t), lp;
            if (t < tempLookUntil) lp = tempLook;
            else if (scanning && !scannerClosed) lp = bt;
            else if (look == Look.Point) lp = lookPoint;
            else if (look == Look.Camera || talk || companion) lp = camPos;   // talking → address the learner
            else lp = faceTarget;

            // ---- body float / sway / turn
            Vector3 d = transform.InverseTransformDirection(lp - head.position);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            bodyYaw = Mathf.LerpAngle(bodyYaw, Mathf.Clamp(yaw * 0.5f, -70f, 70f), 1f - Mathf.Exp(-dt * 2.5f));
            float bob = 0.032f * Mathf.Sin(t * 1.7f) + 0.008f * Mathf.Sin(t * 4.3f + 1f);
            rig.localPosition = new Vector3(0f, bob, 0f) + rigOff;
            rig.localRotation = Quaternion.Euler(1.6f * Mathf.Sin(t * 1.13f), bodyYaw + spin, 2.4f * Mathf.Sin(t * 0.91f));
            rig.localScale = Vector3.one * Mathf.Max(0.001f, appear);

            // ---- head
            Vector3 he = new Vector3(Mathf.Clamp(pitch, -35f, 30f), Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, yaw), -65f, 65f), 2.5f * Mathf.Sin(t * 0.7f)) + headAdd;
            if (glancing) he += glanceHead;
            he.x += voice * (3f + 4f * Mathf.Sin(t * 6.3f));                                    // emphasis nods while talking
            he += new Vector3(-3f, 0f, 5f) * gest;                                               // beat gesture: slight chin-up, head tilts away from the raised hand
            headPivot.localRotation = Quaternion.Slerp(headPivot.localRotation, Quaternion.Euler(he), 1f - Mathf.Exp(-dt * 7f));

            // ---- eyes
            Vector3 dh = head.InverseTransformDirection(lp - head.position);
            Vector2 eyeT = new Vector2(Mathf.Clamp(Mathf.Atan2(dh.x, dh.z) / 0.6f, -1f, 1f), Mathf.Clamp(Mathf.Atan2(dh.y, dh.z) / 0.5f, -1f, 1f)) + eyeAdd;
            if (glancing) eyeT += glanceEye;
            eyeLook = Vector2.Lerp(eyeLook, eyeT, 1f - Mathf.Exp(-dt * 28f));
            float open = blink * eyeOpen * (sleepy ? 0.08f : 1f), sx = 0.044f * eyeWide, sy = sx * open * (1f + 0.06f * voice);
            PlaceEye(eyeR, 16f, sx, sy); PlaceEye(eyeL, -16f, sx, sy);

            // ---- hands
            float k9 = 1f - Mathf.Exp(-dt * 9f);
            Vector3 idleR = RestR + new Vector3(0.012f * Mathf.Sin(t * 2.7f), 0.02f * Mathf.Sin(t * 1.7f - 0.6f) + voice * (0.012f + 0.022f * Mathf.Sin(t * 5.1f)), voice * 0.03f)
                          + new Vector3(0.03f, 0.11f, 0.09f) * gest;                       // beat gesture: out, up to chest height, toward the listener
            Vector3 tR = Vector3.Lerp(idleR, poseR, wR); Quaternion qR = Quaternion.Slerp(Quaternion.Slerp(restRotR, cheerRotR, 0.45f * gest), rotR, wR);
            bool pointing = t < pointUntil;
            if (pointing)
            {
                Vector3 dir = rig.InverseTransformDirection(pointTarget - rig.TransformPoint(ShoulderR));
                if (dir.sqrMagnitude > 1e-6f) { dir.Normalize(); tR = ShoulderR + dir * 0.3f; qR = Quaternion.LookRotation(dir, Vector3.up); }
            }
            handR.localPosition = Vector3.Lerp(handR.localPosition, tR, k9);
            handR.localRotation = Quaternion.Slerp(handR.localRotation, qR, k9);
            finger.localScale = Vector3.Lerp(finger.localScale, pointing ? fingerScale : Vector3.zero, Mathf.Min(1f, k9 * 1.4f));

            Vector3 tL = Vector3.Lerp(RestL + new Vector3(0f, 0.018f * Mathf.Sin(t * 1.7f - 0.9f), 0f), poseL, wL);
            Quaternion qL = Quaternion.Slerp(restRotL, rotL, wL);
            bool raise = scanning && !scannerClosed;
            if (raise)
            {
                tL = ScanHold;
                Vector3 aim = rig.InverseTransformDirection(bt - scanner.position);
                if (aim.sqrMagnitude > 1e-6f) qL = Quaternion.LookRotation(aim, Vector3.up);
            }
            float st = t - shutT0; bool twirl = st >= 0.35f && st < 0.75f;
            if (st >= 0f && st < 1.5f)
            {   // flourish: raise (0–.35 s), twirl about the grip (.35–.75), iris snaps (.72), lower (.9–1.5)
                float up = st < 0.9f ? Fx.Eval(st / 0.35f, Ease.Out) : 1f - Fx.Eval((st - 0.9f) / 0.6f, Ease.InOut);
                tL = Vector3.Lerp(tL, ShutHold, up);
                qL = Quaternion.Slerp(qL, Quaternion.Euler(-20f, -10f, 360f * Fx.Eval((st - 0.35f) / 0.4f, Ease.InOut)), up);
            }
            handL.localPosition = Vector3.Lerp(handL.localPosition, tL, k9);
            handL.localRotation = twirl ? qL : Quaternion.Slerp(handL.localRotation, qL, k9);

            // ---- scanner iris + lens glow
            float irisT = scannerClosed && st >= 0.72f ? 1f : 0f;
            irisS = Mathf.MoveTowards(irisS, irisT, dt * (irisT > irisS ? 9f : 4f));
            iris.localScale = Vector3.one * (0.045f * Mathf.Max(0f, Fx.Eval(irisS, Ease.Back)));
            float lensE = scannerClosed ? 0.05f + 1.6f * (1f - irisS)
                : beamOn > 0f ? 2f + 3.5f * beamOn * (0.85f + 0.15f * Mathf.Sin(t * 31f)) : 1.5f + 0.3f * Mathf.Sin(t * 2.1f);
            Fx.SetEmission(lensMat, Palette.Cyan * lensE);

            // ---- beam
            bool beamWant = raise && t >= scanStart + 0.18f;          // hand rises first, then the beam fires
            if (beamWant && beamOn <= 0f) beamGrowT0 = t;
            beamOn = Mathf.MoveTowards(beamOn, beamWant ? 1f : 0f, dt * (beamWant ? 6f : 4f));
            UpdateBeam(t, cam, camPos, bt);
            if (sweeping && scanning && t >= nextTick) { nextTick = t + 0.5f; if (au && Visible) au.Play(Sfx.Beep, 0.16f, 1.6f); }

            // ---- antenna spring (driven by the sprout base's acceleration in Curio space, so camera cuts never jolt it)
            Vector3 bp = transform.InverseTransformPoint(antenna.position);
            if (!antInit || (bp - antPrev).sqrMagnitude > 0.09f) { antInit = true; antPrev = bp; antVel0 = Vector3.zero; }
            Vector3 vel = (bp - antPrev) / dt, acc = Vector3.ClampMagnitude((vel - antVel0) / dt, 30f); antPrev = bp; antVel0 = vel;
            Vector3 la = antenna.parent.InverseTransformDirection(transform.TransformDirection(acc));
            Vector2 goal = new Vector2(-la.z, la.x) * 5f + new Vector2(4f * Mathf.Sin(t * 1.3f), 5f * Mathf.Sin(t * 0.97f + 1f) + voice * 7f * Mathf.Sin(t * 11f));
            float sdt = Mathf.Min(dt, 0.033f);
            antAngVel += ((goal - antAng) * 140f - antAngVel * 7f) * sdt;
            antAng = Vector2.ClampMagnitude(antAng + antAngVel * sdt, 40f);
            antenna.localRotation = Quaternion.Euler(antAng.x - 6f, 0f, antAng.y);

            // ---- emissive pulses (eyes / mouth / chest / sprout bud / hover ring)
            Fx.SetEmission(eyeMat, Palette.Cyan * (3f + 2.4f * voice + 0.8f * beamOn));
            Fx.SetEmission(mouthMat, Palette.Cyan * (0.6f + 3.2f * voice));
            mouth.localScale = new Vector3(0.03f * (1f - 0.2f * voice), 0.005f + 0.012f * voice * (0.6f + 0.4f * Mathf.Sin(t * 17f)), 0.006f);
            Fx.SetEmission(coreMat, Palette.Cyan * (1.6f + 0.5f * Mathf.Sin(t * 2f) + 2f * voice));
            Fx.SetEmission(budMat, BudCol * (1.4f + 0.5f * Mathf.Sin(t * 2.4f) + 4f * voice));
            Fx.SetColor(hoverMat, HoverCol * (0.8f + 0.2f * Mathf.Sin(t * 3.1f) - 2.2f * (bob - 0.02f)));   // brighter as he dips toward it
            if (cam && glow.gameObject.activeSelf) glow.rotation = cam.transform.rotation;
        }

        void PlaceEye(Transform e, float az, float sx, float sy)
        {
            Vector3 n = Dir((az + eyeLook.x * 8f) * Mathf.Deg2Rad, (1f + eyeLook.y * 6f) * Mathf.Deg2Rad);
            e.localPosition = n * (R * 1.017f); e.localRotation = Quaternion.LookRotation(n, Vector3.up); e.localScale = new Vector3(sx, sy, 0.018f);
        }

        void UpdateBeam(float t, Camera cam, Vector3 camPos, Vector3 target)
        {
            bool on = beamOn > 0.001f;
            if (beam.gameObject.activeSelf != on) beam.gameObject.SetActive(on);
            if (holo.gameObject.activeSelf != on) holo.gameObject.SetActive(on);
            var em = glyphs.emission; em.rateOverTime = on ? 55f * beamOn : 0f;
            if (!on) return;
            float s = Scale;
            Vector3 o = lens.position, dv = target - o; float dist = dv.magnitude;
            if (dist < 1e-4f) return;
            Vector3 dir = dv / dist;
            if (!sweeping)
            {   // slight searching wobble around the target
                Vector3 side = Vector3.Cross(dir, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.right);
                side.Normalize(); Vector3 up2 = Vector3.Cross(side, dir);
                dv += (side * Mathf.Sin(t * 2.9f) + up2 * (0.7f * Mathf.Sin(t * 2.1f + 1.3f))) * (dist * 0.03f);
                dist = dv.magnitude; dir = dv / dist;
            }
            float len = dist;
            bool atLearner = (target - camPos).sqrMagnitude < 0.36f;
            if (atLearner) len = Mathf.Max(dist * 0.55f, dist - 1.1f);   // scanning the learner: stop short of the lens
            len *= Fx.Eval((t - beamGrowT0) / 0.25f, Ease.Out);
            float r1 = Mathf.Max(0.03f * s, len * 0.11f) * (0.4f + 0.6f * beamOn), r0 = 0.035f * s;
            // Seen end-on, the cone mouth near the lens would wash the whole screen cyan: cap it to ~1/3 of the view height.
            if (atLearner && cam) r1 = Mathf.Min(r1, 0.33f * Mathf.Max(0.05f, Vector3.Distance(o + dir * len, camPos)) * Mathf.Tan(cam.GetGateFittedFieldOfView() * 0.5f * Mathf.Deg2Rad));
            beam.SetPositionAndRotation(o, Quaternion.FromToRotation(Vector3.up, dir));
            beam.localScale = Vector3.one / s;                       // beam children work in world units

            for (int i = 0; i <= BeamSides; i++)
            {
                float c = coneCos[i], sn = coneSin[i];
                coneV[i] = new Vector3(c * r0, 0f, sn * r0); coneV[i + BeamSides + 1] = new Vector3(c * r1, len, sn * r1);
                coneUV[i + BeamSides + 1] = new Vector2(1f, len / 0.3f);   // u: along the beam (fade) · v: scan bands
            }
            coneMesh.SetVertices(coneV); coneMesh.SetUVs(0, coneUV);
            coneMesh.bounds = new Bounds(new Vector3(0f, len * 0.5f, 0f), new Vector3(r1 * 2f, len, r1 * 2f));
            coreLine.localScale = new Vector3(0.005f * s, len, 0.005f * s);
            float pp = Mathf.Repeat(t * 1.6f, 1f);
            ring.localPosition = ping.localPosition = new Vector3(0f, len, 0f);
            ring.localScale = Vector3.one * (r1 * (1f + 0.05f * Mathf.Sin(t * 9f)));
            ping.localScale = Vector3.one * (r1 * (1f + 0.9f * pp));
            Fx.SetColor(beamMat, BeamCol * beamOn); Fx.SetColor(lineMat, LineCol * beamOn);
            Fx.SetColor(ringMat, RingCol * beamOn); Fx.SetColor(pingMat, RingCol * ((1f - pp) * beamOn));
            beamMat.SetTextureOffset(BaseMap, new Vector2(0f, -t * 2.2f));   // bands travel out toward the target

            // data glyphs stream from the target back into the scanner
            glyphs.transform.SetPositionAndRotation(o + dir * len, Quaternion.LookRotation(-dir));
            var main = glyphs.main; main.startSize = new ParticleSystem.MinMaxCurve(r1 * 0.09f, r1 * 0.17f);
            var sh = glyphs.shape; sh.radius = r1 * 0.7f;
            var gv = glyphs.velocityOverLifetime; gv.z = len * 0.5f / 0.9f;

            // holographic read-out floating above the scanner
            if (cam) holo.SetPositionAndRotation(o + Vector3.up * (0.1f * s), cam.transform.rotation);
            Fx.SetColor(holoMat, HoloCol * beamOn);
            holoMat.SetTextureOffset(BaseMap, new Vector2(0f, Mathf.Floor(t * 20f) * 0.03125f));
        }

        // ================================================================== build (once, in Module.Awake)
        void Awake() { home = transform.parent; Build(); }

        void Build()
        {
            if (built) return;
            restRotR = Quaternion.Euler(28f, 12f, -10f); restRotL = Quaternion.Euler(-6f, -12f, 0f);
            thinkRot = Quaternion.Euler(-75f, -35f, 20f); surpriseRot = Quaternion.Euler(-60f, 20f, -30f);
            cheerRotR = Quaternion.Euler(-80f, 10f, -20f); cheerRotL = Quaternion.Euler(-70f, -10f, 20f);
            companionRot = Quaternion.Euler(0f, 225f, 0f); fingerScale = new Vector3(0.017f, 0.022f, 0.017f);

            // ---- materials (cloned templates, colours only)
            var shellTex = Own(GradientTex());
            var shell = shellMat = M(MatKind.LitEmissive, Color.white, ShellLift, 0.82f, 0f); Fx.SetTexture(shell, shellTex);
            var coat = coatMat = M(MatKind.LitEmissive, CoatCol, ShellLift, 0.45f, 0f);
            var graphite = M(MatKind.Lit, Graphite, null, 0.78f, 0.25f);
            var visorMat = M(MatKind.LitEmissive, VisorCol, new Color(0.01f, 0.045f, 0.05f), 0.97f, 0f);
            var bezelMat = M(MatKind.Lit, Palette.Hex(0xDCE8E4), null, 0.92f, 0.35f);
            var glintMat = M(MatKind.Additive, new Color(1f, 1f, 1f, 0.3f));
            var white = M(MatKind.LitEmissive, Color.white, Color.white * 2.2f);
            var leafMat = M(MatKind.Leaf, Palette.GreenBright, Palette.GreenBright * 0.35f);
            var stalkMat = M(MatKind.Lit, Palette.Green, null, 0.5f, 0f);
            var cuffMat = M(MatKind.LitEmissive, Palette.GlassStroke, Palette.GlassStroke * 0.6f, 0.8f, 0f);
            var gold = M(MatKind.LitEmissive, Palette.Gold, Palette.GoldDeep * 0.35f, 0.8f, 0.9f);
            eyeMat = M(MatKind.LitEmissive, Palette.Cyan, Palette.Cyan * 3f, 0.9f, 0f);
            mouthMat = M(MatKind.LitEmissive, Palette.Cyan, Palette.Cyan);
            coreMat = M(MatKind.LitEmissive, Palette.Cyan, Palette.Cyan * 2f, 0.9f, 0f);
            budMat = M(MatKind.LitEmissive, BudCol, BudCol * 2f);
            hoverMat = M(MatKind.Additive, HoverCol); Fx.SetTexture(hoverMat, Fx.SoftDot);
            lensMat = M(MatKind.LitEmissive, Palette.Hex(0x0B3940), Palette.Cyan * 1.6f, 0.97f, 0f);
            beamMat = M(MatKind.Additive, BeamCol); Fx.SetTexture(beamMat, Own(BeamTex()));
            lineMat = M(MatKind.Additive, LineCol); ringMat = M(MatKind.Additive, RingCol); pingMat = M(MatKind.Additive, RingCol);
            holoMat = M(MatKind.Additive, HoloCol); Fx.SetTexture(holoMat, Own(DataTex())); holoMat.SetTextureScale(BaseMap, new Vector2(1f, 0.55f));
            glowMat = M(MatKind.Additive, GlowCol); Fx.SetTexture(glowMat, Fx.SoftDot);

            // ---- meshes (low-poly lathes)
            Vector2 V(float x, float y) => new Vector2(x, y);
            Mesh headMesh = Lathe(SphereProfile(12, 1f, 0f), 16, "CurioHead"), ball = Lathe(SphereProfile(6, 0.5f, 0f), 9, "CurioBall"),
                bead = Lathe(SphereProfile(3, 0.5f, 0f), 6, "CurioBead"), pill = Lathe(SphereProfile(6, 0.5f, 0.25f), 10, "CurioPill"),
                puck = Lathe(new[] { V(0f, -0.32f), V(0.82f, -0.32f), V(1f, -0.2f), V(1f, 0.2f), V(0.82f, 0.32f), V(0f, 0.32f) }, 10, "CurioPuck"),
                disc = Lathe(new[] { V(1f, 0f), V(0f, 0f) }, 14, "CurioDisc"),
                dome = Lathe(new[] { V(1f, 0f), V(0.8f, 0.18f), V(0.45f, 0.28f), V(0f, 0.3f) }, 14, "CurioDome"),
                band = Lathe(new[] { V(1f, -0.5f), V(1f, 0.5f) }, 10, "CurioBand"), line = Lathe(new[] { V(1f, 0f), V(1f, 1f) }, 6, "CurioLine"),
                rod = Lathe(new[] { V(0f, -0.5f), V(1f, -0.5f), V(1f, -0.5f), V(1f, 0.5f) }, 8, "CurioRod"),
                torus = Lathe(Circle(1f, 0.27f, 5), 12, "CurioTorus"), rim = Lathe(Circle(1f, 0.2f, 6), 14, "CurioRim"),
                thin = Lathe(Circle(1f, 0.05f, 4), 20, "CurioRing"), torsoMesh = Lathe(TorsoProfile(), 14, "CurioTorso"), leaf = LeafMesh();
            foreach (var m in new[] { headMesh, ball, bead, pill, puck, disc, dome, band, line, rod, torus, rim, thin, torsoMesh, leaf }) owned.Add(m);

            // ---- body
            rig = Fx.Node("Rig", transform).transform;
            Caster(Part("Torso", torsoMesh, shell, rig, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.86f)));
            Part("Neck", band, graphite, rig, new Vector3(0f, 0.19f, -0.005f), Vector3.zero, new Vector3(0.042f, 0.05f, 0.042f));
            Part("Collar", torus, coat, rig, new Vector3(0f, 0.162f, -0.004f), Vector3.zero, new Vector3(0.078f, 0.078f, 0.07f));
            for (int side = -1; side <= 1; side += 2)   // lab-coat lapels
                Fx.Prim(PrimitiveType.Cube, rig, new Vector3(side * 0.043f, 0.088f, 0.106f), new Vector3(0.042f, 0.115f, 0.012f), coat, false, "Lapel")
                    .transform.localRotation = Quaternion.Euler(-16f, side * 20f, side * 24f);
            Part("Core Light", dome, coreMat, rig, new Vector3(0f, -0.035f, 0.121f), new Vector3(90f, 0f, 0f), Vector3.one * 0.03f);
            Part("Leaf Badge", leaf, leafMat, rig, new Vector3(-0.072f, 0.035f, 0.108f), new Vector3(-10f, -28f, 25f), Vector3.one * 0.04f);
            // hover glow: soft horizontal light pool just under the torso (double-sided additive, so it reads from above and below)
            Fx.Prim(PrimitiveType.Quad, rig, new Vector3(0f, -0.172f, 0f), Vector3.one * 0.46f, hoverMat, false, "Hover Glow").transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var motes = Fx.Particles("Hover Motes", rig, new Fx.PCfg
            {
                color = Palette.Cyan, color2 = Palette.GlassStroke, size = 0.016f, life = 1.1f, speed = 0.02f, rate = 7f, radius = 0.035f,
                max = 24, noise = 0.04f, world = false, velocity = new Vector3(0f, -0.12f, 0f)
            });
            motes.transform.localPosition = new Vector3(0f, -0.17f, 0f); owned.Add(motes.GetComponent<ParticleSystemRenderer>().sharedMaterial);
            var mm = motes.main; mm.scalingMode = ParticleSystemScalingMode.Hierarchy;

            // ---- head
            headPivot = Fx.Node("Neck Pivot", rig, new Vector3(0f, 0.2f, 0f)).transform;
            head = Fx.Node("Head", headPivot, new Vector3(0f, 0.205f, 0f)).transform;
            Caster(Part("Shell", headMesh, shell, head, Vector3.zero, Vector3.zero, Vector3.one * R));
            Fx.MeshObj("Visor", Own(Visor(R * 1.012f, 4, 24)), visorMat, head);
            var rimPts = new List<Vector3>(21); for (int j = 0; j <= 20; j++) rimPts.Add(VisorDir(1f, j / 20f * Mathf.PI * 2f) * (R * 1.006f));
            Fx.MeshObj("Visor Bezel", Own(Fx.Tube(rimPts, 0.0085f, 0.0085f, 4)), bezelMat, head);
            var glintPts = new List<Vector3>(8); for (int j = 0; j < 8; j++) glintPts.Add(VisorDir(0.8f, (35f + j * 40f / 7f) * Mathf.Deg2Rad) * (R * 1.017f));
            Fx.MeshObj("Visor Glint", Own(Fx.Tube(glintPts, 0.0035f, 0.0065f, 3)), glintMat, head);
            eyeR = Part("Eye R", pill, eyeMat, head, Vector3.zero, Vector3.zero, Vector3.one);
            eyeL = Part("Eye L", pill, eyeMat, head, Vector3.zero, Vector3.zero, Vector3.one);
            foreach (var e in new[] { eyeR, eyeL }) Part("Eye Glint", bead, white, e, new Vector3(0.18f, 0.32f, 0.45f), Vector3.zero, new Vector3(0.28f, 0.2f, 0.35f));
            Vector3 mn = Dir(0f, -16f * Mathf.Deg2Rad);
            mouth = Fx.Prim(PrimitiveType.Cube, head, mn * (R * 1.014f), new Vector3(0.03f, 0.005f, 0.006f), mouthMat, false, "Mouth").transform;
            mouth.localRotation = Quaternion.LookRotation(mn, Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                Part("Ear", puck, shell, head, new Vector3(side * 0.218f, 0f, -0.005f), new Vector3(0f, 0f, -90f * side), Vector3.one * 0.062f);
                Part("Ear Light", disc, coreMat, head, new Vector3(side * 0.2405f, 0f, -0.005f), new Vector3(0f, 0f, -90f * side), Vector3.one * 0.036f);
            }
            antenna = Fx.Node("Leaf Sprout", head, new Vector3(0f, R * 0.985f, -0.02f)).transform;
            var stalk = Fx.Smooth(new[] { Vector3.zero, new Vector3(0f, 0.045f, 0.004f), new Vector3(0.004f, 0.09f, 0.012f), new Vector3(0f, 0.125f, 0.016f) }, 6);
            Fx.MeshObj("Stalk", Own(Fx.Tube(stalk, 0.0085f, 0.0055f, 5)), stalkMat, antenna);
            Vector3 tip = stalk[stalk.Count - 1];
            Part("Leaf", leaf, leafMat, antenna, tip, new Vector3(-15f, 25f, -50f), Vector3.one * 0.085f);
            Part("Leaf", leaf, leafMat, antenna, tip, new Vector3(-15f, -25f, 50f), Vector3.one * 0.075f);
            Part("Bud", bead, budMat, antenna, tip + new Vector3(0f, 0.006f, 0f), Vector3.zero, Vector3.one * 0.024f);
            Part("Socket", band, graphite, antenna, new Vector3(0f, 0.004f, 0f), Vector3.zero, new Vector3(0.016f, 0.012f, 0.016f));

            // ---- floating hands + scanner
            handR = Hand("Hand R", 1, shell, cuffMat, ball, bead, band);
            finger = Part("Finger", pill, shell, handR, new Vector3(0.004f, 0.008f, 0.05f), new Vector3(90f, 0f, 0f), Vector3.zero);
            handL = Hand("Hand L", -1, shell, cuffMat, ball, bead, band);
            handR.localPosition = RestR; handR.localRotation = restRotR; handL.localPosition = RestL; handL.localRotation = restRotL;
            scanner = Fx.Node("Scanner", handL, new Vector3(0f, 0.07f, 0.012f)).transform;
            Part("Housing", puck, graphite, scanner, new Vector3(0f, 0f, -0.012f), new Vector3(90f, 0f, 0f), Vector3.one * 0.05f);
            Part("Gold Rim", rim, gold, scanner, new Vector3(0f, 0f, 0.004f), new Vector3(90f, 0f, 0f), Vector3.one * 0.047f);
            lens = Part("Lens", dome, lensMat, scanner, new Vector3(0f, 0f, 0.002f), new Vector3(90f, 0f, 0f), Vector3.one * 0.042f);
            iris = Part("Iris", disc, gold, scanner, new Vector3(0f, 0f, 0.0168f), new Vector3(90f, 0f, 0f), Vector3.zero);
            Part("Lens Glint", bead, white, scanner, new Vector3(0.014f, 0.016f, 0.0135f), Vector3.zero, new Vector3(0.008f, 0.008f, 0.004f));
            Part("Grip", rod, graphite, scanner, new Vector3(0f, -0.058f, -0.012f), Vector3.zero, new Vector3(0.012f, 0.075f, 0.012f));

            // ---- scan beam (world units under a 1/scale node), reticle rings, glyph stream, holo read-out
            beam = Fx.Node("Scan Beam", transform).transform;
            coneMesh = Own(new Mesh { name = "CurioBeamCone" }); coneMesh.MarkDynamic();
            var tri = new int[BeamSides * 6];
            for (int i = 0; i <= BeamSides; i++)
            {
                float a = i / (float)BeamSides * Mathf.PI * 2f; coneCos[i] = Mathf.Cos(a); coneSin[i] = Mathf.Sin(a);
                coneUV[i] = Vector2.zero; coneUV[i + BeamSides + 1] = new Vector2(1f, 1f);
                if (i < BeamSides) { int q = i * 6, b = i + BeamSides + 1; tri[q] = i; tri[q + 1] = b; tri[q + 2] = i + 1; tri[q + 3] = i + 1; tri[q + 4] = b; tri[q + 5] = b + 1; }
            }
            coneMesh.vertices = coneV; coneMesh.uv = coneUV; coneMesh.triangles = tri;
            Fx.MeshObj("Cone", coneMesh, beamMat, beam);
            coreLine = Part("Core", line, lineMat, beam, Vector3.zero, Vector3.zero, Vector3.one);
            ring = Part("Reticle", thin, ringMat, beam, Vector3.zero, Vector3.zero, Vector3.one);
            ping = Part("Ping", thin, pingMat, beam, Vector3.zero, Vector3.zero, Vector3.one);
            beam.gameObject.SetActive(false);
            holo = Fx.Prim(PrimitiveType.Quad, transform, Vector3.zero, new Vector3(0.16f, 0.09f, 1f), holoMat, false, "Scanner Readout").transform;
            holo.gameObject.SetActive(false);

            glyphs = Fx.Particles("Scan Glyphs", transform, new Fx.PCfg { color = Palette.Cyan, color2 = Color.white, size = 0.05f, sizeVar = 0.4f, life = 0.9f, speed = 0f, rate = 0f, radius = 0.1f, max = 120, noise = 0.12f });
            var gr = glyphs.GetComponent<ParticleSystemRenderer>(); Destroy(gr.sharedMaterial);
            var gm = M(MatKind.ParticleAdditive, Color.white * 1.8f); Fx.SetTexture(gm, Own(GlyphTex())); gr.sharedMaterial = gm;
            var tsa = glyphs.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 4; tsa.numTilesY = 4;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);   // random static glyph per particle
            var gv = glyphs.velocityOverLifetime; gv.enabled = true; gv.space = ParticleSystemSimulationSpace.Local; gv.x = 0f; gv.y = 0f; gv.z = 0f;

            // ---- materialise FX: converging golden vortex + core glow
            vortex = Fx.Particles("Materialize Vortex", transform, new Fx.PCfg { color = Palette.Gold, color2 = Palette.Light, size = 0.055f, sizeVar = 0.5f, life = 1.15f, speed = 0f, rate = 160f, radius = 1.15f, max = 320, noise = 0.15f });
            vortex.transform.localPosition = new Vector3(0f, 0.3f, 0f); owned.Add(vortex.GetComponent<ParticleSystemRenderer>().sharedMaterial);
            var vs = vortex.shape; vs.radiusThickness = 0f;                  // spawn on the shell…
            var vv = vortex.velocityOverLifetime; vv.enabled = true; vv.space = ParticleSystemSimulationSpace.Local;
            vv.x = 0f; vv.y = 0f; vv.z = 0f; vv.radial = -1.05f; vv.orbitalY = 2.4f;   // …and spiral into the centre
            var vz = vortex.sizeOverLifetime; vz.enabled = true; vz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.3f));
            glow = Fx.Prim(PrimitiveType.Quad, transform, new Vector3(0f, 0.3f, 0f), Vector3.zero, glowMat, false, "Materialize Glow").transform;

            built = true;
            ResetTransient();
        }

        Transform Hand(string name, int side, Material skin, Material cuff, Mesh ball, Mesh bead, Mesh band)
        {
            var h = Fx.Node(name, rig).transform;
            Caster(Part("Palm", ball, skin, h, Vector3.zero, Vector3.zero, new Vector3(0.07f, 0.058f, 0.082f)));
            Part("Thumb", bead, skin, h, new Vector3(-side * 0.03f, 0.004f, 0.012f), new Vector3(0f, -side * 25f, 0f), new Vector3(0.026f, 0.026f, 0.036f));
            Part("Cuff", band, cuff, h, new Vector3(0f, 0f, -0.036f), new Vector3(90f, 0f, 0f), new Vector3(0.032f, 0.012f, 0.032f));
            return h;
        }

        Transform Part(string name, Mesh mesh, Material m, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            var tr = Fx.MeshObj(name, mesh, m, parent).transform;
            tr.localPosition = pos; tr.localRotation = Quaternion.Euler(euler); tr.localScale = scale;
            return tr;
        }

        void Caster(Transform t) { var r = t.GetComponent<Renderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; casters.Add(r); }
        Material M(MatKind k, Color c, Color? e = null, float smooth = -1f, float metal = -1f) => Own(Fx.Mat(k, c, e, smooth, metal));
        T Own<T>(T o) where T : Object { owned.Add(o); return o; }

        void OnDestroy() { foreach (var o in owned) if (o) Destroy(o); }

        // ================================================================== procedural meshes & textures
        static Vector3 Dir(float az, float el) => new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));

        /// Direction on the head sphere inside the squircle-shaped visor (rho 0 centre → 1 edge).
        static Vector3 VisorDir(float rho, float phi)
        {
            float c = Mathf.Cos(phi), s = Mathf.Sin(phi);
            float ex = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / 3f), ey = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / 3f);
            return Dir(VisorAz * rho * ex, VisorEl0 + VisorEl * rho * ey);
        }

        /// Visor: a patch of the head sphere (radius r) — the dark glossy face screen conforms exactly to the shell.
        static Mesh Visor(float r, int rings, int segs)
        {
            int cols = segs + 1; var v = new Vector3[(rings + 1) * cols]; var n = new Vector3[v.Length]; var tri = new int[rings * segs * 6];
            for (int k = 0; k <= rings; k++)
                for (int j = 0; j <= segs; j++) { var d = VisorDir(k / (float)rings, j / (float)segs * Mathf.PI * 2f); v[k * cols + j] = d * r; n[k * cols + j] = d; }
            int q = 0;
            for (int k = 0; k < rings; k++)
                for (int j = 0; j < segs; j++)
                {
                    int a = k * cols + j, b = a + cols;
                    tri[q++] = a; tri[q++] = b; tri[q++] = a + 1; tri[q++] = a + 1; tri[q++] = b; tri[q++] = b + 1;
                }
            var m = new Mesh { name = "CurioVisor", vertices = v, normals = n, triangles = tri }; m.RecalculateBounds(); return m;
        }

        /// Surface of revolution around Y from an (radius, height) profile running bottom → top (outward normals).
        /// Repeated profile points make hard edges; a profile whose first and last points match is closed (torus).
        static Mesh Lathe(Vector2[] p, int sides, string name)
        {
            int n = p.Length, cols = sides + 1; bool closed = p[0] == p[n - 1];
            var v = new Vector3[n * cols]; var nr = new Vector3[v.Length]; var uv = new Vector2[v.Length]; var tri = new int[(n - 1) * sides * 6];
            for (int i = 0; i < n; i++)
            {
                Vector2 a = i > 0 ? p[i - 1] : closed ? p[n - 2] : p[i], b = i < n - 1 ? p[i + 1] : closed ? p[1] : p[i];
                if (i > 0 && p[i - 1] == p[i]) a = p[i];
                if (i < n - 1 && p[i + 1] == p[i]) b = p[i];
                Vector2 tg = b - a, m = new Vector2(tg.y, -tg.x).normalized;
                if (Mathf.Abs(p[i].x) < 1e-5f) m = new Vector2(0f, m.y >= 0f ? 1f : -1f);
                for (int s = 0; s <= sides; s++)
                {
                    float ang = s / (float)sides * Mathf.PI * 2f, c = Mathf.Cos(ang), sn = Mathf.Sin(ang); int k = i * cols + s;
                    v[k] = new Vector3(p[i].x * c, p[i].y, p[i].x * sn); nr[k] = new Vector3(m.x * c, m.y, m.x * sn); uv[k] = new Vector2(s / (float)sides, i / (n - 1f));
                }
            }
            int q = 0;
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * cols + s, b = a + cols;
                    tri[q++] = a; tri[q++] = b; tri[q++] = a + 1; tri[q++] = a + 1; tri[q++] = b; tri[q++] = b + 1;
                }
            var mesh = new Mesh { name = name, vertices = v, normals = nr, uv = uv, triangles = tri }; mesh.RecalculateBounds(); return mesh;
        }

        static Vector2[] SphereProfile(int lat, float r, float stretch)
        {
            if (stretch <= 0f)
            {
                var p = new Vector2[lat + 1];
                for (int i = 0; i <= lat; i++) { float a = Mathf.PI * (i / (float)lat - 0.5f); p[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r); }
                p[0].x = 0f; p[lat].x = 0f; return p;
            }
            int h = lat / 2; var q = new Vector2[2 * (h + 1)];   // capsule: two hemispheres pulled apart by ±stretch
            for (int i = 0; i <= h; i++)
            {
                float a = Mathf.PI * 0.5f * (i / (float)h - 1f), b = Mathf.PI * 0.5f * (i / (float)h);
                q[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r - stretch); q[h + 1 + i] = new Vector2(Mathf.Cos(b) * r, Mathf.Sin(b) * r + stretch);
            }
            q[0].x = 0f; q[q.Length - 1].x = 0f; return q;
        }

        /// Closed circle profile (torus cross-section), counter-clockwise so normals face out.
        static Vector2[] Circle(float r0, float r, int segs)
        {
            var p = new Vector2[segs + 1];
            for (int i = 0; i <= segs; i++) { float a = -Mathf.PI * 0.5f + i / (float)segs * Mathf.PI * 2f; p[i] = new Vector2(r0 + Mathf.Cos(a) * r, Mathf.Sin(a) * r); }
            p[segs] = p[0]; return p;
        }

        static Vector2[] TorsoProfile() => new[]
        {
            new Vector2(0f, -0.16f), new Vector2(0.075f, -0.152f), new Vector2(0.115f, -0.125f), new Vector2(0.138f, -0.075f), new Vector2(0.146f, -0.015f),
            new Vector2(0.144f, 0.045f), new Vector2(0.128f, 0.1f), new Vector2(0.098f, 0.142f), new Vector2(0.055f, 0.166f), new Vector2(0f, 0.172f)
        };

        /// Unit leaf (length 1 along +Y) with a folded midrib and a forward curl. Rendered double-sided (Leaf material).
        static Mesh LeafMesh()
        {
            const int seg = 6; var v = new Vector3[(seg + 1) * 3]; var tri = new int[seg * 12];
            for (int j = 0; j <= seg; j++)
            {
                float t = j / (float)seg, w = 0.225f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.8f))), 0.75f), z = 0.15f * t * t;
                v[j * 3] = new Vector3(-w, t, z + 0.25f * w); v[j * 3 + 1] = new Vector3(0f, t, z); v[j * 3 + 2] = new Vector3(w, t, z + 0.25f * w);
            }
            int q = 0;
            for (int j = 0; j < seg; j++)
            {
                int l0 = j * 3, m0 = l0 + 1, r0 = l0 + 2, l1 = l0 + 3, m1 = l0 + 4, r1 = l0 + 5;
                tri[q++] = l0; tri[q++] = l1; tri[q++] = m0; tri[q++] = m0; tri[q++] = l1; tri[q++] = m1;
                tri[q++] = m0; tri[q++] = m1; tri[q++] = r0; tri[q++] = r0; tri[q++] = m1; tri[q++] = r1;
            }
            var m = new Mesh { name = "CurioLeaf", vertices = v, triangles = tri }; m.RecalculateNormals(); m.RecalculateBounds(); return Fx.FixNormals(m);
        }

        static Texture2D Tex(int w, int h, string name, TextureWrapMode u, TextureWrapMode v) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapModeU = u, wrapModeV = v, filterMode = FilterMode.Bilinear };

        /// Glossy shell: pale sage (#BFD3CE) at the bottom → white at the top (lathe UV v runs bottom → top).
        static Texture2D GradientTex()
        {
            var t = Tex(2, 64, "CurioShell", TextureWrapMode.Clamp, TextureWrapMode.Clamp);
            for (int y = 0; y < 64; y++) { var c = Color.Lerp(ShellBottom, Color.white, Mathf.Pow(y / 63f, 0.7f)); t.SetPixel(0, y, c); t.SetPixel(1, y, c); }
            t.Apply(false, true); return t;
        }

        /// Beam: u = position along the beam (soft start, fade toward the end) × v = repeating scan bands.
        static Texture2D BeamTex()
        {
            var t = Tex(64, 64, "CurioBeam", TextureWrapMode.Clamp, TextureWrapMode.Repeat); var px = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                float band = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(y / 64f * Mathf.PI * 2f), 6f);
                for (int x = 0; x < 64; x++)
                {
                    float u = x / 63f, fade = Mathf.SmoothStep(0f, 1f, u / 0.05f) * (1f - 0.7f * u) * (1f - Mathf.SmoothStep(0f, 1f, (u - 0.93f) / 0.07f));
                    px[y * 64 + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(fade * (0.32f + 0.68f * band))));
                }
            }
            t.SetPixels32(px); t.Apply(false, true); return t;
        }

        /// 4×4 atlas of glowing 3×5 glyphs for the data particles.
        static Texture2D GlyphTex()
        {
            var t = Tex(64, 64, "CurioGlyphs", TextureWrapMode.Clamp, TextureWrapMode.Clamp); var px = new Color32[64 * 64];
            for (int g = 0; g < 16; g++)
            {
                int tx = g % 4 * 16, ty = (3 - g / 4) * 16;
                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 3; c++)
                        if (Glyphs[g][r * 3 + c] == '1')
                            for (int yy = 0; yy < 3; yy++) for (int xx = 0; xx < 3; xx++) px[(ty + 14 - (r * 3 + yy)) * 64 + tx + 3 + c * 3 + xx] = new Color32(255, 255, 255, 255);
            }
            t.SetPixels32(px); t.Apply(false, true); return t;
        }

        /// Rows of tiny glyphs for the scrolling holographic read-out.
        static Texture2D DataTex()
        {
            var t = Tex(64, 64, "CurioData", TextureWrapMode.Repeat, TextureWrapMode.Repeat); var px = new Color32[64 * 64]; var rnd = new System.Random(7);
            for (int row = 0; row < 8; row++)
            {
                byte a = (byte)(140 + rnd.Next(115));
                for (int col = 0; col < 16; col++)
                {
                    if (rnd.NextDouble() < 0.3) continue;
                    string g = Glyphs[rnd.Next(16)];
                    for (int r = 0; r < 5; r++) for (int c = 0; c < 3; c++) if (g[r * 3 + c] == '1') px[(row * 8 + 6 - r) * 64 + col * 4 + c] = new Color32(255, 255, 255, a);
                }
            }
            t.SetPixels32(px); t.Apply(false, true); return t;
        }
    }
}
