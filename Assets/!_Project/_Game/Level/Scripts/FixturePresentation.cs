using System.Collections;
using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;
using UnityEngine.SceneManagement;

// 0.5.1 item 4 and 12. Names float above the things you can use, and the old decorative office
// camera stays gone. Runtime migration also covers existing local scenes without serializing art
// into git.
//
// The names are world-space text (a legacy TextMesh with a black copy behind it), not OnGUI: they
// sit behind walls and furniture like any other object, and a picture taken in batch mode shows
// them. A label shows only within 4 m of the camera and with a clear line to it; not in the
// training edition and not while paused. It never lights anything: no light, no emissive.
public sealed class FixturePresentation : MonoBehaviour
{
    public const float ShowWithin = 4f;       // metres from the camera
    public const float Above = 0.15f;         // gap over the top of the fixture, metres
    public const float CharacterSize = 0.0125f;
    public const int FontSize = 64;           // 64 * 0.0125 * 0.1 = a 0.08 m line
    public const string LabelName = "Fixture_Label";
    public const float RescanSeconds = 5f;    // fixtures other scripts add a little late

    // Where the coffee machine goes at load: the path of a surface, or empty to leave it where
    // the scene has it (the staff-room kitchen counter). One line to change: e.g.
    // "Staff Room - Furnished/Shared dining table" puts it on the dining table.
    public const string CoffeeMachineSurface = "";

    static readonly string[] ClockPaths = { "Staff Room - Furnished/Staff clock", "Office - Furnished/Office clock" };
    static Font font;

    sealed class Label
    {
        public Transform target;
        public GameObject root;
        public bool shown;
    }

    readonly List<Label> labels = new List<Label>();
    float nextScan;

    public int LabelCount => labels.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnLoad()
    {
        Install();
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode) => Install();

    static void Install()
    {
        if (FindAnyObjectByType<ShiftManager>() != null && FindAnyObjectByType<FixturePresentation>() == null)
            new GameObject("Fixture names and placement").AddComponent<FixturePresentation>();
    }

    IEnumerator Start()
    {
        yield return null; // KarenFixtures installs the PA and the rest during scene startup.
        ApplyPlacement();
        BuildLabels();
        nextScan = Time.unscaledTime + RescanSeconds;
    }

    public static string NameOf(HighlightInteractable fixture)
    {
        if (fixture is CoffeeMachine) return "Coffee machine";
        if (fixture is Puncher) return "Time clock (Puncher)";
        if (fixture is ClosingMic) return "PA microphone";
        if (fixture is PaBox) return "PA controls";
        if (fixture is CameraBox) return "Camera controls";
        if (fixture is MusicBox) return "Radio";
        if (fixture is FogBox) return "Refrigeration controls";
        if (fixture is ElectricBox) return "Power breakers";
        if (fixture is RoomLightSwitch) return "Light switch";
        return null; // Stock, doors and drawers already have contextual action prompts.
    }

    // ---- placement ---------------------------------------------------------------------------

    // Safe to run any number of times. The microphone is not moved here: KarenFixtures builds it
    // on its own table.
    public static void ApplyPlacement()
    {
        // The reported office camera was decorative, with no feed; a scene the builder has not
        // been rerun on still has it.
        foreach (string path in new[] { "Office - Furnished/Office camera", "Supermarket/Office/Furniture_And_Props/Office_Camera_01" })
        {
            var retired = SceneObjectNames.Find(path);
            if (retired != null && IsRetiredOfficeCamera(retired)) retired.SetActive(false);
        }
        if (CoffeeMachineSurface.Length == 0) return;
        var table = SceneObjectNames.Find(CoffeeMachineSurface);
        if (table == null || !BoundsOf(table.transform, out Bounds b)) return;
        var coffee = FindAnyObjectByType<CoffeeMachine>();
        if (coffee != null) RestOn(coffee.transform, b, new Vector3(b.min.x + .42f, b.max.y, b.min.z + .35f));
    }

    // The bare mesh prop the office builder used to place: named "Office camera" when authored
    // (Office_Camera_01 in the organised scene), with no Camera and no StoreCamera beneath it.
    // A real feed, even a disabled one, is never matched.
    public static bool IsRetiredOfficeCamera(GameObject go)
    {
        if (go == null) return false;
        if (SceneObjectNames.Original(go) != "Office camera" && go.name != "Office camera" && go.name != "Office_Camera_01") return false;
        return go.GetComponentInChildren<StoreCamera>(true) == null && go.GetComponentInChildren<Camera>(true) == null;
    }

