using System.IO;
using Kehai.Store;
using UnityEditor;
using UnityEngine;

// Writes the store map out for people and agents to read. The map itself is rebuilt from
// the scene every time the game runs; this only refreshes the readable copy in the repo.
public static class StoreMapMenu
{
    const string MarkdownPath = "docs/design/STORE_MAP.md";

    [MenuItem("Kehai/Map/Export STORE_MAP.md")]
    public static void ExportMarkdown()
    {
        string summary = Export();
        Debug.Log($"Wrote {MarkdownPath}: {summary}");
    }

    [MenuItem("Kehai/Map/Log Map Summary")]
    public static void LogSummary()
    {
        StoreMap map = StoreMap.Rebuild();
        Debug.Log(map.Summary() + "\n\n" + map.ToAscii());
    }

    // Returns the map summary, so a caller (or a test) can check what was written.
    public static string Export()
    {
        StoreMap map = StoreMap.Rebuild();
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        File.WriteAllText(Path.Combine(projectRoot, MarkdownPath), StoreMapReport.ToMarkdown(map));
        return map.Summary();
    }
}
