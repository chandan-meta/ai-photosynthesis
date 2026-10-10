using System;
using System.Collections;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Photosynthesis
{
    /// Development tools: F1 debug panel (jump to any beat, skip VO, time scale, unlock all, stats) and
    /// command-line automation for testing builds:
    ///   -beat Water            start at a beat
    ///   -autoplay              play the whole module unattended (auto clicks / answers); -wrongfirst exercises reteach
    ///   -shots &lt;dir&gt;          jump through every beat and save a screenshot of each, then quit
    ///   -quitafter &lt;seconds&gt;  quit automatically (CI smoke test)
    ///   -quality &lt;Performance|High|index&gt;  test-only quality override (not saved, so a learner's choice is never overwritten)
    public class DebugTools : MonoBehaviour
    {
        ProfilerRecorder drawCalls, setPass, tris;
        float fps; int frames; float acc;
        string shotsDir; float quitAfter = -1, filmRate = 3f;
        void Awake() => Application.logMessageReceived += CountError;
        void CountError(string msg, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) errors++; }

        void Start()
        {
            var m = Module.I;
            var args = Environment.GetCommandLineArgs();
            int fr = Array.IndexOf(args, "-filmrate");
            if (fr >= 0)
            {
                if (fr + 1 < args.Length && float.TryParse(args[fr + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rate) && rate > 0f) filmRate = rate;
                else Debug.LogWarning("[Photosynthesis] -filmrate needs a positive number of seconds (e.g. 0.5); keeping 3 s");
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autoplay") { m.story.AutoPlay = true; m.dialogue.SkipVoice = true; }
                if (args[i] == "-wrongfirst") m.story.AutoPlayWrongFirst = true;
                if (args[i] == "-perf") perfLog = true;
                if (args[i] == "-shots" && i + 1 < args.Length) shotsDir = args[i + 1];
                if (args[i] == "-film" && i + 1 < args.Length) StartCoroutine(Film(args[i + 1]));
                if (args[i] == "-uishots" && i + 1 < args.Length) StartCoroutine(UiShots(args[i + 1]));
                if (args[i] == "-touchtest") StartCoroutine(TouchTest());
                if (args[i] == "-exportmodels" && i + 1 < args.Length) StartCoroutine(ModelExport.Run(args[i + 1]));
                if (args[i] == "-quitafter" && i + 1 < args.Length) float.TryParse(args[i + 1], out quitAfter);
                if (args[i] == "-quality" && i + 1 < args.Length)
                {
                    int q = Array.FindIndex(QualitySettings.names, n => string.Equals(n, args[i + 1], StringComparison.OrdinalIgnoreCase));
                    if (q < 0 && !int.TryParse(args[i + 1], out q)) q = -1;
                    if (q >= 0 && q < QualitySettings.names.Length) QualitySettings.SetQualityLevel(q, true); else Debug.LogWarning($"[Photosynthesis] -quality {args[i + 1]}: unknown level");
                }
            }
            if (Array.IndexOf(args, "-realvoice") >= 0) m.dialogue.SkipVoice = false;   // autoplay at true VO pacing
            if (perfLog) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }   // measure real headroom
            bool dev = Debug.isDebugBuild || Application.isEditor;
            int speedArg = Array.IndexOf(args, "-testspeed");
            if (dev && speedArg >= 0 && speedArg + 1 < args.Length && float.TryParse(args[speedArg + 1], out float speed))
            {
                Time.timeScale = Mathf.Clamp(speed, 0.25f, 4f);
                Debug.Log($"[Polish] test-only simulation speed {Time.timeScale}");
            }
            if (dev)
                m.hud.BuildDebug((Beat[])Enum.GetValues(typeof(Beat)), b => m.story.Run(b), on => m.dialogue.SkipVoice = on,
                    s => Time.timeScale = s, () => { m.clues.ResetFor(Beat.Equation); });
            if (dev || perfLog)   // a release player measures too (-perf), without the F1 panel
            {
                drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            }
            if (!string.IsNullOrEmpty(shotsDir)) StartCoroutine(Shots());
            int bi = Array.IndexOf(args, "-bannertest");
            if (bi >= 0 && bi + 1 < args.Length) StartCoroutine(BannerTest(args[bi + 1]));
            if (Array.IndexOf(args, "-sciencecheck") >= 0) CheckMolecules();
            if (Array.IndexOf(args, "-biologycheck") >= 0) StartCoroutine(CheckBiology());
            if (Array.IndexOf(args, "-diag") >= 0) StartCoroutine(Diag());
            int hi = Array.IndexOf(args, "-hide"), si = Array.IndexOf(args, "-snap");
            if (hi >= 0 && hi + 1 < args.Length) StartCoroutine(Hide(args[hi + 1].Split(',')));
            if (si >= 0 && si + 1 < args.Length) StartCoroutine(Snap(args[si + 1]));
        }
        int errors; bool perfLog; float nextPerf;

        void CheckMolecules()
        {
            var expectations = new[] { (Mol.CO2, 1, 0, 2), (Mol.H2O, 0, 2, 1), (Mol.O2, 0, 0, 2), (Mol.Glucose, 6, 12, 6) };
            foreach (var (kind, carbon, hydrogen, oxygen) in expectations)
            {
                var molecule = Fx.Molecule(kind, transform); molecule.SetActive(false);
                int c = 0, h = 0, o = 0;
                foreach (var r in molecule.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name != "Atom") continue;
                    var material = r.sharedMaterial;
                    if (material == SurfaceArt.Upgraded("AtomC")) c++;
                    else if (material == SurfaceArt.Upgraded("AtomH")) h++;
                    else if (material == SurfaceArt.Upgraded("AtomO")) o++;
                }
                int bonds = 0; foreach (var r in molecule.GetComponentsInChildren<MeshRenderer>(true)) if (r.name == "Bond") bonds++;
                int expectedBonds = kind == Mol.CO2 ? 4 : kind == Mol.Glucose ? 24 : 2;
                bool correct = c == carbon && h == hydrogen && o == oxygen && bonds == expectedBonds;
                if (kind == Mol.H2O)
                {
                    var atoms = Array.FindAll(molecule.GetComponentsInChildren<MeshRenderer>(true), r => r.name == "Atom");
                    float angle = Vector3.Angle(atoms[1].transform.localPosition - atoms[0].transform.localPosition, atoms[2].transform.localPosition - atoms[0].transform.localPosition);
                    correct &= Mathf.Abs(angle - 104.5f) < .01f;
                    Debug.Log($"[Science] H2O bond angle {angle:0.00} degrees");
                }
                if (!correct) errors++;
                Debug.Log($"[Science] {kind} C:{c} H:{h} O:{o} bond-sticks:{bonds} {(correct ? "PASS" : "FAIL")}");
                Destroy(molecule);
            }
        }

        IEnumerator CheckBiology()
        {
            while (!Module.I.soil.Built) yield return null;
            var textures = new System.Collections.Generic.HashSet<Texture>();
            foreach (var r in Module.I.soil.GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !m.shader.isSupported) { errors++; Debug.LogError("[Biology] Missing/unsupported material " + r.name); continue; }
                    foreach (var property in m.GetTexturePropertyNames()) { var t = m.GetTexture(property); if (t) textures.Add(t); }
                }
                if (r.name != "Root" && r.name != "Root Hairs" && r.name != "Root Hair (hero)" && r.name != "Water Films") continue;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh; var material = r.sharedMaterial;
                bool correct = mesh && mesh.uv.Length == mesh.vertexCount && mesh.tangents.Length == mesh.vertexCount && material.GetTexture("_BumpMap");
                if (r.name != "Water Films") correct &= material.GetTexture("_BaseMap") && material.GetTexture("_OcclusionMap") && material.GetTexture("_MetallicGlossMap");
                if (!correct) errors++;
                Debug.Log($"[Biology] {r.name} {material.shader.name} UV/tangent/maps {(correct ? "PASS" : "FAIL")}");
            }
            long bytes = 0; foreach (var t in textures) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
            Debug.Log($"[Biology] Soil texture objects={textures.Count}, Unity texture-memory bytes={bytes}; errors={errors}");
        }

        IEnumerator BannerTest(string dir)
        {
            Directory.CreateDirectory(dir); var m = Module.I;
            yield return new WaitForSecondsRealtime(4f);
            bool waiting = m.AtStartScreen && !m.story.Running && !m.dialogue.Speaking;
            if (!waiting) errors++;
            Debug.Log($"[Polish] banner gate {(waiting ? "PASS" : "FAIL")}");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "00_banner.png"));
            m.hud.ReviewStartSettings(true); yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "01_help.png")); m.hud.ReviewCloseSystem();
            m.hud.ReviewStartSettings(false); yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "02_settings.png")); m.hud.ReviewCloseSystem();
            yield return null; m.hud.ReviewStartButton(); m.hud.ReviewStartButton();
            yield return new WaitForSecondsRealtime(2f);
            bool started = !m.AtStartScreen && m.story.Running && m.Beat == Beat.Scene2Start;
            if (!started) errors++;
            Debug.Log($"[Polish] start submit / repeated submit {(started ? "PASS" : "FAIL")}");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "03_original_intro.png"));
            m.ShowStartScreen(); yield return new WaitForSecondsRealtime(1f);
            if (!m.AtStartScreen || m.story.Running || m.dialogue.Speaking) errors++;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "04_reset_banner.png"));
            m.story.Run(Beat.Clue1); yield return null;
            bool jumped = !m.AtStartScreen && m.story.Running;
            if (!jumped) errors++;
            Debug.Log($"[Polish] debug jump clears banner {(jumped ? "PASS" : "FAIL")}");
            Debug.Log($"[Polish] banner review complete, errors={errors}");
            yield return new WaitForSecondsRealtime(.6f); Application.Quit();
        }

        void OnDestroy() { Application.logMessageReceived -= CountError; drawCalls.Dispose(); setPass.Dispose(); tris.Dispose(); }

        void Update()
        {
            var k = Keyboard.current;
            if (k != null && k.f1Key.wasPressedThisFrame && (Debug.isDebugBuild || Application.isEditor)) Module.I.hud.ToggleDebug();
            frames++; acc += Time.unscaledDeltaTime;
            if (acc >= 0.5f)
            {
                fps = frames / acc; frames = 0; acc = 0;
                if (drawCalls.Valid && (Debug.isDebugBuild || Application.isEditor))   // release: no stats label to write
                    Module.I.hud.SetStats($"FPS {fps:0} · draw calls {drawCalls.LastValue} · setpass {setPass.LastValue} · tris {tris.LastValue / 1000}k · beat {Module.I.Beat} · level {Module.I.assessment.Level} · errors {errors}");
            }
            if (perfLog && Time.unscaledTime >= nextPerf)
            {   // -1 stays numeric for the perf-summary parser when a release player doesn't expose a counter
                nextPerf = Time.unscaledTime + 2f;
                Debug.Log($"[Perf] {Module.I.Beat} fps {fps:0} dc {(drawCalls.Valid ? drawCalls.LastValue : -1)} setpass {(setPass.Valid ? setPass.LastValue : -1)} tris {(tris.Valid ? tris.LastValue / 1000 : -1)}k gcKB {GC.GetTotalMemory(false) / 1024}");
            }
            if (quitAfter > 0 && Time.realtimeSinceStartup > quitAfter) { Debug.Log($"[Photosynthesis] quitafter reached, errors={errors}"); Application.Quit(); }
            if (Module.I.story.AutoPlay && !Module.I.story.Running && Time.realtimeSinceStartup > 5) { Debug.Log($"[Photosynthesis] autoplay complete, errors={errors}"); Application.Quit(); }
        }

        /// -diag: list transparent / emissive renderers whose bounds overlap the plant focus (finds glare culprits).
        IEnumerator Diag()
        {
            yield return new WaitForSecondsRealtime(8f);
            var focus = Module.I.plant.Base + Vector3.up * 2.1f;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.isVisible || r.bounds.SqrDistance(focus) > 1f) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    var e = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
                    var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                    if (m.renderQueue < 2500 && e.maxColorComponent < 1f) continue;
                    var path = r.name; for (var t = r.transform.parent; t; t = t.parent) path = t.name + "/" + path;
                    Debug.Log($"[Diag] {path} | {m.shader.name} q{m.renderQueue} base {c} emis {e} size {r.bounds.size}");
                }
            }
            static bool Bad(Vector3 v) => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z);
            foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mesh = mf.sharedMesh; if (!mesh || !mesh.isReadable) continue;
                int bv = 0, bn = 0, bt = 0;
                foreach (var v in mesh.vertices) if (Bad(v)) bv++;
                foreach (var n in mesh.normals) if (Bad(n) || n.sqrMagnitude < 1e-8f) bn++;
                foreach (var t in mesh.tangents) if (float.IsNaN(t.x) || float.IsNaN(t.w)) bt++;
                if (bv + bn + bt == 0) continue;
                var path = mf.name; for (var t = mf.transform.parent; t; t = t.parent) path = t.name + "/" + path;
                Debug.Log($"[Diag] BAD MESH {path} ({mesh.name}) badVerts {bv} badNormals {bn} badTangents {bt} of {mesh.vertexCount}");
            }
            foreach (var tr in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (Bad(tr.position) || Bad(tr.lossyScale) || float.IsNaN(tr.rotation.x)) Debug.Log($"[Diag] BAD TRANSFORM {tr.name} parent {(tr.parent ? tr.parent.name : "-")}");
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (l.enabled && l.intensity > 0f) Debug.Log($"[Diag] light {l.name} {l.type} I={l.intensity} range={l.range} at {l.transform.position}");
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (ps.particleCount > 0) Debug.Log($"[Diag] particles {ps.name} n={ps.particleCount} at {ps.transform.position} size~{ps.main.startSize.constantMax} parent {(ps.transform.parent ? ps.transform.parent.name : "-")}");
            // mood chain (why is a micro-world backdrop dark?)
            Debug.Log($"[Diag] fog {RenderSettings.fog} {RenderSettings.fogMode} color {RenderSettings.fogColor} density {RenderSettings.fogDensity} ambient {RenderSettings.ambientMode} sky {RenderSettings.ambientSkyColor} skybox {(RenderSettings.skybox ? RenderSettings.skybox.name : "-")}");
            var cam = Module.I.cam.Cam;
            Debug.Log($"[Diag] camera clear {cam.clearFlags} bg {cam.backgroundColor} far {cam.farClipPlane} pos {cam.transform.position}");
            foreach (var v in FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (v.profile && v.profile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments ca)) Debug.Log($"[Diag] volume {v.name} w={v.weight} postExposure {ca.postExposure.value} contrast {ca.contrast.value} sat {ca.saturation.value}");
                if (v.profile && v.profile.TryGet(out UnityEngine.Rendering.Universal.Vignette vg)) Debug.Log($"[Diag] vignette {vg.intensity.value}");
                if (v.profile && v.profile.TryGet(out UnityEngine.Rendering.Universal.Tonemapping tm)) Debug.Log($"[Diag] tonemap {tm.mode.value}");
            }
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.name == "Backdrop")
                {
                    var m = r.sharedMaterial; var t = m && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") as Texture2D : null;
                    Debug.Log($"[Diag] backdrop {r.transform.parent.name} active {r.gameObject.activeInHierarchy} visible {r.isVisible} bounds {r.bounds.size} shader {(m ? m.shader.name : "-")} base {(m ? m.GetColor("_BaseColor") : Color.clear)} tex {(t ? t.name + " " + t.width + "x" + t.height + " srgb? " + (QualitySettings.activeColorSpace) : "-")}");
                }
            Debug.Log("[Diag] done");
        }

        // -hide a,b,c : deactivate objects by name once the scene is running (bisecting visual problems).
        IEnumerator Hide(string[] names)
        {
            for (float t = 0; t < 12f; t += Time.unscaledDeltaTime)
            {
                foreach (var n in names) { var g = GameObject.Find(n); if (g) { g.SetActive(false); Debug.Log("[Diag] hid " + n); } }
                yield return null;
            }
        }

        /// -touchtest : a simulated touchscreen drags across the Free Explore plant (the camera must turn) and taps it (a click must fire), then quit.
        IEnumerator TouchTest()
        {
            var m = Module.I; yield return new WaitForSecondsRealtime(2f);
            m.story.Run(Beat.FreeExplore); yield return new WaitForSecondsRealtime(5f);
            m.cam.AutoSpin = 0f;   // the idle turntable would turn the camera without any touch
            var ts = InputSystem.AddDevice<Touchscreen>();
            Vector2 a = new Vector2(Screen.width * 0.42f, Screen.height * 0.6f), b = a + new Vector2(Screen.width * 0.14f, 0f);
            float yaw0 = m.cam.transform.eulerAngles.y;
            InputSystem.QueueStateEvent(ts, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Began, position = a }); yield return null; yield return null;
            for (int i = 1; i <= 12; i++) { InputSystem.QueueStateEvent(ts, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Moved, position = Vector2.Lerp(a, b, i / 12f) }); yield return null; }
            InputSystem.QueueStateEvent(ts, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = b }); yield return null;
            yield return new WaitForSecondsRealtime(1.2f);
            float turned = Mathf.Abs(Mathf.DeltaAngle(yaw0, m.cam.transform.eulerAngles.y));
            int taps = 0; Action<Vector2> onTap = p => taps++; m.cam.Clicked += onTap;
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.55f);
            InputSystem.QueueStateEvent(ts, new TouchState { touchId = 2, phase = UnityEngine.InputSystem.TouchPhase.Began, position = c }); yield return null; yield return null;
            InputSystem.QueueStateEvent(ts, new TouchState { touchId = 2, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = c }); yield return null; yield return null;
            yield return new WaitForSecondsRealtime(0.8f);
            m.cam.Clicked -= onTap; InputSystem.RemoveDevice(ts);
            bool ok = turned > 5f && taps == 1; if (!ok) errors++;
            Debug.Log($"[Photosynthesis] touchtest: drag turned the camera {turned:0.0}°, tap fired {taps} click(s) → {(ok ? "ok" : "FAIL")}");
            Debug.Log($"[Photosynthesis] touchtest complete, errors={errors}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        /// -film dir : a frame every 3 s (or -filmrate s) for as long as the module runs (a play-through filmstrip for review;
        /// -filmrate 0.2 with -beat X -quitafter N gives an animation burst).
        IEnumerator Film(string dir)
        {
            Directory.CreateDirectory(dir);
            for (int i = 0; ; i++)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, filmRate));
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{i:000}_{Time.realtimeSinceStartup:0000}s_{Module.I.Beat}.png"));
            }
        }

        /// -uishots dir : every UI state that the beat screenshots never show (system panels, chat states, cards), then quit.
        /// Run at several -screen-width/-height values to check scaling. Story is stopped so nothing moves underneath.
        IEnumerator UiShots(string dir)
        {
            var m = Module.I; var h = m.hud; Directory.CreateDirectory(dir);
            yield return new WaitForSecondsRealtime(2f);
            m.story.Run(Beat.Clue2); yield return new WaitForSecondsRealtime(4f);
            m.story.StopAllCoroutines(); h.CancelWaits(); h.HideTransient();
            m.clues.ResetFor(Beat.Clue2);
            int n = 0;
            IEnumerator Snap(string name, float wait = 0.6f)
            {
                yield return new WaitForSecondsRealtime(wait);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{n++:00}_{name}.png")); yield return null; yield return null;
            }
            m.HubOpen = true; h.ShowLine("Dr. Curio", "Second clue. Let's scan the plant from top to bottom."); h.ShowClueCards(true);   // HubOpen: the hub's pickable rows + chevrons
            h.ShowInstruction("Select CLUE 02 to continue — or ask Dr. Curio.", IconId.Target);
            yield return Snap("hub_cards_instruction");
            h.ShowClueCards(false); h.HideInstruction(); m.HubOpen = false;
            for (int i = 0; i <= 6; i++) { h.DebugSystem(i); yield return Snap(new[] { "pause", "settings_audio", "settings_display", "settings_access", "help", "reset_confirm", "exit_confirm" }[i]); }
            h.DebugSystem(-1);
            h.OpenChat(); h.AddChat(true, "Show me water."); h.AddChat(false, "Follow me! The water trail starts below us — in the roots."); h.ShowWorldAction("Opening Clue 02");
            foreach (VoiceState v in Enum.GetValues(typeof(VoiceState))) { h.SetVoiceState(v); yield return Snap("chat_" + v); }
            h.SetVoiceState(VoiceState.Idle); h.CloseChat();
            h.ShowScanner(ScanState.Detected, "SIGNAL DETECTED BELOW", "Source: beneath the plant", 1f); h.ShowInfo("Xylem", "Carries water and minerals upward from the roots.", IconId.Xylem);
            h.ShowTooltip("Stem", "Holds the leaves up to the light. Xylem carries water up inside it; phloem carries food.", IconId.Stem, new Vector2(Screen.width * 0.55f, Screen.height * 0.5f));
            h.Callout("a", "ROOTS", "Hidden beneath the floor", Tone.Blue, () => m.plant.RootTip + Vector3.up * 0.4f);
            yield return Snap("scanner_info_tooltip_callout");
            h.HideScanner(); h.HideInfo(); h.HideTooltip(); h.ClearCallouts();
            var q = m.assessment.Get("Q4");
            StartCoroutine(h.Question(q, 3, 0, "UNDERSTANDING · ADAPTIVE", c => { })); yield return Snap("question", 0.8f);
            h.MarkAnswer((q.correct + 1) % q.options.Length, false); yield return Snap("question_wrong");
            h.HideQuestion();
            h.ShowReteach("Not quite — let's look again", q.wrongFeedback, new[] { "Plant transparent", "Root", "Xylem", "Leaf" }); h.RestartReteachClock();
            yield return Snap("reteach", 1.5f); h.HideReteach();
            StartCoroutine(h.Feedback(true, "Correct!", q.correctFeedback, "Continue", "See it again", r => { }, q.concept)); yield return Snap("feedback_correct", 0.8f);
            h.CancelWaits();
            StartCoroutine(h.ClueUnlocked(ClueId.Water)); yield return Snap("clue_unlocked", 1.2f); h.HideTransient();
            h.ShowGlucoseUses(true); yield return Snap("glucose_uses"); h.ShowGlucoseUses(false);
            h.ShowInOut(true); yield return Snap("in_out"); h.ShowInOut(false);
            h.ShowComparison(x => { }); yield return Snap("xylem_phloem_toggle"); h.HideComparison();
            StartCoroutine(h.Equation()); yield return Snap("equation", 6f); h.HideEquation();
            StartCoroutine(h.Complete(4, 5, Level.Average, r => { })); yield return Snap("complete", 1.5f); h.CancelWaits();
            h.SetHudVisible(false); h.SetAskAIVisible(false); h.SetProgressVisible(false); h.SetZoomControlVisible(false);   // as Finale(): only the end menu on black
            StartCoroutine(h.Fade(1f, 0f, Color.black)); yield return null; StartCoroutine(h.EndMenu(r => { })); yield return Snap("end_menu", 1f); h.CancelWaits(); StartCoroutine(h.Fade(0f, 0f));
            h.SetHudVisible(true); h.SetCinematic(true);
            StartCoroutine(h.Title("This is Photosynthesis", "Light energy turns water and carbon dioxide into glucose and oxygen", 3f, 80f, 0.1f)); yield return Snap("finale_title", 1.3f);
            h.SetCinematic(false); h.SetAskAIVisible(true); h.SetProgressVisible(true); h.SetZoomControlVisible(true);
            { bool kb = Dialogue.KbClip("Bye for now! I'll be right here if you need me.") != null; if (!kb) errors++; Debug.Log($"[Photosynthesis] kb voice for dynamic replies: {(kb ? "ok" : "MISSING")}"); }
            h.SetProgressVisible(false);   // as Story.FreeExplore: the tracker is gone, the Exit button owns that corner
            h.ShowFreeExplore(k => { }, () => { }); h.SetZoomPath(1); yield return Snap("free_explore"); h.HideFreeExplore();
            h.SetProgressVisible(true);
            h.ShowExperiment((i, on) => { }, () => { }); h.SetExperimentToggle(0, false); h.SetFoodMeters(0f, 0f); yield return Snap("experiment", 1.5f);
            h.SetExperimentToggle(0, true); h.SetExperimentToggle(3, false); h.SetFoodMeters(1f, 0f); yield return Snap("experiment_phloem_off", 1.5f); h.HideExperiment();
            h.ShowZoomPath(2); h.ShowLine("Dr. Curio", "Photosynthesis isn't happening everywhere in here."); yield return Snap("zoom_path_story"); h.ShowZoomPath(-1);
            h.SetCinematic(true); yield return Snap("cinematic_hud", 1f); h.SetCinematic(false); h.HideLine();
            StartCoroutine(h.Loading("ENTERING THE LEAF · LEAF → TISSUE → CELL", "Inside the leaf", "A single leaf can hold millions of chloroplasts.", 3f)); yield return Snap("loading", 1f);
            yield return new WaitForSecondsRealtime(3f);
            h.ToggleDebug(); yield return Snap("debug_panel"); h.ToggleDebug();
            StartCoroutine(h.Title("A New World", "Botanical Investigation Chamber", 3f)); yield return Snap("title", 1.2f);
            Debug.Log($"[Photosynthesis] uishots complete, errors={errors}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        // -snap file.png : one screenshot after 9 s, then quit.
        IEnumerator Snap(string file)
        {
            yield return new WaitForSecondsRealtime(9f);
            ScreenCapture.CaptureScreenshot(file); yield return null; yield return null; yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        /// Screenshot every beat. Each beat runs with autoplay for `settle` seconds before capture.
        IEnumerator Shots()
        {
            var m = Module.I; m.story.AutoPlay = true; m.dialogue.SkipVoice = true;
            Directory.CreateDirectory(shotsDir);
            yield return new WaitForSeconds(1f);
            foreach (Beat b in Enum.GetValues(typeof(Beat)))
            {
                m.story.Run(b);
                float settle = b switch
                {
                    Beat.Scene2Start => 12f, Beat.CurioIntro => 7f, Beat.Clue2 => 8f, Beat.Clue3 => 8f, Beat.Reaction => 7f,
                    Beat.Phloem => 13f, Beat.Equation => 10f, Beat.FinalPlant => 9f, Beat.Stomata => 6f, Beat.CO2 => 6f, Beat.XylemRide => 6f,
                    _ => 5f
                };
                yield return new WaitForSecondsRealtime(settle);
                ScreenCapture.CaptureScreenshot(Path.Combine(shotsDir, $"{(int)b:00}_{b}.png"));
                yield return null; yield return null;
                Debug.Log($"[Photosynthesis] shot {b}, errors so far {errors}");
            }
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log($"[Photosynthesis] shots complete, errors={errors}");
            Application.Quit();
        }
    }
}
