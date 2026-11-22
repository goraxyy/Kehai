#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Kehai.Eval;
using Kehai.Karen;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kehai.Sandbox
{
    // The trick stalls stand outside the store. Its shift, targets, jobs, witnesses and the
    // player's hands are the real ones. The keyboard fires the same trigger from inside.
    [DefaultExecutionOrder(200)] // after KarenBrain.Start has built her body and context
    public sealed class TricksSandbox : MonoBehaviour
    {
        public const string StoreScene = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
        public const string SessionKey = "Kehai.Sandbox.Tricks";
        // The recorded bot also starts on shift four. Force bypasses career unlocks, but
        // each tactic still checks its own prerequisites (e.g. overtime's last two minutes).
        public const int ShiftNumber = 4;
        // On the sandbox island (SandboxIsland, built in every editor Play of the store), near its south
        // edge where you walk on from the road, east of the sight area; facing the road.
        public static readonly Vector3 StallsAt = new Vector3(-25f, 0f, -40f);
        const float ColumnSpacing = 1.25f;
        const int Rows = 3;
        static int Columns => (Catalogue.Count + Rows - 1) / Rows;
        public static IReadOnlyList<string> Catalogue => TrickCheck.Tricks;

        static KarenConfig previousConfig;
        static bool previousDisabled, installed;
        static int? previousCompleted;
        static int previousSeed;
        KarenBrain brain;
        string ledgerPath;
        int selected;
        bool runOne, resumeAgent;
        string feedback = "Waiting for the store.";

        public string Feedback => feedback;

        internal static KarenConfig NewConfig() => new KarenConfig
        {
            seed = 4242, rung = KarenRung.E_Ledger,
            sandboxNoTricks = true, sandboxCatchFreezes = true,
            learningRecords = false, writeJsonl = false,
            ledgerPath = Path.Combine(Application.temporaryCachePath, "tricks_" + Guid.NewGuid().ToString("N") + ".json")
        };

        // Before ShiftManager.Awake or StoreLayout reads the career. The editor holds the
        // override through teardown, including a scene reload or Play with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            previousConfig = KarenBootstrap.Override;
            previousDisabled = KarenBootstrap.Disabled;
            previousCompleted = CareerRuntime.PendingCompletedShifts;
            previousSeed = CareerRuntime.Seed;
            installed = true;
            KarenBootstrap.Override = NewConfig(); // CareerRuntime disables disk reads/writes
            KarenBootstrap.Disabled = true;       // the room installs the brain without recorders or webcam
            CareerRuntime.PendingCompletedShifts = ShiftNumber - 1;
            CareerRuntime.Seed = KarenBootstrap.Override.seed;
            KehaiEnv.Ensure(); // keeps the title menu closed, without engaging an AgentDriver
            SceneManager.sceneLoaded += OnLoaded;
        }

        public static void EndSession()
        {
            if (!installed) return;
            SceneManager.sceneLoaded -= OnLoaded;
            KarenBootstrap.Override = previousConfig;
            KarenBootstrap.Disabled = previousDisabled;
            CareerRuntime.PendingCompletedShifts = previousCompleted;
            CareerRuntime.Seed = previousSeed;
            installed = false;
        }

        static void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            // Not only Single: Play's first load of the scene reports another mode (the island found it).
            if (scene.path != StoreScene) return;
            var root = new GameObject("Tricks sandbox");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<TricksSandbox>();
        }

        void Awake()
        {
            if (!installed) { enabled = false; return; }
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null || FindAnyObjectByType<ShiftManager>() == null)
            {
                feedback = "The store needs its player and shift manager.";
                return;
            }

            player.GetOrAdd<PlayerPresence>();
            player.GetOrAdd<FootprintTrail>();
            foreach (CustomerNPC npc in FindObjectsByType<CustomerNPC>())
                npc.gameObject.GetOrAdd<CustomerMemory>();

            // Every reload starts with a fresh Ledger, including F6 after a catch.
            KarenConfig config = NewConfig();
            ledgerPath = config.ledgerPath;
            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            if (motor != null) KarenBootstrap.MatchPlayer(config, motor.walkSpeed, motor.sprintSpeed);
            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null)
            {
                config.bodyRadius = Mathf.Clamp(controller.radius, 0.25f, 0.6f);
                config.bodyHeight = Mathf.Max(1.5f, controller.height);
            }
            var karen = new GameObject(GameNames.Antagonist);
            brain = karen.AddComponent<KarenBrain>();
            brain.config = config;
            brain.SandboxCaught += Caught;
        }

        void Start()
        {
            if (brain == null) return;
            Freeze();
            feedback = "F5 starts shift four. The trick stalls stand on the sandbox island: out of the front door, north to the road, then west.";
            // Facing south: the plates face you as you come onto the island from the road.
            Transform stalls = SandboxKit.Group(transform, "Trick stalls", StallsAt, 180f);
            BuildStalls(stalls, id => TryFire(id));
            var controls = SandboxButton.Panel(stalls, "Shift controls",
                new Vector3(-(Columns + 1) * ColumnSpacing / 2f, 1.25f, 0f), 0f,
                ("START", "Start the shift and release Karen", SandboxKit.Rack, StartRunning),
                ("RESTART", "Restart the private shift", SandboxKit.Rack, Restart),
                ("FREEZE", "Freeze Karen", SandboxKit.Rack, Freeze));
            foreach (var button in controls) button.gameObject.layer = LayerMask.NameToLayer("Interactable");
        }

        public static void BuildStalls(Transform root, Action<string> fire)
        {
            int layer = LayerMask.NameToLayer("Interactable");
            for (int i = 0; i < Catalogue.Count; i++)
            {
                string id = Catalogue[i];
                string title = TacticLibrary.Get(id)?.Title ?? id;
                int col = i / Rows, row = i % Rows;
                float x = ((Columns - 1) * 0.5f - col) * ColumnSpacing;
                if (row == 0)
                    SandboxKit.Box(root, "Stand", new Vector3(x, 0.70f, -0.07f),
                        new Vector3(0.08f, 1.40f, 0.08f), SandboxKit.Rack);
                var at = new Vector3(x, 1.40f - row * 0.36f, 0f);
                var plate = SandboxKit.Box(root, id, at, new Vector3(1.18f, 0.30f, 0.09f), SandboxKit.Shelf);
                plate.layer = layer;
                var trigger = plate.AddComponent<SandboxButton>();
                trigger.label = title;
                trigger.prompt = "Try " + title;
                trigger.pressed = () => fire(id);
                var label = SandboxButton.Label(root, title, at + Vector3.forward * 0.055f, 0.04f);
                float width = label.GetComponent<Renderer>().localBounds.size.x;
                if (width > 1.08f) label.transform.localScale *= 1.08f / width;
            }
        }

        public bool TryFire(string id)
        {
            int index = Array.IndexOf(TrickCheck.Tricks, id);
            if (index < 0) { feedback = "Unknown sandbox trick: " + id; return false; }
            selected = index;
            if (brain == null || brain.Ctx == null) { feedback = "Waiting for the store."; return false; }
            if (brain.KilledThisShift) { feedback = "Caught. F6 restarts the sandbox."; return false; }
            if (brain.ForcedPlanRunning)
            {
                feedback = $"Still running {brain.CurrentPlan.Name}. F5 resumes if frozen.";
                return false;
            }
            if (!brain.Force(id, out string why))
            {
                feedback = $"{id} refused: {why}";
                return false;
            }
            runOne = !brain.enabled;
            Resume();
            feedback = "Started " + id;
            return true;
        }

        void Resume()
        {
            var agent = brain.Body.Agent;
            if (resumeAgent && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                agent.isStopped = false;
            resumeAgent = false;
            brain.enabled = true;
        }

        void Freeze()
        {
            if (!brain.enabled) return;
            var agent = brain.Body.Agent;
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                resumeAgent = !agent.isStopped;
                agent.isStopped = true; // keep the path so F5 can resume the current step
            }
            brain.enabled = false;
            feedback = "Karen is frozen.";
        }

        void Caught()
        {
            Freeze();
            feedback = "Caught. F6 restarts the sandbox.";
        }

        void StartRunning()
        {
            if (brain.KilledThisShift) { feedback = "Caught. F6 restarts the sandbox."; return; }
            if (!brain.Shift.IsShiftActive) brain.Shift.StartShift();
            runOne = false;
            Resume();
            feedback = "Karen is moving.";
        }

        void Restart() => ShiftRestart.Reload(ShiftNumber - 1, StoreScene);

        void Update()
        {
            if (GamePause.Paused || brain == null || brain.Ctx == null) return;
            if (Input.GetKeyDown(KeyCode.F6)) { Restart(); return; }
            if (runOne && !brain.ForcedPlanRunning) { runOne = false; Freeze(); }
            // F5 starts or freezes her; F7 stays the store's clip mark.
            if (Input.GetKeyDown(KeyCode.F5)) { if (brain.enabled && !runOne) Freeze(); else StartRunning(); }
            int count = Catalogue.Count;
            if (count == 0) return;
            if (Input.GetKeyDown(KeyCode.LeftBracket)) selected = (selected + count - 1) % count;
            if (Input.GetKeyDown(KeyCode.RightBracket)) selected = (selected + 1) % count;
            if (Input.GetKeyDown(KeyCode.Return)) TryFire(Catalogue[selected]);
        }

        void OnGUI()
        {
            if (GamePause.Paused) return;
            float width = Mathf.Min(465f, Screen.width - 16f);
            GUILayout.BeginArea(new Rect(Screen.width - width - 8f, 8f, width, Screen.height - 16f));
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Tricks sandbox  ·  " + (brain != null && brain.enabled ? "moving" : "frozen"));
            GUILayout.Label("F5 start or freeze    F6 restart");
            GUILayout.Label("E at a stall    [  ] select anywhere    Return to fire");
            GUILayout.Label(feedback);
            if (Catalogue.Count > 0 && brain != null && brain.Ctx != null)
            {
                Tactic t = TacticLibrary.Get(Catalogue[selected]);
                if (t != null)
                {
                    GUILayout.Label(t.Title + "  ·  " + t.Chore);
                    if (brain.ShiftActive && t.FixAt(brain.Ctx, out Vector3 at))
                        GUILayout.Label("Chore at " + brain.Map.NameAt(at));
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void OnDestroy()
        {
            if (brain != null) brain.SandboxCaught -= Caught;
            if (ledgerPath != null && File.Exists(ledgerPath)) File.Delete(ledgerPath);
        }
    }
}
#endif
