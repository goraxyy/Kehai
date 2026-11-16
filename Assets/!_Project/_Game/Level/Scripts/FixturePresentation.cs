using System.Collections;
using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runtime migration also covers existing local scenes without serializing art into git.
public sealed class FixturePresentation : MonoBehaviour
{
    readonly List<(Component target, string title)> labels = new List<(Component, string)>();
    GUIStyle style;

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
        yield return null; // KarenFixtures installs the PA during scene startup.
        ApplyPlacement();
        foreach (var fixture in FindObjectsByType<HighlightInteractable>())
        {
            string title = NameOf(fixture);
            if (title != null) labels.Add((fixture, title));
        }
        foreach (string path in new[] { "Staff Room - Furnished/Staff clock", "Office - Furnished/Office clock" })
        {
            var clock = SceneObjectNames.Find(path);
            if (clock != null) labels.Add((clock.transform, "Clock"));
        }
    }

    public static string NameOf(HighlightInteractable fixture)
    {
        if (fixture is CoffeeMachine) return "Coffee machine";
        if (fixture is Puncher) return "Time clock / puncher";
        if (fixture is ClosingMic) return "PA microphone";
        if (fixture is PaBox) return "PA controls";
        if (fixture is CameraBox) return "Camera controls";
        if (fixture is MusicBox) return "Radio";
        if (fixture is FogBox) return "Refrigeration controls";
        if (fixture is ElectricBox) return "Power breakers";
        if (fixture is RoomLightSwitch) return "Light switch";
        return null; // Stock, doors and drawers already have contextual action prompts.
    }

    public static void ApplyPlacement()
    {
        // Identified in the local scene and OfficeRoomBuilder: decorative only, no feed.
        var retired = SceneObjectNames.Find("Office - Furnished/Office camera");
        if (retired != null && retired.GetComponentInChildren<StoreCamera>(true) == null &&
            retired.GetComponentInChildren<Camera>(true) == null) retired.SetActive(false);
        var table = SceneObjectNames.Find("Staff Room - Furnished/Shared dining table") ?? SceneObjectNames.Find("Shared dining table");
        if (table == null || !BoundsOf(table.transform, out Bounds b)) return;
        // Opposite ends of the kitchen table, within reach from either long side.
        var coffee = FindAnyObjectByType<CoffeeMachine>();
        if (coffee != null) RestOn(coffee.transform, b, new Vector3(b.min.x + .42f, b.max.y, b.min.z + .35f));
        var mic = FindAnyObjectByType<ClosingMic>();
        if (mic != null)
        {
            mic.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            RestOn(mic.transform, b, new Vector3(b.max.x - .3f, b.max.y, b.max.z - .16f));
        }
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

    void OnGUI()
    {
        if (Edition.Training || GamePause.Paused || Camera.main == null) return;
        var eye = Camera.main;
        if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
        foreach (var entry in labels)
        {
            if (entry.target == null || !entry.target.gameObject.activeInHierarchy) continue;
            if (!BoundsOf(entry.target.transform, out Bounds b)) continue;
            Vector3 anchor = new Vector3(b.center.x, b.max.y + .16f, b.center.z);
            if (Vector3.Distance(eye.transform.position, b.center) > 4f) continue;
            // Test both the fixture and its label so a high label cannot peek over a wall.
            if (!Visible(eye.transform.position, b.center, entry.target.transform) ||
                !Visible(eye.transform.position, anchor, entry.target.transform)) continue;
            Vector3 pixel = eye.WorldToScreenPoint(anchor);
            if (pixel.z <= 0) continue;
            var rect = new Rect(pixel.x - 120, Screen.height - pixel.y - 12, 240, 24);
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), entry.title, style);
            style.normal.textColor = Color.white;
            GUI.Label(rect, entry.title, style);
        }
    }

    static bool Visible(Vector3 from, Vector3 to, Transform target)
    {
        Vector3 delta = to - from;
        return !Physics.Raycast(from, delta.normalized, out RaycastHit hit, delta.magnitude, ~0, QueryTriggerInteraction.Ignore) ||
            hit.transform == target || hit.transform.IsChildOf(target);
    }
}
