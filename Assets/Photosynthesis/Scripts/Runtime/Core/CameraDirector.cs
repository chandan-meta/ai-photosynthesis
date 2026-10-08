using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Photosynthesis
{
    /// All camera work: scripted shots (Cut/Blend/Arc/Ride/Push), learner orbit (rotate · zoom · pan), idle-hold drift and shake.
    /// ponytail: hand-rolled instead of Cinemachine — every shot is generated from procedural geometry,
    /// so authored virtual cameras would add a package without saving code. Add Cinemachine if shots become hand-authored.
    [RequireComponent(typeof(Camera))]
    public class CameraDirector : MonoBehaviour
    {
        public Camera Cam { get; private set; }

        [Header("Orbit")]
        public float rotateSpeed = 0.25f, panSpeed = 0.004f, damping = 8f;
        public float minPitch = -10f, maxPitch = 55f;   // set by every Orbit call (the scene serialises 75: never rely on the initializer)
        /// Non-zero: while the orbit pivot is inside this ellipsoid, the arm shortens so the lens stays inside it (Story sets the chamber dome).
        public Vector3 BoundCentre, BoundExtents;

        /// Fired per zoom input: ±1 per wheel notch or HUD +/− press (positive = in). Story uses it for "zoom into the leaf".
        public event Action<float> UserZoomed;
        /// Fired when the learner clicks (not drags) in the 3D view. Screen position.
        public event Action<Vector2> Clicked;

        public bool Orbiting { get; private set; }
        public bool UserControl { get; set; }
        public float AutoSpin { get; set; }               // deg/s turntable while idle in orbit mode
        public Func<Vector2, bool> UiBlocks;               // true when the pointer is over UI

        Vector3 pivot, pivotTarget; float dist, distTarget, yaw, yawTarget, pitch, pitchTarget, minDist = 1, maxDist = 30;
        Vector3 panOffset; float shakeAmp, shakeT; Vector2 pressPos; bool dragging; float lastInput;
        int shot, activeShot; // activeShot != 0 while a Blend/Arc/Ride/Push owns the camera; Cut/Orbit supersede it
        float settle = 1f, fovTarget; Quaternion settleFrom;   // settle 0→1 while easing out of a scripted shot into orbit; fovTarget > 0: lens eased with the orbit (0 = leave the lens alone)
        float hold, drift, holdDist = 10f, lastFov; Vector3 lastPos, shakeOff; Quaternion lastRot;   // idle-hold drift: seconds left alone, metres pushed, subject distance; pose as we left it last frame
        DepthOfField dof; Vector3 shotLook; float focusD;   // cinematic depth of field: focused on the current scripted shot's subject (High quality only)

        void Awake()
        {
            Cam = GetComponent<Camera>();
            // The authored 16:9 frame is the guaranteed area on every display. Wider screens see more at the sides (Hor+, as before);
            // 16:10 / 4:3 / 5:4 school monitors see more above and below instead of losing Curio off the right edge.
            // fieldOfView remains the sensor's vertical FOV, so every existing Cut/Blend/Arc/Ride/micro-world fov write is unchanged at 16:9.
            float f = Cam.fieldOfView;
            Cam.sensorSize = new Vector2(36f, 20.25f); Cam.gateFit = Camera.GateFitMode.Overscan; Cam.usePhysicalProperties = true;
            Cam.fieldOfView = f;   // setting the sensor rederives fov from focal length, so restore the authored lens
        }

        void Start()
        {   // the builder adds a disabled Bokeh DoF to the global volume; scripted shots own it (see LateUpdate)
            var ch = Module.I ? Module.I.chamber : null;
            if (ch && ch.volume && ch.volume.profile.TryGet(out dof)) dof.active = false;
        }

        // ------------------------------------------------------------------ scripted shots
        public void Cut(Vector3 pos, Vector3 look, float fov = 50f)
        {
            Orbiting = false; activeShot = 0; holdDist = Vector3.Distance(pos, look); shotLook = look; transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos)); Cam.fieldOfView = fov;
        }

        public IEnumerator Blend(Vector3 pos, Vector3 look, float dur, float fov = 50f, Ease ease = Ease.InOut, float roll = 0f)
        {
            Orbiting = false; int id = activeShot = ++shot; holdDist = Vector3.Distance(pos, look); shotLook = look;
            Vector3 p0 = transform.position; Quaternion r0 = transform.rotation; float f0 = Cam.fieldOfView;
            Quaternion r1 = Quaternion.LookRotation(look - pos) * Quaternion.Euler(0, 0, roll);
            yield return Fx.Tween(dur, t =>
            {
                if (activeShot != id) return;
                transform.SetPositionAndRotation(Vector3.LerpUnclamped(p0, pos, t), Quaternion.SlerpUnclamped(r0, r1, t));
                Cam.fieldOfView = Mathf.LerpUnclamped(f0, fov, t);
            }, ease);
            if (activeShot == id) activeShot = 0;
        }

        /// Frame a world point from a direction at a distance.
        public IEnumerator Focus(Vector3 target, Vector3 fromDir, float distance, float dur, float fov = 45f) =>
            Blend(target - fromDir.normalized * distance, target, dur, fov);

        /// Like Blend, but travels round `look` (yaw the short way) instead of along a straight chord, so a return from an
        /// orbited view never cuts through the subject. Starts from the current view, ends on Blend's pose.
        public IEnumerator Arc(Vector3 pos, Vector3 look, float dur, float fov = 50f, Ease ease = Ease.InOut)
        {
            Orbiting = false; int id = activeShot = ++shot; shotLook = look;
            Vector3 off = pos - look; float d1 = holdDist = off.magnitude, d0 = Vector3.Distance(transform.position, look), f0 = Cam.fieldOfView;
            Vector3 c0 = transform.position + transform.forward * d0;   // what the camera looks at now, at the target's range
            Vector3 e = transform.eulerAngles; float y0 = e.y, p0 = Mathf.DeltaAngle(0f, e.x), r0 = Mathf.DeltaAngle(0f, e.z);
            float y1 = y0 + Mathf.DeltaAngle(y0, Mathf.Atan2(-off.x, -off.z) * Mathf.Rad2Deg), p1 = Mathf.Asin(Mathf.Clamp(off.y / Mathf.Max(d1, 0.001f), -1f, 1f)) * Mathf.Rad2Deg;
            yield return Fx.Tween(dur, t =>
            {
                if (activeShot != id) return;
                var q = Quaternion.Euler(Mathf.LerpUnclamped(p0, p1, t), Mathf.LerpUnclamped(y0, y1, t), 0f);
                transform.SetPositionAndRotation(Vector3.LerpUnclamped(c0, look, t) + q * new Vector3(0f, 0f, -Mathf.LerpUnclamped(d0, d1, t)), q * Quaternion.Euler(0f, 0f, r0 * (1f - t)));   // a leftover bank unwinds instead of popping
                Cam.fieldOfView = Mathf.LerpUnclamped(f0, fov, t);
            }, ease);
            if (activeShot == id) activeShot = 0;
        }

        /// Slow dolly along the current view direction, for narration holds. Any Blend/Arc/Ride/Cut/Orbit supersedes it.
        public IEnumerator Push(float metres, float dur)
        {
            Orbiting = false; int id = activeShot = ++shot; holdDist = Mathf.Max(holdDist * 0.5f, holdDist - metres);
            Vector3 p0 = transform.position, d = transform.forward * metres; shotLook = p0 + transform.forward * (holdDist + metres);
            yield return Fx.Tween(dur, t => { if (activeShot == id) transform.position = p0 + d * t; });
            if (activeShot == id) activeShot = 0;
        }

        /// Fly along a world-space path. Banks into sideways turns only (bank = degrees per unit curvature, ≤ 12°), optional speed curve (time→progress).
        public IEnumerator Ride(IList<Vector3> path, float dur, float bank = 25f, AnimationCurve progress = null, float lookAhead = 0.04f, Action<float> onProgress = null, float fov = 70f)
        {
            Orbiting = false; int id = activeShot = ++shot;
            float roll = 0, f0 = Cam.fieldOfView;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                if (Module.Skipping || activeShot != id) break;
                float u = progress != null ? progress.Evaluate(t / dur) : Fx.Eval(t / dur, Ease.InOut);
                Vector3 p = Fx.Along(path, u), a = Fx.Along(path, Mathf.Min(1, u + lookAhead)), b = Fx.Along(path, Mathf.Min(1, u + lookAhead * 2));
                Vector3 d1 = (a - p).normalized, d2 = (b - a).normalized, side = Vector3.Cross(Vector3.up, d1);   // horizontal right of the view
                float turn = side.sqrMagnitude > 1e-4f ? Mathf.Asin(Mathf.Clamp(Vector3.Dot(d2, side.normalized), -1f, 1f)) * Mathf.Rad2Deg : 0f;   // sideways bend as the camera sees it: a climb's pitch change never rolls the horizon
                roll = Mathf.Lerp(roll, Hud.ReduceMotion ? 0f : Mathf.Clamp(-turn * bank * 0.2f, -12f, 12f), Time.deltaTime * 3f);   // Reduce motion: the bank eases level instead of popping
                if (d1.sqrMagnitude > 0) transform.SetPositionAndRotation(p, Quaternion.LookRotation(d1) * Quaternion.Euler(0, 0, roll));
                shotLook = p + d1 * 3f;   // focus a few metres down the tunnel
                Cam.fieldOfView = Mathf.Lerp(f0, fov, Mathf.Clamp01(t / 0.8f));
                onProgress?.Invoke(u);
                yield return null;
            }
            if (activeShot == id) { transform.position = path[path.Count - 1]; activeShot = 0; }
            onProgress?.Invoke(1);
        }

        public void Shake(float amplitude, float duration) { shakeAmp = amplitude; shakeT = duration; }

        // ------------------------------------------------------------------ orbit
        /// Switch to orbit around a pivot. Smoothly takes over from the current camera pose.
        /// fov > 0 eases the lens with the orbit's hand-off (whole-plant framings); pitchMin/pitchMax bound the learner's drag.
        public void Orbit(Vector3 pivotPoint, float distance, float yawDeg, float pitchDeg, bool userControl = true, float minDistance = 1f, float maxDistance = 30f,
            float fov = 0f, float pitchMin = -10f, float pitchMax = 55f)
        {
            pitchDeg = Mathf.Clamp(pitchDeg, pitchMin, pitchMax);
            // a re-frame while already orbiting (Free Explore tiles, Clue 1 after the hub, Phloem, restoreFinal) eases like a takeover instead of
            // snapping at full damping (tau 0.125 s, peak speed on frame 1). Compared with the OLD targets, so re-issuing the same frame mid-hand-off is a no-op.
            bool refocus = Orbiting && ((pivotPoint - pivotTarget).sqrMagnitude > 0.04f || Mathf.Abs(distance - distTarget) > 0.3f
                || Mathf.Abs(pitchDeg - pitchTarget) > 4f || Mathf.Abs(Mathf.DeltaAngle(yawTarget, yawDeg)) > 4f || panOffset.sqrMagnitude > 0.04f);
            minPitch = pitchMin; maxPitch = pitchMax;
            pivotTarget = pivotPoint; distTarget = distance; yawTarget = yawDeg; pitchTarget = pitchDeg; fovTarget = fov;
            minDist = minDistance; maxDist = maxDistance; panOffset = Vector3.zero; UserControl = userControl;
            if (!Orbiting)
            {   // derive current spherical coords so the takeover is seamless
                Vector3 off = transform.position - pivotPoint; dist = off.magnitude; pivot = pivotPoint;
                yaw = Mathf.Atan2(-off.x, -off.z) * Mathf.Rad2Deg; pitch = Mathf.Asin(Mathf.Clamp(off.y / Mathf.Max(dist, 0.001f), -1, 1)) * Mathf.Rad2Deg;
                yaw = yawTarget + Mathf.DeltaAngle(yawTarget, yaw);
                settle = 0f; settleFrom = transform.rotation;   // eased hand-off: no 0.4 s whip, no jump in aim
            }
            else
            {
                yaw = yawTarget + Mathf.DeltaAngle(yawTarget, yaw);   // re-pivot while orbiting: eulerAngles.y is 0..360, never spin the long way
                if (refocus) { settle = 0f; settleFrom = transform.rotation; }   // same 1.2 s ramp: starts from rest, aim stays continuous
            }
            Orbiting = true; activeShot = 0;
        }

        public void StopOrbit() { if (Orbiting) holdDist = dist; Orbiting = false; UserControl = false; }

        void LateUpdate()
        {
            HandleInput();
            bool posed = transform.position.Equals(lastPos) && transform.rotation.Equals(lastRot);   // nothing else (shot, Chase, micro world) moved the camera since last frame
            bool still = posed && Cam.fieldOfView.Equals(lastFov);
            if (posed) transform.position -= shakeOff;   // shake is an offset around the pose, never a wander
            shakeOff = Vector3.zero;
            if (Orbiting && activeShot == 0)
            {
                if (AutoSpin != 0 && !Hud.ReduceMotion && Time.unscaledTime - lastInput > 4f) yawTarget += AutoSpin * Time.deltaTime;   // Reduce motion: no turntable the learner didn't start
                float k = 1 - Mathf.Exp(-damping * Time.deltaTime);
                if (settle < 1f) { settle = Mathf.Min(1f, settle + Time.deltaTime / 1.2f); k *= settle; }   // damping ramps in: ~1 s eased takeover
                yaw = Mathf.Lerp(yaw, yawTarget, k); pitch = Mathf.Lerp(pitch, pitchTarget, k); dist = Mathf.Lerp(dist, distTarget, k);
                if (fovTarget > 0f) Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, fovTarget, k);   // whole-plant framings: the lens rides the same eased hand-off
                pivot = Vector3.Lerp(pivot, pivotTarget + panOffset, k);
                Quaternion q = Quaternion.Euler(pitch, yaw, 0);
                boundArm = float.MaxValue;
                if (BoundExtents.x > 0f)
                {   // shorten the arm, never move the pivot: the lens stays inside the dome at any pitch, pan or zoom
                    var inv = new Vector3(1f / BoundExtents.x, 1f / BoundExtents.y, 1f / BoundExtents.z);
                    Vector3 o = Vector3.Scale(pivot - BoundCentre, inv), a = Vector3.Scale(q * Vector3.back, inv);
                    float aa = a.sqrMagnitude, b = Vector3.Dot(o, a), c = o.sqrMagnitude - 1f;
                    if (c < 0f) { boundArm = (-b + Mathf.Sqrt(b * b - aa * c)) / aa; dist = Mathf.Min(dist, boundArm); }
                }
                transform.SetPositionAndRotation(pivot + q * new Vector3(0, 0, -dist), settle < 1f ? Quaternion.Slerp(settleFrom, q, Fx.Eval(settle, Ease.InOut)) : q);
            }
            if (!Orbiting && activeShot == 0 && still && !Hud.ReduceMotion)
            {   // idle hold: eased asymptotic dolly-in (≤ 0.35 m and ≤ 5 % of the subject distance, ~60 % after 7 s), view direction only
                hold += Time.deltaTime; float x = Mathf.Max(0f, hold - 0.6f) / 3.5f;
                float d = Mathf.Min(0.35f, holdDist * 0.05f) * (1f - (1f + x) * Mathf.Exp(-x));
                transform.position += transform.forward * (d - drift); drift = d;
            }
            else if (!posed || Orbiting || activeShot != 0) { hold = 0f; drift = 0f; }   // a lens-only change pauses the drift without re-arming it
            if (shakeT > 0)
            {
                shakeT -= Time.deltaTime; float a = Hud.ReduceMotion ? 0f : shakeAmp * Mathf.Clamp01(shakeT * 2);   // Reduce motion: zero amplitude, so shakeOff stays 0 and the posed/still bookkeeping holds
                shakeOff = new Vector3(Mathf.PerlinNoise(Time.time * 25, 0) - 0.5f, Mathf.PerlinNoise(0, Time.time * 25) - 0.5f, 0) * a;
                transform.position += shakeOff;
            }
            lastPos = transform.position; lastRot = transform.rotation; lastFov = Cam.fieldOfView;
            if (dof != null)
            {   // Bokeh on the shot's subject during Blend / Arc / Ride / Push only; off under learner control, after cuts and in micro-world flights,
                // so selectable structures, text and molecules stay sharp. High quality tier only (Performance: no DoF pass).
                bool cine = activeShot != 0 && !Hud.ReduceMotion && QualitySettings.GetQualityLevel() >= 1;
                if (cine)
                {
                    float d = Mathf.Max(0.3f, Vector3.Distance(transform.position, shotLook));
                    focusD = dof.active ? Mathf.Lerp(focusD, d, 1f - Mathf.Exp(-6f * Time.deltaTime)) : d;
                    dof.focusDistance.value = focusD;
                }
                if (dof.active != cine) dof.active = cine;
            }
        }

        void HandleInput()
        {
            // The pointer is the primary touch while a finger is down (tablets), otherwise the mouse. One finger drags = rotate; a tap
            // (press and release within 6 px, 20 px for a finger) = Clicked. Zoom: wheel or the HUD + / −; pan stays on the mouse's right button.
            var mouse = Mouse.current; var touch = Touchscreen.current?.primaryTouch;
            bool finger = touch != null && (touch.press.isPressed || touch.press.wasReleasedThisFrame);
            if (!finger && mouse == null) return;
            Vector2 pos = finger ? touch.position.ReadValue() : mouse.position.ReadValue();
            bool pressed = finger ? touch.press.wasPressedThisFrame : mouse.leftButton.wasPressedThisFrame;
            bool released = finger ? touch.press.wasReleasedThisFrame : mouse.leftButton.wasReleasedThisFrame;
            bool held = finger ? touch.press.isPressed : mouse.leftButton.isPressed;
            Vector2 delta = finger ? touch.delta.ReadValue() : mouse.delta.ReadValue();
            bool overUi = UiBlocks != null && UiBlocks(pos);
            if (pressed && !overUi) { pressPos = pos; dragging = true; }
            if (released)
            {
                if (dragging && (pos - pressPos).sqrMagnitude < (finger ? 400f : 36f) && !overUi) Clicked?.Invoke(pos);
                dragging = false;
            }
            if (!Orbiting || !UserControl || activeShot != 0) { if (!overUi && mouse != null) ScrollOnly(mouse); return; }
            if (dragging && held && delta.sqrMagnitude > 0)
            { yawTarget += delta.x * rotateSpeed; pitchTarget = Mathf.Clamp(pitchTarget - delta.y * rotateSpeed, minPitch, maxPitch); lastInput = Time.unscaledTime; }
            if (mouse != null && (mouse.rightButton.isPressed || mouse.middleButton.isPressed) && !overUi)
            {
                Vector2 md = mouse.delta.ReadValue();
                if (md.sqrMagnitude > 0) { panOffset += (-transform.right * md.x - transform.up * md.y) * panSpeed * dist; panOffset = Vector3.ClampMagnitude(panOffset, distTarget * 0.6f); lastInput = Time.unscaledTime; }
            }
            if (!overUi && mouse != null) ScrollOnly(mouse);
        }

        void ScrollOnly(Mouse mouse)
        {
            float s = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(s) < 0.01f) return;
            Zoom(Mathf.Abs(s) > 10f ? s / 120f : s);   // 1 per wheel notch under either ScrollDeltaBehavior (±120 Windows-native, ±1 uniform)
        }

        /// HUD + / − and the wheel: one step per press or notch. UserZoomed always fires; the orbit only moves under learner control.
        public void Zoom(float step)
        {
            UserZoomed?.Invoke(step); lastInput = Time.unscaledTime;
            if (Orbiting && UserControl && activeShot == 0) distTarget = Mathf.Clamp((step > 0f ? Mathf.Min(distTarget, boundArm) : distTarget) * Mathf.Pow(0.85f, step), minDist, maxDist);   // 15 % per step; zooming in starts from the bound-clamped arm, so no notch is dead
        }
        float boundArm = float.MaxValue;   // dome arm limit this frame (MaxValue = unbounded)
        public float Distance => distTarget;
        public float IdleSeconds => Time.unscaledTime - lastInput;   // since the learner last rotated, panned or zoomed
    }
}
