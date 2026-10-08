using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Photosynthesis
{
    /// Botanical Investigation Chamber: the imported glass-roofed lab (placed by the editor builder, door at −Z), the hero
    /// plant's soil bed inside its centre planter, sun, sky and atmosphere. Owns the Scene 2 formation.
    /// Built once (Awake, or lazily by any public call). Every public method is idempotent and snaps cleanly, so any
    /// beat can cold-start through Story.Prepare (ResetVisuals → SetVisible → SetFormed).
    public partial class Chamber : MonoBehaviour
    {
        [Header("Assigned by the editor builder")]
        public Transform dome;
        public Light sun;
        public Volume volume;
        [Tooltip("Environment renderers (flat, roof sector-split) swept in during the formation. Collected by the editor builder.")]
        public Renderer[] domePieces;

        // ------------------------------------------------------------------ layout (the environment model's centre planter)
        /// Soil surface the hero plant stands in: inside the planter rim (inner wall r ≈ 2.9, top RimTop out to r ≈ 3.3).
        public const float PlatformHeight = 0.55f, RimTop = 0.592f;

        public static Vector3 Polar(float deg, float r, float y = 0f) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r); }

        // ------------------------------------------------------------------ anchor
        Transform anchor;
        /// Where the hero plant stands (platform top centre). Lazy: Module.Awake reads it before Chamber.Awake.
        public Transform PlantAnchor
        {
            get
            {
                if (anchor) return anchor;
                anchor = transform.Find("Plant Anchor");
                if (!anchor) { anchor = new GameObject("Plant Anchor").transform; anchor.SetParent(transform, false); }
                anchor.localPosition = new Vector3(0f, PlatformHeight, 0f);
                return anchor;
            }
        }
        public Vector3 PlatformCenter => PlantAnchor.position;

        // ------------------------------------------------------------------ state
        bool built;
        float dayLight = 1f;                 // formation daylight 0..1
        float dark, darkTarget;              // mood darkness 0..1
        float floorT, floorTarget;           // floor transparency 0..1
        Coroutine darkCo, floorCo;
        float sunBase = 2.2f, skyExposure = 1f, postBase;
        Color sunColor = Color.white, ambSky, ambEq, ambGround;
        Quaternion sunRot = Quaternion.Euler(52f, -38f, 0f);
        Material sky; ColorAdjustments colorAdj; ReflectionProbe probe; Transform camT;
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");

        public const float FormEnd = 11.2f;
        enum SwirlMode { Off, Gather, Dissolve }
        SwirlMode swirl; float formClock, swirlClock;

        void Awake() => EnsureBuilt();

        void EnsureBuilt()
        {
            if (built) return;
            built = true;
            CaptureMood();
            Build();
            EvaluateForm(FormEnd);
            ApplyLighting();
        }

        void CaptureMood()
        {
            if (sun) { sunBase = sun.intensity; sunColor = sun.color; sunRot = sun.transform.rotation; }
            ambSky = RenderSettings.ambientSkyColor; ambEq = RenderSettings.ambientEquatorColor; ambGround = RenderSettings.ambientGroundColor;
            if (RenderSettings.skybox)
            {   // runtime copy so dimming never edits the Sky.mat asset
                sky = new Material(RenderSettings.skybox) { name = "Chamber Sky (runtime)" };
                if (sky.HasProperty(ExposureId)) skyExposure = sky.GetFloat(ExposureId);
                RenderSettings.skybox = sky;
            }
            if (volume && volume.profile.TryGet(out colorAdj)) postBase = colorAdj.postExposure.value;   // .profile = runtime instance
            probe = GetComponentInChildren<ReflectionProbe>(true);
        }

        // ================================================================== public API
        /// Instantly formed / unformed (debug jumps).
        public void SetFormed(bool formed)
        {
            EnsureBuilt();
            swirl = SwirlMode.Off; ClearSwirl();
            EvaluateForm(formed ? FormEnd : -1f);
            ApplyLighting();
            if (formed) RenderProbe();
        }

        /// Scene 2: golden particles slow → planter ring ignites → the lab assembles → sunlight → skylight → hero plant.
        public IEnumerator Form(HeroPlant plant)
        {
            EnsureBuilt();
            SetFormed(false);
            var au = Module.I ? Module.I.audioDirector : null;
            formClock = 0f; StartSwirl();                    // clock first: StartSwirl poses the motes at formClock
            while (formClock < FormEnd && !Module.Skipping)
            {
                float prev = formClock;
                formClock += Time.deltaTime;
                EvaluateForm(formClock); ApplyLighting();
                foreach (float k in ProbeTimes) if (prev < k && formClock >= k) RenderProbe(true);   // reflections follow the sunrise
                if (au) foreach (var c in FormCues) if (prev < c.t && formClock >= c.t) au.Play(c.sfx, c.vol, c.pitch);
                yield return null;
            }
            bool skipped = formClock < FormEnd;
            EvaluateForm(FormEnd); ApplyLighting(); RenderProbe(!skipped);
            if (skipped || !plant) { SetFormed(true); if (plant) plant.SetGrown(true); yield break; }
            swirl = SwirlMode.Dissolve; swirlClock = 0f;      // remaining motes rise while the plant grows, then clear themselves
            if (au) au.Play(Sfx.Sparkle, 0.6f);              // the stem breaks the soil: lands the sunrise swell (A-major pentatonic, in key)
            yield return plant.Grow(4f);
        }
        static readonly float[] ProbeTimes = { 8.8f, 9.3f, 9.8f, 10.5f };
        /// Scene 2 impact layer on the EvaluateForm clock: a skip exits the loop silently, and a restart kills it with the coroutine.
        /// Pitches stay in the NewWorld key (D major): 0.749 = a fourth down, a deeper take on Curio's own Materialize.
        static readonly (float t, Sfx sfx, float vol, float pitch)[] FormCues =
        {
            (2.2f, Sfx.Materialize, 0.7f, 0.749f),   // bells gather under the title (2.5 s); the bloom (2.14 s into the clip at this pitch) lands on the ring-ignition flash at ~4.3 s
            (6.1f, Sfx.Whoosh,      0.45f, 0.8f),    // the lab's pieces (from 6.4 s) sweep in as the title leaves
            (7.9f, Sfx.EnergyBuild, 0.35f, 0.75f),   // sunrise swell (8.4–10.8 s); peaks ~11.0 s and cuts at ~11.23 s, onto the plant Sparkle at FormEnd 11.2 s
        };

        /// 0 normal daylight → 1 dark (factory won't start).
        public void SetDarkness(float amount, float duration)
        {
            EnsureBuilt();
            darkTarget = Mathf.Clamp01(amount);
            if (darkCo != null) { StopCoroutine(darkCo); darkCo = null; }
            if (duration <= 0f || !isActiveAndEnabled) { dark = darkTarget; ApplyLighting(); if (Mathf.Abs(LightLevel - probeLevel) > 0.001f) RenderProbe(); return; }   // skip the 6-face capture when the probe already holds this light level
            darkCo = StartCoroutine(DarkTween(duration));
        }

        IEnumerator DarkTween(float duration)
        {
            float from = dark;
            yield return Fx.Tween(duration, k => { dark = Mathf.Lerp(from, darkTarget, k); ApplyLighting(); });
            darkCo = null; RenderProbe(true);
        }

        /// Powerful natural sun beam from the skylight onto `target` + impact; onHit fires on the impact frame (also when skipped).
        /// A closed skylight opens while the shaft comes down. Leaves the beam on.
        public IEnumerator SunBeam(Vector3 target, System.Action onHit = null)
        {
            EnsureBuilt();
            AimBeam(target);
            float sky0 = skyOpen; if (sky0 < 1f) SetSkylightReady(false);
            bool hit = false;
            for (float t = 0f; t < BeamWhoom; t += Time.deltaTime)
            {
                if (Module.Skipping) break;
                PoseBeam(t);
                if (sky0 < 1f) PoseSkylight(Mathf.Lerp(sky0, 1f, Fx.Eval(t / BeamHit, Ease.Out)), SkyRimRest);
                if (!hit && t >= BeamHit) { hit = true; BeamImpact(); onHit?.Invoke(); }
                yield return null;
            }
            if (!hit) { BeamImpact(); onHit?.Invoke(); }
            PoseBeam(BeamWhoom); if (sky0 < 1f) PoseSkylight(1f, SkyRimRest);
        }

        /// The beam thins and fades like a cloud passing the sun — the steady flare would white out a close-up on the leaf.
        public IEnumerator FadeSunBeam(float dur)
        {
            EnsureBuilt(); if (!beamRig.activeSelf) yield break;
            beamSteady = false;
            Vector3 s0 = beamShaft.localScale, f0 = beamFlare.localScale, h0 = beamHalo.localScale; float i0 = beamSpot.intensity;
            yield return Fx.Tween(dur, t =>
            {
                float k = 1f - t;
                beamShaft.localScale = new Vector3(s0.x * k, s0.y * k, s0.z); beamFlare.localScale = f0 * k; beamHalo.localScale = h0 * k;
                beamSpot.intensity = i0 * k; foreach (var g in beamGlows) g.Set(k); flareGlow.Set(k);
            }, Ease.In);
            BeamOff();
        }

        public void SetSunBeam(bool on, Vector3 target)
        {
            EnsureBuilt();
            if (!on) { BeamOff(); return; }
            if (skyOpen < 1f) { SetSkylightReady(false); PoseSkylight(1f, SkyRimRest); }
            AimBeam(target); PoseBeam(BeamWhoom); if (!beamDust.isPlaying) beamDust.Play();
        }

        // ------------------------------------------------------------------ skylight (Scene 8: the learner lets the light in)
        /// World centre of the oculus at the dome crown; the sun beam starts here.
        public Vector3 SkylightPos { get { EnsureBuilt(); return skylight.position; } }

        /// on: the gold rim pulses and the skylight takes clicks (IsSkylight). off: steady rim, collider disabled.
        public void SetSkylightReady(bool on)
        {
            EnsureBuilt();
            skyReady = on; skyCol.enabled = on;
            if (!on) PoseSkylight(skyOpen, SkyRimRest);
        }

        public bool IsSkylight(Collider c) { EnsureBuilt(); return c != null && c == skyCol; }

        /// ~0.9 s: the iris blades swing open under the housing, the rim flares, the glass lights up. Leaves it open.
        public IEnumerator OpenSkylight()
        {
            EnsureBuilt();
            SetSkylightReady(false);
            var m = Module.I; if (m != null && m.audioDirector) m.audioDirector.Play(Sfx.Unlock, 1f, 0.8f);
            Fx.Burst(skylight.position, Palette.Gold, 70, 0.16f, 3.2f, 1.4f);
            float from = skyOpen;
            for (float t = 0f; t < SkyOpenTime && !Module.Skipping; t += Time.deltaTime)
            {
                float u = t / SkyOpenTime;
                PoseSkylight(Mathf.Lerp(from, 1f, Fx.Eval(u, Ease.Out)), SkyRimRest + 4.5f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, u * 1.4f)));
                yield return null;
            }
            PoseSkylight(1f, SkyRimRest);
        }

        /// Closed, steady rim, not clickable (part of ResetVisuals).
        public void ResetSkylight() { EnsureBuilt(); SetSkylightReady(false); PoseSkylight(0f, SkyRimRest); }
        /// Cold start after Scene 8 (debug jump / -beat): the skylight is already open.
        public void SetSkylightOpen() { EnsureBuilt(); SetSkylightReady(false); PoseSkylight(1f, SkyRimRest); }

        public void SetFloorTransparent(bool on, float duration)
        {
            EnsureBuilt();
            floorTarget = on ? 1f : 0f;
            if (floorCo != null) { StopCoroutine(floorCo); floorCo = null; }
            if (duration <= 0f || !isActiveAndEnabled) { ApplyFloor(floorTarget); return; }
            floorCo = StartCoroutine(FloorTween(duration));
        }

        IEnumerator FloorTween(float duration)
        {
            float from = floorT;
            yield return Fx.Tween(duration, k => ApplyFloor(Mathf.Lerp(from, floorTarget, k)));
            floorCo = null;
        }

        /// Hide/show the whole chamber (micro worlds) for performance. Showing restores the chamber lighting mood
        /// (RenderSettings fog/ambient/skybox, sun) because micro worlds change them. Hiding hands micro worlds plain daylight.
        public void SetVisible(bool on)
        {
            EnsureBuilt();
            if (!on)
            {
                if (gameObject.activeSelf) RestoreNeutral();
                gameObject.SetActive(false);
                return;
            }
            bool wasHidden = !gameObject.activeSelf;
            gameObject.SetActive(true);
            if (wasHidden)
            {   // tweens die with the object: land on their targets
                darkCo = floorCo = null; dark = darkTarget;
                if (floorT != floorTarget) ApplyFloor(floorTarget);
            }
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            if (sky) RenderSettings.skybox = sky;
            if (sun) { RenderSettings.sun = sun; sun.transform.rotation = sunRot; sun.color = sunColor; }
            ApplyLighting();
        }

        public void ResetVisuals()
        {
            EnsureBuilt();
            StopAllCoroutines(); darkCo = floorCo = null;
            dark = darkTarget = 0f; floorTarget = 0f;
            ApplyFloor(0f); BeamOff(); ResetSkylight(); ApplyLighting();
        }

        // ================================================================== lighting
        /// Daylight = formation light × mood. Only touches global state while the chamber is shown.
        void ApplyLighting()
        {
            ApplyRings();   // materials only: safe (and kept in step with the mood) while the chamber is hidden
            if (!gameObject.activeInHierarchy) return;
            float mood = Mathf.Lerp(1f, 0.1f, dark), day = dayLight * mood;
            if (sun) { sun.intensity = sunBase * dayLight * dayLight * mood; sun.enabled = sun.intensity > 0.002f; }
            float amb = LightLevel;
            RenderSettings.ambientSkyColor = ambSky * amb; RenderSettings.ambientEquatorColor = ambEq * amb; RenderSettings.ambientGroundColor = ambGround * amb;
            // the probe cubemap was captured at probeLevel: scale it so the polished floor never mirrors a bright dome in the dark
            if (probe) probe.intensity = Mathf.Min(2f, amb / Mathf.Max(0.02f, probeLevel));
            if (sky) sky.SetFloat(ExposureId, skyExposure * Mathf.Lerp(0.015f, 1f, dayLight) * Mathf.Lerp(1f, 0.14f, dark));
            if (colorAdj != null) colorAdj.postExposure.value = postBase - 0.45f * dark;
            ApplyDaylightFx(day);
        }

        void RestoreNeutral()
        {
            if (sun) { sun.enabled = true; sun.intensity = sunBase; }
            if (sky) sky.SetFloat(ExposureId, skyExposure);
            if (colorAdj != null) colorAdj.postExposure.value = postBase;
            RenderSettings.ambientSkyColor = ambSky; RenderSettings.ambientEquatorColor = ambEq; RenderSettings.ambientGroundColor = ambGround;
        }

        /// Overall light level (= ambient scale) the reflection probe intensity is normalised against.
        float LightLevel => Mathf.Lerp(0.035f, 1f, dayLight) * Mathf.Lerp(1f, 0.25f, dark);
        float probeLevel = 1f;

        /// sliced: spread the capture over frames (no hitch mid-cinematic); otherwise immediate (cold starts, cuts).
        void RenderProbe(bool sliced = false)
        {
            if (!probe || !probe.isActiveAndEnabled) return;
            probe.timeSlicingMode = sliced ? ReflectionProbeTimeSlicingMode.IndividualFaces : ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.RenderProbe(); probeLevel = LightLevel; probe.intensity = 1f;
        }

        // First capture after Story.Prepare has set the opening state (Module.Start runs first), so the probe never
        // falls back to the bright default sky reflection.
        void Start() => RenderProbe();

        // ================================================================== formation timeline (pure function of t)
        static float Bump(float t, float centre, float width) { float x = 1f - Mathf.Abs(t - centre) / (width * 0.5f); return x <= 0f ? 0f : x * x * (3f - 2f * x); }

        /// Planter ring: formation level × mood (the "no light" beat must not be lit by the floor).
        void ApplyRings()
        {
            float mood = Mathf.Lerp(1f, 0.45f, dark);
            for (int s = 0; s < ringStages.Length; s++) foreach (var g in ringStages[s]) g.Set(ringK[s] * mood);
        }
        /// t < 0: nothing formed (darkness) · t ≥ FormEnd: fully formed. Everything in between is the Scene 2 sequence.
        void EvaluateForm(float t)
        {
            // pre-dawn ambient while the architecture assembles, then the sun
            dayLight = 0.22f * Fx.Eval(Mathf.InverseLerp(6.0f, 8.4f, t), Ease.InOut) + 0.78f * Fx.Eval(Mathf.InverseLerp(8.4f, 10.8f, t), Ease.InOut);

            // 1 · the planter ring ignites with an over-bright flash
            for (int s = 0; s < ringStages.Length; s++)
            {
                float t0 = 4.0f + s * 0.35f;
                ringK[s] = Fx.Eval(Mathf.InverseLerp(t0, t0 + 0.5f, t), Ease.Out) + Bump(t, t0 + 0.25f, 0.8f) * 2.2f;
            }
            ApplyRings();
            // 2 · the lab assembles piece by piece, low before high, sweeping from the back around both sides
            for (int i = 0; i < pieces.Length; i++)
            {
                ref var p = ref pieces[i];
                if (!p.r) continue;
                float u = Mathf.InverseLerp(p.delay, p.delay + 0.75f, t); bool on = u > 0f;
                // GameObject, not renderer.enabled: CullNearCamera owns that (it would show an unformed piece when the lens leaves it)
                if (p.t.gameObject.activeSelf != on) { p.t.gameObject.SetActive(on); if (on && t < FormEnd) EmitSparks(p.center, 7); }
                if (!on) continue;
                // rise + scale-pop about the piece's bounds centre (FBX pivots sit at the model origin)
                float e = Fx.Eval(u, Ease.Out), s = 1f - p.pop * (1f - Fx.Eval(u, Ease.Back));
                p.t.position = p.center + p.off * s - Vector3.up * (1.5f * (1f - e));
                p.t.localScale = p.scale * s;
            }
            float sk = Fx.Eval(Mathf.InverseLerp(9.8f, 10.4f, t), Ease.Out);   // crown skylight, once the roof above it has risen into place
            if (skylight.gameObject.activeSelf != sk > 0f) { skylight.gameObject.SetActive(sk > 0f); if (sk > 0f && t < FormEnd) EmitSparks(skylight.position, 14); }
            skylight.localScale = Vector3.one * Mathf.Max(0.001f, sk);
            // formation light: warm glow from the swirl, flash at ignition, hands over to the sun
            float f = t < 0f ? 0f : Mathf.Clamp01(t / 1.2f) * (1f - Mathf.InverseLerp(8.6f, 10.8f, t)) * 7f + Bump(t, 4.3f, 1.2f) * 9f;
            formLight.intensity = f; formLight.enabled = f > 0.01f;
        }

        // ================================================================== swirl particles (manual simulation)
        const int SwirlCount = 720;
        ParticleSystem swirlPs; ParticleSystem.Particle[] swirlBuf;
        float[] swR0, swH0, swTh, swOm, swDel, swRT, swHT, swR, swH, swSize, swPh; bool[] swMote;

        void StartSwirl()
        {
            var rnd = new System.Random(1702);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int i = 0; i < SwirlCount; i++)
            {
                bool mote = i % 4 == 0;
                swMote[i] = mote;
                swR0[i] = Mathf.Lerp(0.5f, 7.5f, Mathf.Sqrt((float)rnd.NextDouble())); swH0[i] = R(0.1f, 4.8f);
                swTh[i] = R(0f, Mathf.PI * 2f); swOm[i] = R(1.1f, 2.7f) * (1.4f / Mathf.Sqrt(swR0[i] + 0.4f) + 0.3f);
                swDel[i] = R(0f, 0.7f); swPh[i] = R(0f, 6.28f); swSize[i] = R(0.025f, 0.085f);
                swRT[i] = mote ? R(1.3f, 6f) : R(2.95f, 3.3f); swHT[i] = mote ? R(0.8f, 3.8f) : RimTop + R(0.0f, 0.05f);   // grains settle on the planter rim, where the ring ignites
                swR[i] = swR0[i]; swH[i] = swH0[i];
            }
            swirl = SwirlMode.Gather;
            if (!swirlPs.isPlaying) swirlPs.Play();
            UpdateSwirl(0f);
        }

        void ClearSwirl() { if (swirlPs) swirlPs.Clear(); }

        void UpdateSwirl(float dt)
        {
            Vector3 c = transform.position;
            float t = formClock, d = swirlClock += swirl == SwirlMode.Dissolve ? dt : 0f;
            if (swirl == SwirlMode.Dissolve && d > 3.6f) { swirl = SwirlMode.Off; ClearSwirl(); return; }
            float fadeIn = Mathf.Clamp01(t / 0.5f), absorb = Mathf.InverseLerp(4.3f, 4.9f, t);
            for (int i = 0; i < SwirlCount; i++)
            {
                float a;
                if (swirl == SwirlMode.Gather)
                {
                    float s = Fx.Eval(Mathf.InverseLerp(0.7f + swDel[i], 4.3f + swDel[i] * 0.3f, t), Ease.InOut);
                    swTh[i] += swOm[i] * Mathf.Lerp(1f, swMote[i] ? 0.12f : 0.25f, s) * dt;
                    swR[i] = Mathf.Lerp(swR0[i], swRT[i], s) + Mathf.Sin(t * 1.7f + swPh[i]) * 0.25f * (1f - s);
                    swH[i] = Mathf.Lerp(swH0[i], swHT[i], s) + Mathf.Sin(t * 1.1f + swPh[i] * 2f) * (swMote[i] ? 0.12f : 0.35f * (1f - s));
                    a = fadeIn * (swMote[i] ? 0.55f + 0.45f * Mathf.Sin(t * 3f + swPh[i]) : 1f - absorb);
                }
                else
                {   // dissolve upward
                    swTh[i] += swOm[i] * 0.1f * dt;
                    swH[i] += (0.35f + 0.25f * Mathf.Sin(swPh[i])) * (1f + d) * dt;
                    a = swMote[i] ? (1f - d / 3.6f) * (0.55f + 0.45f * Mathf.Sin(Time.time * 3f + swPh[i])) : 0f;
                }
                ref var q = ref swirlBuf[i];
                q.position = c + new Vector3(Mathf.Cos(swTh[i]) * swR[i], swH[i], Mathf.Sin(swTh[i]) * swR[i]);
                q.startSize = swSize[i] * (1f + 0.3f * Mathf.Sin(t * 5f + swPh[i]));
                q.startColor = new Color32(255, (byte)(200 + (i % 5) * 11), (byte)(110 + (i % 7) * 20), (byte)(Mathf.Clamp01(a) * 255f));
                q.remainingLifetime = 50f; q.startLifetime = 100f; q.velocity = Vector3.zero;
            }
            swirlPs.SetParticles(swirlBuf, SwirlCount);
        }

        void EmitSparks(Vector3 at, int count)
        {
            if (!sparks) return;
            var ep = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true };
            sparks.Emit(ep, count);
        }

        // ================================================================== per-frame life
        void Update()
        {
            float dt = Time.deltaTime, time = Time.time;
            if (swirl != SwirlMode.Off) UpdateSwirl(dt);
            if (beamRig.activeSelf && beamSteady)
            {
                float k = 1f + 0.06f * Mathf.Sin(time * 6.1f) + 0.04f * Mathf.Sin(time * 13.7f);
                foreach (var g in beamGlows) g.Set(k);
            }
            if (skyReady) { float k = 3f + Mathf.Sin(time * 3.6f); skyRim.Set(k); skyHalo.Set(0.3f * k); }
            if (ringFx > 0f) { ringFx -= dt / 0.7f; PoseImpactRing(); }
        }

        void LateUpdate()
        {
            if (!camT) { var m = Module.I; if (m != null && m.cam) camT = m.cam.transform; if (!camT) return; }
            if (beamRig.activeSelf)
            {   // camera-facing sprites, pulled toward the lens so the target leaf doesn't slice through them
                var q = camT.rotation; Vector3 p = beamRig.transform.position, toCam = Vector3.ClampMagnitude(camT.position - p, 0.45f);
                beamFlare.SetPositionAndRotation(p + toCam, q); beamHalo.SetPositionAndRotation(p + toCam, q); impactRing.rotation = q;
            }
            CullNearCamera(camT.position);
        }

        /// Hide hanging ivy / foliage / hologram screens the orbiting camera is inside of (instead of filling the lens with leaves).
        /// ponytail: hard pop, not a dithered fade — the foliage materials are opaque alpha-clip and we never switch keywords.
        void CullNearCamera(Vector3 c)
        {
            for (int i = 0; i < occ.Length; i++)
            {
                ref var o = ref occ[i];
                bool hide = o.b.SqrDistance(c) < OccMargin * OccMargin;
                if (hide == o.hidden) continue;
                o.hidden = hide;
                if (o.r) o.r.enabled = !hide;
            }
        }
    }
}
