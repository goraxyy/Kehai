using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Sandbox
{
    // The pursuit sandbox (0.5.2, Phase 11, C11.7): a loop of aisles between two long rows of tall
    // shelves, with a gap to cut through, a stock fridge and a stock rack. Here Karen is her real
    // brain (KarenBrain), so her hunt and her 60 s pursuit after losing you (PostSightPursuit,
    // 0.5.1) are the game's own, with three sandbox switches, all off in the game:
    //   - no tricks: her movement tactics only (patrol, investigate, sweep, ask, withdraw, hunt);
    //   - a catch freezes the test instead of ending the shift;
    //   - her Ledger is a temporary file, so your own save is never read or written.
    // Pacing and the Director's hint are off too (config.hunter, Vital's baseline): she hunts the
    // moment she's sure, and only her senses say where you are.
    //
    // Where it sits: inside the store's sales-floor rectangle, so her map (StoreMap.AreaAt) counts
    // every cell as hers. It's an empty scene, so nothing of the store is there.
    public sealed class PursuitSandbox : MonoBehaviour
    {
        public static readonly Vector3 Centre = new Vector3(55f, 0f, -148f);
        public static readonly Vector2 RoomSize = new Vector2(24f, 16f);
        public const float DoorWidth = 1.4f;
        public const float PanelHeight = 1.3f;   // within reach: below eye level (1.6 m)
        public static readonly Vector3 KarenSpot = new Vector3(-10f, 0f, 5.5f);   // local to Centre
        public const float KarenYaw = 90f;
        public static readonly Vector3 PlayerStart = new Vector3(0f, 0f, -11f);
        public static readonly Vector3 FridgeFront = new Vector3(10.5f, 0f, 5f);
        public static readonly Vector3 RackCentre = new Vector3(-5f, 0f, 7f);
        public const float RowZ = 3f;
        // Two rows of tall shelves along x, seven units each; the north row has a gap in the middle.
        public static IEnumerable<Vector3> Shelves()
        {
            for (int i = -3; i <= 3; i++)
            {
                float x = i * SandboxKit.TallShelf.x;
                if (i != 0) yield return new Vector3(x, 0f, RowZ);
                yield return new Vector3(x, 0f, -RowZ);
            }
        }

        public bool buildPlayer = true;

        public KarenBrain Brain { get; private set; }
        public PlayerPresence Player { get; private set; }
        public HidingPlace Fridge { get; private set; }
        public SandboxDoor FridgeDoor { get; private set; }
        public HidingPlace Rack { get; private set; }
        public SandboxIndicators Indicators { get; private set; }
        public bool Built { get; private set; }
        public bool Ready { get; private set; }   // her brain has started and her shift begun
        public bool Caught { get; private set; }
        public bool Running => Brain != null && Brain.enabled && !Caught;
        public string LedgerPath { get; private set; }

        public static Vector3 World(Vector3 local) => Centre + local;

        void Awake()
        {
            if (!Built) Build();
        }

        public void Build()
        {
            if (Built) return;
            Built = true;
            transform.position = Centre;
            Transform root = transform;
            SandboxDoor door = BuildRoom(root, out HidingPlace fridge, out HidingPlace rack);
            Fridge = fridge;
            Rack = rack;
            FridgeDoor = Fridge.transform.parent.GetComponentInChildren<SandboxDoor>();
            door.Set(true, instantly: true);
            Physics.SyncTransforms();
            SandboxKit.Bake(gameObject);
            door.Set(false, instantly: true);

            if (buildPlayer && PlayerPresence.Current == null) Player = SandboxPlayer.Build(root, World(PlayerStart), 0f);
            else Player = PlayerPresence.Current;
            Brain = BuildBrain();

            Panel(root, "Panel outside", new Vector3(-2.2f, PanelHeight, -RoomSize.y * 0.5f - SandboxKit.WallThickness * 0.5f - 0.02f), 180f);
            Panel(root, "Panel inside", new Vector3(-2.2f, PanelHeight, -RoomSize.y * 0.5f + SandboxKit.WallThickness * 0.5f + 0.02f), 0f);
        }

        // The same room is built for a replay, without an active player, brain or NavMesh.
        internal static SandboxDoor BuildRoom(Transform root, out HidingPlace fridge, out HidingPlace rack)
        {
            Vector3 doorway = SandboxKit.Room(root, new Vector3(0f, 0f, -2f), new Vector2(RoomSize.x + 4f, RoomSize.y + 8f), Vector3.zero, RoomSize, DoorWidth);
            SandboxDoor door = SandboxKit.Door(root, doorway, DoorWidth);
            int n = 0;
            foreach (Vector3 at in Shelves()) SandboxKit.ShelfUnit(root, "Tall shelf " + (++n), at, 0f, SandboxKit.TallShelf);
            fridge = SandboxKit.StockFridge(root, FridgeFront, 90f);
            rack = SandboxKit.StockRack(root, RackCentre, 0f);
            Transform lights = SandboxKit.CeilingLights(root);
            foreach (float x in new[] { -8f, 0f, 8f })
            foreach (float z in new[] { 5.5f, 0f, -5.5f })
                SandboxKit.Lamp(lights, new Vector3(x, 2.9f, z));
            SandboxKit.Lamp(lights, new Vector3(0f, 2.9f, -10f), 1.4f);
            return door;
        }

        KarenBrain BuildBrain()
        {
            // Her Ledger in a temporary file, fresh each time: never your own save.
            LedgerPath = Path.Combine(Application.temporaryCachePath, "kehai_sandbox_ledger.json");
            if (File.Exists(LedgerPath)) File.Delete(LedgerPath);

            var go = new GameObject(GameNames.Antagonist);
            go.SetActive(false);   // configured before her Awake and Start
            go.transform.SetParent(transform, false);
            go.transform.position = World(KarenSpot);
            var brain = go.AddComponent<KarenBrain>();
            KarenConfig c = brain.config;
            c.hunter = true;                // pacing and the Director's hint off
            c.sandboxNoTricks = true;
            c.sandboxCatchFreezes = true;
            c.writeJsonl = false;
            c.learningRecords = false;
            c.ledgerPath = LedgerPath;
            c.start = World(KarenSpot);
            c.seed = 1;
            // Her hurry and run speeds from the player's, as KarenBootstrap does in the store.
            PlayerMotor motor = Player != null ? Player.GetComponent<PlayerMotor>() : null;
            if (motor != null) KarenBootstrap.MatchPlayer(c, motor.walkSpeed, motor.sprintSpeed);
            if (Edition.AllowDebugTools) go.AddComponent<KarenDebugOverlay>();   // F1: her map and belief
            go.SetActive(true);
            brain.SandboxCaught += OnCaught;
            return brain;
        }

        // Her brain's Start builds her body, map and belief; then her shift begins, and she
        // waits, frozen, for Start.
        IEnumerator Start()
        {
            while (Brain != null && Brain.Body == null) yield return null;
            if (Brain == null) yield break;
            Brain.BeginShift();
            Brain.Body.transform.rotation = Quaternion.Euler(0f, KarenYaw, 0f);
            Freeze();
            Indicators = SandboxIndicators.Build(transform, Brain.Body, "Pursuit sandbox");
            Indicators.status = Status;
            Indicators.extra = Extra;
            Indicators.keys = "F5 start   F6 restart   F7 freeze   F1 her map   F4 hide these   F3 her cone   Ctrl crouch   Esc frees the mouse";
            Indicators.Watch(Fridge, FridgeFront + new Vector3(-0.3f, 2.5f, 0f));
            Indicators.Watch(Rack, RackCentre + new Vector3(0f, 2.7f, 0f));
            Ready = true;
        }

        void Panel(Transform root, string name, Vector3 at, float yaw)
        {
            SandboxButton.Panel(root, name, at, yaw,
                ("START", "Start her: she roams, and hunts you if she's sure", new Color(0.2f, 0.7f, 0.3f), StartRunning),
                ("RESTART", "Restart: back to her spot, a new shift, frozen", new Color(0.8f, 0.6f, 0.15f), Restart),
                ("FREEZE", "Freeze her brain where she is", new Color(0.25f, 0.45f, 0.85f), Freeze));
        }

        public void StartRunning()
        {
            if (Brain == null || Caught) return;
            Brain.enabled = true;
        }

        public void Freeze()
        {
            if (Brain == null) return;
            Brain.enabled = false;
            if (Brain.Body != null) Brain.Body.Stop();
        }

        // A new shift for her: her belief, hunt and awareness cleared, back on her spot, frozen.
        public void Restart()
        {
            if (Brain == null || Brain.Body == null) return;
            Caught = false;
            Brain.BeginShift();
            Brain.Body.Warp(World(KarenSpot));
            Brain.Body.transform.rotation = Quaternion.Euler(0f, KarenYaw, 0f);
            Freeze();
        }

        void OnCaught()
        {
            Caught = true;
            Freeze();
        }

        string Status()
        {
            if (Brain == null) return "";
            if (Caught) return "CAUGHT YOU: the test is frozen; press RESTART";
            return Running ? "running" : "frozen";
        }

        IEnumerable<string> Extra()
        {
            if (Brain == null) yield break;
            float now = Time.time;
            PostSightPursuit pursuit = Brain.Pursuit;
            string plan = Brain.CurrentPlan != null ? Brain.CurrentPlan.Name : "none";
            yield return $"Her action: {Brain.Actions.Current}   plan: {plan}";
            if (Brain.IsChasing && pursuit.Active(now))
                yield return pursuit.Searching
                    ? $"Hunting: lost you; searching, {pursuit.Left(now):0} s of the hunt left, {pursuit.SpotsSearched} spots searched"
                    : $"Hunting: {(Brain.Body.Sight.SeesNow ? "she sees you" : $"lost you {now - pursuit.LastSighting:0.0} s ago, running on")}, {pursuit.Left(now):0} s of the hunt left";
            else yield return Brain.IsChasing ? "Hunting" : "Not hunting";
            ThoughtRecord last = Brain.Log.Latest(1).LastOrDefault();
            if (last != null) yield return "Last thought: " + last.Text;
        }

        // The keys (C11.3): the same actions as the panel's buttons. Public so the tests press them.
        public bool Press(KeyCode key)
        {
            if (!Ready) return false;
            switch (key)
            {
                case KeyCode.F4: Indicators.ToggleAll(); return true;
                case KeyCode.F5: StartRunning(); return true;
                case KeyCode.F6: Restart(); return true;
                case KeyCode.F7: Freeze(); return true;
            }
            return false;
        }

        static readonly KeyCode[] Keys = { KeyCode.F4, KeyCode.F5, KeyCode.F6, KeyCode.F7 };

        void Update()
        {
            if (!Ready) return;
            foreach (KeyCode key in Keys) if (Input.GetKeyDown(key)) Press(key);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }
        }

        void OnDestroy()
        {
            if (Brain != null) Brain.SandboxCaught -= OnCaught;
            if (!string.IsNullOrEmpty(LedgerPath) && File.Exists(LedgerPath)) File.Delete(LedgerPath);
        }
    }
}
