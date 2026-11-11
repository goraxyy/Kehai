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
        Vector3 at = face + across * (halfWidth + FogBoxGap + FogBox.Size.x * 0.5f) + Vector3.up * (FloorY(face) + BoxHeight);
        FogBox.Build(Wall(at, facing), facing, outline);
    }

    // ---- the PA box (B2.2) -----------------------------------------------------------------

    static void PlacePaBox(Material outline)
    {
        Puncher clock = Object.FindAnyObjectByType<Puncher>();
        if (clock == null) return;
        Vector3 facing = TowardFloor(clock.transform.position);
        Vector3 across = Vector3.Cross(Vector3.up, facing).normalized;
        Vector3 at = clock.transform.position + across * PaBoxBeside;
        at.y = FloorY(clock.transform.position) + BoxHeight;
        PaBox.Build(Wall(at, facing), facing, outline);
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

    static float FloorY(Vector3 p) =>
        Physics.Raycast(new Vector3(p.x, p.y + 1.5f, p.z), Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
}
