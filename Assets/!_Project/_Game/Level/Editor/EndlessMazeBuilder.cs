using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Kehai/Endless/Open an Endless Maze: a scene of its own with the endless maze in it (IDEAS.md,
// "Scaling", step 3), a walker to explore it with, and the drawer for its stock. Saved next to
// the store's scene, which it doesn't touch. Press Play and walk.
public static class EndlessMazeBuilder
{
    public const string ScenePath = "Assets/!_Project/_Core/Scenes/EndlessMaze.unity";
    const string Ready = "Assets/!_Project/_Game/Level/Prefabs/Shelves/Ready/";
    const string Grid = "Assets/Scalable Grid Prototype Materials/Materials/";

    [MenuItem("Kehai/Endless/Open an Endless Maze")]
    public static void OpenMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log(Make());
    }

    public static string Make(int seed = 1)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Night outside, and the ceiling lights to see by.
        var moon = new GameObject("Moon").AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.intensity = 0.05f;
        moon.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.12f, 0.14f);

        // In the middle of a cell: walls are on the cells' edges, pillars at their corners.
        var player = new GameObject("Walker") { tag = "Player" };
        player.transform.position = new Vector3(2.5f * MazeGenerator.CellSize, 0.1f, 2.5f * MazeGenerator.CellSize);
        var body = player.AddComponent<CharacterController>();
        body.height = 1.8f;
        body.radius = 0.3f;
        body.center = new Vector3(0f, 0.9f, 0f);
        var eye = new GameObject("Camera") { tag = "MainCamera" };
        eye.transform.SetParent(player.transform, false);
        eye.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        eye.AddComponent<Camera>().nearClipPlane = 0.05f;
        eye.AddComponent<AudioListener>();
        player.AddComponent<EndlessWalker>().view = eye.transform;

        var maze = new GameObject("Endless Maze").AddComponent<EndlessMaze>();
        maze.seed = seed;
        maze.player = player.transform;
        maze.runPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Ready + "ShelfTwoside_3_grey_Ready.prefab");
        maze.pillarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Ready + "Shelfpillar_I_3_grey_Ready.prefab");
        maze.floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(Grid + "Light_Gray_Prototype.mat");
        maze.ceilingMaterial = AssetDatabase.LoadAssetAtPath<Material>(Grid + "Blue_Prototype.mat");

        ShelfDrawer.Ensure();
        EditorSceneManager.SaveScene(scene, ScenePath);

        string missing = (maze.runPrefab == null ? " the shelf run prefab" : "") +
                         (maze.pillarPrefab == null ? " the pillar prefab" : "") +
                         (maze.floorMaterial == null ? " the floor material" : "") +
                         (maze.ceilingMaterial == null ? " the ceiling material" : "");
        Bounds run = maze.runPrefab != null ? EndlessMaze.Footprint(maze.runPrefab) : default;
        Bounds pillar = maze.pillarPrefab != null ? EndlessMaze.Footprint(maze.pillarPrefab) : default;
        return $"Endless maze (seed {seed}) saved as {ScenePath}. Shelf run {run.size:F2} at {run.center:F2}, " +
               $"pillar {pillar.size:F2} at {pillar.center:F2}." +
               (missing.Length > 0 ? " Not found:" + missing + "." : " Press Play and walk.");
    }
}
