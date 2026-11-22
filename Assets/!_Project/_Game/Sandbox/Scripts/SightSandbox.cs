using Kehai.Karen;
using UnityEngine;

namespace Kehai.Sandbox
{
    // The sight sandbox (0.5.2, Phase 11, C11.5): a walled room with one door, a stock fridge, a
    // stock rack, two tall shelves, three short shelves and four lamps. Karen walks her loop and
    // sees, and does nothing else; you hide and read the meters. Open it from the editor with
    // Kehai → Sandbox → Sight (SandboxMenu): an empty scene with this component, built at load.
    //
    // The layout, in metres (the room is 16 × 12, its door in the middle of the south wall):
    //
    //      north wall                                     z = +6
    //      .  K ......................... rack ........ .
    //      .  (her spot, facing east)                  F    F = the fridge, against the east wall,
    //      .        tall          short                     opening west
    //      .        tall          short     lamp            short shelves hide a crouched body,
    //      .  short                                     .    not a standing person's eyes
    //      .  ................ her loop ................ .
    //      south wall ======  door  ====== panel          z = -6
    //                    you start out here               z = -9
    public sealed class SightSandbox : MonoBehaviour
    {
        public static readonly Vector2 RoomSize = new Vector2(16f, 12f);
        public const string ReplayScene = "Sight sandbox";
        public const float DoorWidth = 1.4f;
        public const float PanelHeight = 1.3f;   // within reach: below eye level (1.6 m)
        public static readonly Vector3 KarenSpot = new Vector3(-6.2f, 0f, 3.6f);
        public const float KarenYaw = 90f;
        public static readonly Vector3 PlayerStart = new Vector3(0f, 0f, -9f);
        public static readonly Vector3 FridgeFront = new Vector3(5.9f, 0f, 1.5f);
        public static readonly Vector3 RackCentre = new Vector3(2.2f, 0f, 4.8f);
        public static readonly Vector3[] TallShelves = { new Vector3(-2.5f, 0f, 1.2f), new Vector3(-2.5f, 0f, -1.8f) };
        public static readonly Vector3[] ShortShelves = { new Vector3(2.0f, 0f, 1.0f), new Vector3(2.0f, 0f, -1.6f), new Vector3(-4.6f, 0f, -0.4f) };
        public static readonly Vector3[] Loop =
        {
            new Vector3(-6.2f, 0f, 3.6f), new Vector3(4.8f, 0f, 3.6f), new Vector3(4.8f, 0f, -4.4f), new Vector3(-6.2f, 0f, -4.4f),
        };

        public bool buildPlayer = true;

        public SandboxKaren Karen { get; private set; }
        public HidingPlace Fridge { get; private set; }
        public HidingPlace Rack { get; private set; }
        public SandboxDoor Door { get; private set; }
        public SandboxIndicators Indicators { get; private set; }
        public PlayerPresence Player { get; private set; }
        public bool Built { get; private set; }

        void Awake()
        {
            if (!Built) Build();
        }

        // Everything, from nothing: the room and its NavMesh, the player, Karen, the panels.
        public void Build()
        {
            if (Built) return;
            Built = true;
            Transform root = transform;
            BuildRoom(root, out var door, out var fridge, out var rack);
            Door = door;
            Fridge = fridge;
            Rack = rack;
            // Bake with the door open, so the doorway is walkable.
            Door.Set(true, instantly: true);
            Physics.SyncTransforms();
            SandboxKit.Bake(gameObject);
            Door.Set(false, instantly: true);

            if (buildPlayer && PlayerPresence.Current == null) Player = SandboxPlayer.Build(root, PlayerStart, 0f);
            else Player = PlayerPresence.Current;

            Karen = SandboxKaren.Build(root, KarenSpot, KarenYaw, Loop);
            Indicators = SandboxIndicators.Build(root, Karen.Body, "Sight sandbox");
            Indicators.status = () => (Karen.Current == SandboxKaren.State.Moving ? "moving" : "frozen") + ", " +
                (Karen.CurrentRoute == SandboxKaren.Route.Loop ? "walking her loop" : "standing and turning");
            Indicators.keys = "F5 start or freeze   F6 restart   ROUTE on the panel   F4 hide these   F3 her cone   Ctrl crouch   Esc frees the mouse";
            Indicators.Watch(Fridge, FridgeFront + new Vector3(-0.3f, 2.75f, 0f));
            Indicators.Watch(Rack, RackCentre + new Vector3(0f, 2.7f, 0f));

            // Two panels: one outside by the door (start her before you go in), one inside.
            float h = PanelHeight;
            Panel(root, "Panel outside", new Vector3(-2.2f, h, -RoomSize.y * 0.5f - SandboxKit.WallThickness * 0.5f - 0.02f), 180f);
            Panel(root, "Panel inside", new Vector3(-2.2f, h, -RoomSize.y * 0.5f + SandboxKit.WallThickness * 0.5f + 0.02f), 0f);
        }

