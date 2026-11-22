using Kehai.Karen;
using UnityEngine;
using UnityEngine.SceneManagement;

// What Karen 0.3.0 adds to the store, put in at load as data (like StoreLayout), since scenes
// aren't in git:
//
//   - the WC mop's rack: the WC mop is already a mop; its home becomes a ToolSnapPoint with
//     place "WC", the only rack Tool theft takes from (build plan B3.1);
//   - the fridge control box on the backstreet wall beside the breaker box (B2.1);
//   - the PA box on the staff room wall beside the time clock (B2.2);
//   - every hinged door swings both ways, away from whoever opens it (B4.1);
//   - the fog's look (FogVisuals), and wall clocks whose hands run and stop (WallClockHands).
//
// Each step skips what's already there, and quietly does nothing in a scene without it.
public static class KarenFixtures
{
    public const string WcMop = "WC_Cleaning_Mop_01", WcMopHome = "WC_Mop_Home_01";
    public const float BoxHeight = 1.45f;      // centre of a wall box above the floor, metres
    public const float FogBoxGap = 0.15f;      // between the breaker box and the fridge box
    public const float PaBoxBeside = 0.65f;    // from the time clock to the PA box, along the wall
    public const float MicBeside = 0.45f;      // from the PA box on to the PA microphone (0.4.1)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnLoad()
    {
        Install();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

    public static void Install()
    {
        if (Object.FindAnyObjectByType<ShiftManager>() == null) return;   // not the store
        Material outline = OutlineMaterial();
        WcRack(outline);
        if (Object.FindAnyObjectByType<FogBox>() == null) PlaceFogBox(outline);
        if (Object.FindAnyObjectByType<PaBox>() == null) PlacePaBox(outline);
        if (Object.FindAnyObjectByType<ClosingMic>() == null) PlaceClosingMic(outline);
        PlaceRadio();
        StoreCameras.Install(outline);
        PhaseOneFixtures.Install(outline);
        TwoWayDoors();
        FogVisuals.Ensure();
        WallClockHands.Ensure();
    }

    // The yellow outline the store's own props use.
    static Material OutlineMaterial()
    {
        foreach (OutlineHighlight o in Object.FindObjectsByType<OutlineHighlight>())
            if (o != null && o.outlineMaterial != null) return o.outlineMaterial;
        return null;
    }

    // ---- the WC mop's rack (B3.1) --------------------------------------------------------

    public static ToolSnapPoint WcRack(Material outline)
    {
        GameObject homeGo = SceneObjectNames.Find(WcMopHome);
        GameObject mopGo = SceneObjectNames.Find(WcMop);
        if (homeGo == null || mopGo == null || !mopGo.TryGetComponent(out Item mop)) return null;
        if (homeGo.TryGetComponent(out ToolSnapPoint existing)) return existing;

        // As the staff room's rack: a trigger round the mop, on the interactable layer.
        homeGo.layer = LayerMask.NameToLayer("Interactable");
        var zone = homeGo.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(0.5f, 1.8f, 0.5f);
        var highlight = homeGo.AddComponent<OutlineHighlight>();
        highlight.outlineMaterial = outline;
        highlight.shape = OutlineHighlight.OutlineShape.BoundingBox;
        var rack = homeGo.AddComponent<ToolSnapPoint>();
        rack.tool = mop;
        rack.place = ToolTheftTactic.Rack;
        rack.homeRadius = 0.4f;
        return rack;
    }

    // ---- the fridge control box (B2.1) ---------------------------------------------------

    static void PlaceFogBox(Material outline)
    {
        ElectricBox breakers = Object.FindAnyObjectByType<ElectricBox>();
        if (breakers == null) return;
        BreakerPanel.FaceOf(breakers, out Vector3 face, out Vector3 facing, out Vector3 across, out float halfWidth);
        Vector3 at = face + across * (halfWidth + FogBoxGap + FogBox.Size.x * 0.5f);
        FogBox.Build(WallBoxAt(at, facing), facing, outline);
    }

    // ---- the PA box (B2.2) -----------------------------------------------------------------

    public static PaBox PlacePaBox(Material outline)
    {
        Puncher clock = Object.FindAnyObjectByType<Puncher>();
        if (clock == null) return null;
        Vector3 facing = TowardFloor(clock.transform.position);
        Vector3 across = Vector3.Cross(Vector3.up, facing).normalized;
        Vector3 at = clock.transform.position + across * PaBoxBeside;
        at.y = FloorY(clock.transform.position, facing) + BoxHeight;
        return PaBox.Build(Wall(at, facing), facing, outline);
    }

    // ---- the radio (0.5.2, C12.2) ------------------------------------------------------------

    public const float RadioHeight = 1.4f;   // the radio's centre above the floor, metres
    public const float ReachTop = 1.6f;      // the player's eyes: nothing usable sits above them

    // The scene has the staff room's radio with its centre 2.1 m up, above eye level (the
    // placement audit, C12.1). At load it goes down onto the wall it's by: its centre 1.4 m
    // over the floor in front of it, its back against the wall, its turn kept. A radio already
    // within reach stays where it is, so calling this again moves nothing.
    public static MusicBox PlaceRadio()
    {
        MusicBox radio = Object.FindAnyObjectByType<MusicBox>();
        if (radio == null || !FixturePresentation.BoundsOf(radio.transform, out Bounds b)) return radio;
        // Its own colliders out of the rays while they look for its wall and floor.
        Collider[] own = radio.GetComponentsInChildren<Collider>();
        var wasOn = new bool[own.Length];
        for (int i = 0; i < own.Length; i++) { wasOn[i] = own[i].enabled; own[i].enabled = false; }
        Physics.SyncTransforms();
        try
        {
            Vector3 centre = b.center;
            Vector3 facing = TowardFloor(centre);
            float floor = FloorY(centre, facing);
            if (centre.y - floor <= ReachTop) return radio;
            Vector3 at = centre;
            at.y = floor + RadioHeight;
            Vector3 wall = Wall(at, facing);
            float depth = Mathf.Abs(b.extents.x * facing.x) + Mathf.Abs(b.extents.z * facing.z);
            Vector3 target = wall == at ? at : wall + facing * (depth + 0.01f);
            target.y = at.y;
            radio.transform.position += target - centre;
            return radio;
        }
        finally
        {
            for (int i = 0; i < own.Length; i++) own[i].enabled = wasOn[i];
            Physics.SyncTransforms();
        }
    }

    // ---- the PA microphone (0.4.1; its own table 0.5.1) ------------------------------------

    public const float MicTableHeight = 0.75f;   // top of the little table, metres above the floor
    public const float MicTableAlong = 0.6f;     // along the wall
    public const float MicTableDeep = 0.45f;     // out from the wall
    public const string MicTableName = "PA_Microphone_Table";

    // On the same wall as the time clock and the PA box, one step further along: you clock in,
    // and at closing time you say so, in the same corner. The microphone stands on a small
    // table of its own, so it is within reach of someone standing on the floor.
    public static ClosingMic PlaceClosingMic(Material outline)
    {
        Puncher clock = Object.FindAnyObjectByType<Puncher>();
        if (clock == null) return null;
        Vector3 facing = TowardFloor(clock.transform.position);
        Vector3 across = Vector3.Cross(Vector3.up, facing).normalized;
        float floor = FloorY(clock.transform.position, facing);
        float micY = floor + MicTableHeight + ClosingMic.Size.y * 0.5f + 0.01f;

        Vector3 at = clock.transform.position + across * (PaBoxBeside + MicBeside);
        Vector3 wall = TableWall(at, facing, floor, micY);
        // If that end of the wall is taken (a corner, a door frame), go the other side of the clock.
        if (MicTableBlocked(wall, facing, floor))
            wall = TableWall(clock.transform.position - across * PaBoxBeside, facing, floor, micY);

        BuildMicTable(wall, facing, floor);
        // Standing on the table, its back a hand's width off the wall.
        return ClosingMic.Build(wall + facing * 0.05f, facing, outline);
    }

    // The wall behind `at`, found at the height the microphone will stand.
    static Vector3 TableWall(Vector3 at, Vector3 facing, float floor, float micY)
    {
        at.y = micY;
        return Wall(at, facing);
    }

    static Quaternion Facing(Vector3 facing) => Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.z).normalized);

    static bool MicTableBlocked(Vector3 wall, Vector3 facing, float floor)
    {
        Vector3 centre = wall + facing * (MicTableDeep * 0.5f + 0.03f);
        centre.y = floor + 0.4f;
        return Physics.CheckBox(centre, new Vector3(MicTableAlong * 0.5f, 0.3f, MicTableDeep * 0.5f - 0.02f),
            Facing(facing), ~0, QueryTriggerInteraction.Ignore);
    }

    // A plain side table: a slab on four legs, one box collider, and a carve in the NavMesh.
    public static GameObject BuildMicTable(Vector3 wall, Vector3 facing, float floor)
    {
        var root = new GameObject(MicTableName);
        Vector3 at = wall + facing * (MicTableDeep * 0.5f + 0.005f);
        at.y = floor;
        root.transform.SetPositionAndRotation(at, Facing(facing));
        Color wood = new Color(0.45f, 0.33f, 0.22f);
        Part(root.transform, "Top", new Vector3(0f, MicTableHeight - 0.02f, 0f), new Vector3(MicTableAlong, 0.04f, MicTableDeep), wood);
        float lx = MicTableAlong * 0.5f - 0.04f, lz = MicTableDeep * 0.5f - 0.04f, lh = MicTableHeight - 0.04f;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Part(root.transform, "Leg", new Vector3(sx * lx, lh * 0.5f, sz * lz), new Vector3(0.04f, lh, 0.04f), wood);
        var box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(MicTableAlong, MicTableHeight, MicTableDeep);
        box.center = new Vector3(0f, MicTableHeight * 0.5f, 0f);
        var carve = root.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        carve.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
        carve.size = box.size;
        carve.center = box.center;
        carve.carving = true;
        return root;
    }

    static void Part(Transform parent, string name, Vector3 local, Vector3 size, Color colour)
    {
        GameObject go = KarenProps.Box(name, parent.position, size, colour, parent, collider: false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.identity;
    }

    // ---- doors (B4.1) -----------------------------------------------------------------------

    // Every door opens away from whoever opens it, from either side (the staff room's swung
    // one way until 0.3.0): so a box on one side holds it from both.
    static void TwoWayDoors()
    {
        foreach (HingeDoor door in Object.FindObjectsByType<HingeDoor>())
            if (door != null) door.fixedSwingDirection = false;
    }

    // ---- where things go ---------------------------------------------------------------------

    // The way a wall fixture at `p` faces: away from the nearest wall, found with four short rays
    // along the store's axes. (Not from the store map: this runs as the scene loads.)
    static Vector3 TowardFloor(Vector3 p)
    {
        Vector3 best = Vector3.forward;
        float nearest = float.MaxValue;
        foreach (Vector3 d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            if (Physics.Raycast(p, d, out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore) && hit.distance < nearest)
            {
                nearest = hit.distance;
                best = -d;
            }
        return best;
    }

    // The wall behind a point, looking back along `facing`; the point itself if there's none.
    static Vector3 Wall(Vector3 p, Vector3 facing)
    {
        Vector3 from = p + facing * 0.6f;
        return Physics.Raycast(from, -facing, out RaycastHit hit, 1.6f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : p;
    }

    // Each box sits at the same height over the ground where the employee stands.
    public static Vector3 WallBoxAt(Vector3 at, Vector3 facing)
    {
        at.y += BoxHeight;
        at.y = FloorY(at, facing) + BoxHeight;
        return Wall(at, facing);
    }

    // The floor in front of a wall fixture at `p`. Not straight down the wall line: that lands on
    // whatever hangs on the wall above the fixture (the wall clock over the time clock, its own
    // collider), which put the PA box and microphone 1.7 m too high (0.5.1). Instead drop rays
    // from the fixture's own height at three steps out into the room and take the lowest surface.
    public static float FloorY(Vector3 p, Vector3 facing)
    {
        float lowest = float.MaxValue;
        foreach (float step in new[] { 0.5f, 1f, 1.5f })
        {
            Vector3 from = p + facing * step;
            from.y = p.y;
            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.point.y < lowest) lowest = hit.point.y;
        }
        return lowest < float.MaxValue ? lowest : FloorY(p);
    }

    static float FloorY(Vector3 p) =>
        Physics.Raycast(new Vector3(p.x, p.y + 1.5f, p.z), Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
}
