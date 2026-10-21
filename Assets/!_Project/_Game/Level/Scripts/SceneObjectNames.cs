using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The hierarchy is an authoring view. Gameplay identities survive display-name changes.
// One registry per scene avoids adding a component to every shelf part or prefab instance.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class SceneObjectNames : MonoBehaviour
{
    [Serializable]
    public sealed class Entry
    {
        public Transform target;
        public string originalName, originalPath;
        public Transform originalParent;
        public int originalSibling;
    }

    [HideInInspector] public List<Entry> entries = new List<Entry>();
    [HideInInspector] public List<Transform> folders = new List<Transform>();
    [HideInInspector] public bool organized;

    static Dictionary<Transform, Entry> identities;
    static List<Entry> ordered;

    void OnEnable() => Invalidate();
    void OnDisable() => Invalidate();
    void OnValidate() => Invalidate();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Invalidate() { identities = null; ordered = null; }

    static void Ensure()
    {
        if (identities != null) return;
        identities = new Dictionary<Transform, Entry>();
        ordered = new List<Entry>();
        foreach (var map in UnityEngine.Object.FindObjectsByType<SceneObjectNames>(FindObjectsInactive.Include))
            foreach (var entry in map.entries)
                if (entry.target != null)
                {
                    identities[entry.target] = entry;
                    ordered.Add(entry);
                }
    }

    public static string Original(UnityEngine.Object value)
    {
        if (value == null) return "";
        Transform t = value is GameObject go ? go.transform : (value as Component)?.transform;
        Ensure();
        return t != null && identities.TryGetValue(t, out var entry) ? entry.originalName : value.name;
    }

    // Same active-object contract as GameObject.Find, with support for pre-migration paths.
    public static GameObject Find(string nameOrPath)
    {
        Ensure();
        string path = nameOrPath.TrimStart('/');
        var activeScene = SceneManager.GetActiveScene();
        GameObject otherScene = null;
        foreach (var entry in ordered)
        {
            if (entry.target == null || !entry.target.gameObject.activeInHierarchy) continue;
            if (entry.originalName != path && entry.originalPath != path) continue;
            if (entry.target.gameObject.scene == activeScene) return entry.target.gameObject;
            if (otherScene == null) otherScene = entry.target.gameObject;
        }
        return otherScene != null ? otherScene : GameObject.Find(nameOrPath);
    }

    public static Transform Child(Transform parent, string name)
    {
        if (parent == null) return null;
        Ensure();
        foreach (var entry in ordered)
            if (entry.target != null && entry.originalParent == parent && entry.originalName == name)
                return entry.target;
        var exact = parent.Find(name);
        if (exact != null) return exact;
        // A duplicated, organized assembly has no registry entry yet. Its compact names
        // still resolve (Left_Door_01 -> LeftDoor), until the next explicit organization.
        string key = NameKey(name);
        foreach (Transform child in parent) if (NameKey(child.name) == key) return child;
        return null;
    }

    public static bool EndsWith(UnityEngine.Object value, string word) =>
        NameKey(Original(value)).EndsWith(NameKey(word), StringComparison.Ordinal);

    static string NameKey(string name)
    {
        int end = name.Length;
        while (end > 0 && char.IsDigit(name[end - 1])) end--;
        var key = new System.Text.StringBuilder(end);
        for (int i = 0; i < end; i++) if (char.IsLetterOrDigit(name[i])) key.Append(char.ToLowerInvariant(name[i]));
        return key.ToString();
    }

    public static Transform OriginalParent(Transform t)
    {
        Ensure();
        return t != null && identities.TryGetValue(t, out var entry) ? entry.originalParent : t?.parent;
    }

    public static Transform OriginalRoot(Transform t)
    {
        if (t == null) return null;
        for (var p = OriginalParent(t); p != null; p = OriginalParent(t)) t = p;
        return t;
    }

    public static bool WasChildOf(Transform t, Transform ancestor)
    {
        for (; t != null; t = OriginalParent(t)) if (t == ancestor) return true;
        return false;
    }

    public static SceneObjectNames ForScene(Scene scene)
    {
        foreach (var map in UnityEngine.Object.FindObjectsByType<SceneObjectNames>(FindObjectsInactive.Include))
            if (map.gameObject.scene == scene) return map;
        return null;
    }
}