        internal static void BuildRoom(Transform root, out SandboxDoor door, out HidingPlace fridge, out HidingPlace rack)
        {
            Vector3 doorway = SandboxKit.Room(root, new Vector3(0f, 0f, -2f), new Vector2(RoomSize.x + 4f, RoomSize.y + 8f), Vector3.zero, RoomSize, DoorWidth);
            door = SandboxKit.Door(root, doorway, DoorWidth);
            fridge = SandboxKit.StockFridge(root, FridgeFront, 90f);   // opening west, inside towards the east wall
            rack = SandboxKit.StockRack(root, RackCentre, 0f);
            for (int i = 0; i < TallShelves.Length; i++)
                SandboxKit.ShelfUnit(root, "Tall shelf " + (i + 1), TallShelves[i], 0f, SandboxKit.TallShelf);
            for (int i = 0; i < ShortShelves.Length; i++)
                SandboxKit.ShelfUnit(root, "Short shelf " + (i + 1), ShortShelves[i], 0f, SandboxKit.ShortShelf);
            Transform lights = SandboxKit.CeilingLights(root);
            foreach (Vector3 at in new[] { new Vector3(-4f, 2.9f, 2.5f), new Vector3(4f, 2.9f, 2.5f), new Vector3(-4f, 2.9f, -3f), new Vector3(4f, 2.9f, -3f) })
                SandboxKit.Lamp(lights, at);
            SandboxKit.Lamp(lights, new Vector3(0f, 2.9f, -8f), 1.4f);   // outside, by the door
        }

        void Panel(Transform root, string name, Vector3 at, float yaw)
        {
            SandboxButton.Panel(root, name, at, yaw,
                ("START", "Start her: she walks her loop", new Color(0.2f, 0.7f, 0.3f), () => Karen.StartMoving()),
                ("RESTART", "Restart: back to her spot, frozen, meters cleared", new Color(0.8f, 0.6f, 0.15f), () => Karen.Restart()),
                ("FREEZE", "Freeze her where she is (she still sees)", new Color(0.25f, 0.45f, 0.85f), () => Karen.Freeze()),
                ("ROUTE", "Switch: walk her loop, or stand and turn", new Color(0.55f, 0.35f, 0.7f), () => Karen.ToggleRoute()));
        }

        // The keys (C11.3): the same actions as the panel's buttons. Public so the tests press them.
        public bool Press(KeyCode key)
        {
            if (Karen == null) return false;
            switch (key)
            {
                case KeyCode.F4: Indicators.ToggleAll(); return true;
                // F5 starts her, or freezes her if she's moving (F7 and F8 keep their store jobs:
                // a clip mark and the webcam). Route is on the panel.
                case KeyCode.F5:
                    if (Karen.Current == SandboxKaren.State.Moving) Karen.Freeze(); else Karen.StartMoving();
                    return true;
                case KeyCode.F6: Karen.Restart(); return true;
            }
            return false;
        }

        static readonly KeyCode[] Keys = { KeyCode.F4, KeyCode.F5, KeyCode.F6 };

        void Update()
        {
            if (Karen == null) return;
            foreach (KeyCode key in Keys) if (Input.GetKeyDown(key)) Press(key);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }
        }
    }
}
