using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The owner's three editor steps from 0.5.1 and 0.4.1, in one batch run on the owner's own project
// (0.5.2; the owner asked for them to be done, 26 November):
//   Kehai → Cold Storage → Move Existing Islands Closer
//   Kehai → Store Finish → Apply Door, Locker and Exterior Fit
//   Kehai → Props → Import Karen's Props
// The store scene is copied to Library/Kehai/SceneBackups first (the fit step saves its own
// backup too), opened, changed and saved. Run with the editor closed:
//   Unity -batchmode -nographics -projectPath ~/Developer/Kehai -executeMethod OwnerSteps.Run -logFile <log>
public static class OwnerSteps
{
    public const string Scene = "Assets/!_Project/_Core/Scenes/SampleScene.unity";

    public static void Run()
    {
        int code = 0;
        try
        {
            Directory.CreateDirectory("Library/Kehai/SceneBackups");
            string backup = "Library/Kehai/SceneBackups/Before-owner-steps-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
            File.Copy(Scene, backup);
            Debug.Log($"OwnerSteps: the scene is backed up to {backup}");

            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            bool moved = ColdStoreBuilder.MoveIslandsCloser();
            if (moved) StaffRoomBuilder.BakeNavigation();
            Debug.Log($"OwnerSteps: the cold room's islands {(moved ? "moved closer" : "were not found; nothing moved")}");
            EnvironmentFitBuilder.Apply();
            Debug.Log("OwnerSteps: the door, locker and exterior fit applied");
            if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()))
                throw new InvalidOperationException("the store scene could not be saved");
            Debug.Log("OwnerSteps: the store scene is saved");

            Debug.Log("OwnerSteps: " + PropImport.ImportAll());
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogError("OwnerSteps failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }
}