    static void RestOn(Transform fixture, Bounds table, Vector3 bottom)
    {
        if (!BoundsOf(fixture, out Bounds b)) return;
        // Keep the whole model on the top, including imported meshes with off-centre pivots.
        float insetX = Mathf.Min(b.extents.x + .03f, table.extents.x);
        float insetZ = Mathf.Min(b.extents.z + .03f, table.extents.z);
        bottom.x = Mathf.Clamp(bottom.x, table.min.x + insetX, table.max.x - insetX);
        bottom.z = Mathf.Clamp(bottom.z, table.min.z + insetZ, table.max.z - insetZ);
        fixture.position += bottom - new Vector3(b.center.x, b.min.y, b.center.z);
    }

    public static bool BoundsOf(Transform target, out Bounds bounds)
    {
        bounds = new Bounds(target.position, Vector3.zero); bool found = false;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
        }
        return found;
    }

    // ---- the names -----------------------------------------------------------------------------

    // One label per fixture, made once: a second call adds only what is new.
    public void BuildLabels()
    {
        foreach (var fixture in FindObjectsByType<HighlightInteractable>())
        {
            string title = NameOf(fixture);
            if (title != null) AddLabel(fixture.transform, title);
        }
        foreach (string path in ClockPaths)
        {
            var clock = SceneObjectNames.Find(path);
            if (clock != null) AddLabel(clock.transform, "Clock");
        }
    }

    public GameObject LabelOf(Transform target)
    {
        foreach (var l in labels) if (l.target == target) return l.root;
        return null;
    }

    public bool AddLabel(Transform target, string title)
    {
        if (target == null || LabelOf(target) != null || !BoundsOf(target, out Bounds b)) return false;
        // Not a child of the fixture: its outline is drawn round the fixture's renderers.
        var root = new GameObject(LabelName + " - " + title);
        root.transform.SetParent(transform, false);
        root.transform.position = new Vector3(b.center.x, b.max.y + Above, b.center.z);
        Text(root.transform, "Shadow", title, new Color(0f, 0f, 0f, 0.85f), new Vector3(0.004f, -0.004f, 0.004f));
        Text(root.transform, "Text", title, Color.white, Vector3.zero);
        root.SetActive(false);
        labels.Add(new Label { target = target, root = root });
        return true;
    }

    static void Text(Transform parent, string name, string text, Color colour, Vector3 local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var mesh = go.AddComponent<TextMesh>();
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.font = font;
        mesh.text = text;
        mesh.fontSize = FontSize;
        mesh.characterSize = CharacterSize;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = colour;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan && nextScan > 0f) { nextScan = Time.unscaledTime + RescanSeconds; BuildLabels(); }
        Camera eye = Camera.main;
        UpdateLabels(eye != null ? eye.transform : null, Edition.Training || GamePause.Paused);
    }

    // Shows each label that is near and in the clear, turned to face the eye (yaw only).
    public void UpdateLabels(Transform eye, bool suppressed)
    {
        for (int i = labels.Count - 1; i >= 0; i--)
        {
            Label l = labels[i];
            if (l.target == null)
            {
                if (l.root != null) DestroyImmediateOrNot(l.root);
                labels.RemoveAt(i);
                continue;
            }
            Vector3 anchor = l.root.transform.position;
            bool show = !suppressed && eye != null && l.target.gameObject.activeInHierarchy &&
                Vector3.Distance(eye.position, anchor) <= ShowWithin && Visible(eye.position, anchor, l.target);
            if (show)
            {
                Vector3 away = anchor - eye.position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f) l.root.transform.rotation = Quaternion.LookRotation(away);
            }
            if (show != l.shown) { l.shown = show; l.root.SetActive(show); }
        }
    }

    static void DestroyImmediateOrNot(GameObject go)
    {
        if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
    }

    // True with nothing solid between the eye and the label, apart from the fixture itself.
    public static bool Visible(Vector3 from, Vector3 to, Transform target)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 1e-3f) return true;
        foreach (RaycastHit hit in Physics.RaycastAll(from, delta / length, length, ~0, QueryTriggerInteraction.Ignore))
            if (hit.transform != target && !hit.transform.IsChildOf(target)) return false;
        return true;
    }
}
