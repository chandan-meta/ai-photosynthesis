using System;
using UnityEngine;

namespace Photosynthesis
{
    /// Clue progression. Clue N+1 only becomes Available when clue N is Discovered (no impossible progression).
    public class ClueBoard
    {
        readonly ClueState[] state = { ClueState.Locked, ClueState.Locked, ClueState.Locked };
        public event Action<ClueId, ClueState> Changed;
        public ClueState this[ClueId id] => state[(int)id];
        public bool AllFound => state[2] == ClueState.Discovered;

        public void Set(ClueId id, ClueState s)
        {
            if (state[(int)id] == s) return;
            state[(int)id] = s; Changed?.Invoke(id, s);
            if (s == ClueState.Discovered && (int)id < 2 && state[(int)id + 1] == ClueState.Locked) Set(id + 1, ClueState.Available);
        }

        /// Restore the board to what it must be when the story reaches `b` (used by debug jumps and reset).
        public void ResetFor(Beat b)
        {
            for (int i = 0; i < 3; i++) state[i] = ClueState.Locked;
            if (b >= Beat.Clue1) state[0] = ClueState.Available;
            if (b > Beat.CO2) { state[0] = ClueState.Discovered; state[1] = ClueState.Available; }
            if (b > Beat.XylemRide) { state[1] = ClueState.Discovered; state[2] = ClueState.Available; }
            if (b > Beat.Sunlight) state[2] = ClueState.Discovered;
            for (int i = 0; i < 3; i++) Changed?.Invoke((ClueId)i, state[i]);
        }
    }

    /// Composition root. Owns every subsystem so nothing searches the scene at runtime.
    [DefaultExecutionOrder(-100)]
    public class Module : MonoBehaviour
    {
        public static Module I { get; private set; }
        public const string QualityKey = "ps.quality";   // PlayerPrefs: the learner's Settings ▸ Graphics quality choice
        /// True for the single frame after the learner/dev presses Skip — tweens and waits fast-forward.
        public static bool Skipping => I != null && I.skipFrame == Time.frameCount;

        [Header("Start")]
        [Tooltip("Beat the module starts on. Overridden by the -beat <Name> command-line argument.")]
        public Beat startBeat = Beat.Scene2Start;
        [Tooltip("Scale of the procedural hero plant (1 = 4.2 m tall).")]
        public float heroPlantScale = 1.25f;

        [Header("Scene (assigned by Photosynthesis ▸ Build Scene)")]
        public Chamber chamber;
        public CameraDirector cam;

        // Built at runtime
        [NonSerialized] public HeroPlant plant;
        [NonSerialized] public Curio curio;
        [NonSerialized] public LeafSurfaceWorld leafSurface;
        [NonSerialized] public SoilWorld soil;
        [NonSerialized] public XylemRide xylem;
        [NonSerialized] public LeafTissueWorld tissue;
        [NonSerialized] public CellWorld cell;
        [NonSerialized] public ChloroplastWorld chloroplast;
        [NonSerialized] public LeafInteriorWorld interior;   // Scene 3 prologue vista (the client's leaf cross-section model)

        // Components on this object
        [NonSerialized] public Hud hud;
        [NonSerialized] public AudioDirector audioDirector;
        [NonSerialized] public Dialogue dialogue;
        [NonSerialized] public Story story;
        [NonSerialized] public AskAI ai;
        [NonSerialized] public DebugTools debugTools;

        public readonly ClueBoard clues = new ClueBoard();
        public readonly Assessment assessment = new Assessment();

        public Beat Beat { get; private set; }
        public event Action<Beat> BeatChanged;
        /// True while the learner is in an open/explorable state where AI world-actions may navigate.
        public bool HubOpen { get; set; }

        int skipFrame = -1;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            // 0 = Performance, 1 = High. A saved choice wins; otherwise integrated GPUs (Intel HD/UHD/Iris, AMD "Radeon(TM) ... Graphics")
            // and cards reporting < 3 GB start on Performance. Unity counts shared RAM on iGPUs, so VRAM alone misses most of them.
            string gpu = SystemInfo.graphicsDeviceName; int vendor = SystemInfo.graphicsDeviceVendorID;
            bool intelDiscrete = vendor == 0x8086 && System.Text.RegularExpressions.Regex.IsMatch(gpu, @"Arc\(TM\) [AB]\d{3}");   // Arc A/B cards are discrete
            bool weakGpu = SystemInfo.graphicsMemorySize < 3000 || (vendor == 0x8086 && !intelDiscrete) || (vendor == 0x1002 && gpu.IndexOf("Graphics", StringComparison.OrdinalIgnoreCase) >= 0);
            int q = PlayerPrefs.GetInt(QualityKey, weakGpu ? 0 : QualitySettings.GetQualityLevel());
            if (q >= 0 && q < QualitySettings.names.Length && q != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(q, true);
            hud = Ensure<Hud>(); audioDirector = Ensure<AudioDirector>(); dialogue = Ensure<Dialogue>();
            story = Ensure<Story>(); ai = Ensure<AskAI>(); debugTools = Ensure<DebugTools>();
            if (!cam && Camera.main) cam = Ensure<CameraDirector>(Camera.main.gameObject);

            var worlds = new GameObject("Micro Worlds").transform;
            plant = HeroPlant.Create(chamber ? chamber.PlantAnchor : transform);
            plant.transform.localScale = Vector3.one * heroPlantScale;   // the 'enormous' centrepiece; all plant queries are world-space
            curio = Curio.Create(transform);
            leafSurface = MicroWorld.Create<LeafSurfaceWorld>(worlds, new Vector3(2000, 0, 0));
            soil = MicroWorld.Create<SoilWorld>(worlds, new Vector3(-2000, 0, 0));
            xylem = MicroWorld.Create<XylemRide>(worlds, new Vector3(0, 0, 2000));
            tissue = MicroWorld.Create<LeafTissueWorld>(worlds, new Vector3(2000, 0, 2000));
            cell = MicroWorld.Create<CellWorld>(worlds, new Vector3(-2000, 0, 2000));
            chloroplast = MicroWorld.Create<ChloroplastWorld>(worlds, new Vector3(0, 0, -2000));
            interior = MicroWorld.Create<LeafInteriorWorld>(worlds, new Vector3(2000, 0, -2000));
        }

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-beat" && Enum.TryParse(args[i + 1], true, out Beat b)) startBeat = b;
            Fx.Sanitize(gameObject); if (chamber) Fx.Sanitize(chamber.gameObject);
            story.Run(startBeat);
        }

        /// Builds the micro worlds one per frame (Story.Play runs it before the first beat). In Start they added ~1–2 s to the
        /// launch frame, during which the window pumped no messages. Show() still builds lazily if a world is needed sooner.
        public System.Collections.IEnumerator PrebuildWorlds()
        {
            foreach (var w in new MicroWorld[] { interior, leafSurface, soil, xylem, tissue, cell, chloroplast })
                if (!w.Built) { w.Prebuild(); yield return null; }
        }

        T Ensure<T>() where T : Component => Ensure<T>(gameObject);
        // Unity's fake-null means `??` is unsafe on components.
        public static T Ensure<T>(GameObject g) where T : Component { var c = g.GetComponent<T>(); return c ? c : g.AddComponent<T>(); }

        public void SetBeat(Beat b) { Beat = b; BeatChanged?.Invoke(b); }
        public void Skip() => skipFrame = Time.frameCount + 1;
    }
}
