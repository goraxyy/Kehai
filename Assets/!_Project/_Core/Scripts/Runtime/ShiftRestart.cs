using UnityEngine;
using UnityEngine.SceneManagement;

// Esc → Restart this shift. The store is loaded again from scratch (shelves, spills,
// customers, Karen, and you back where you start), and the shift you were on begins
// again when you clock in; between shifts, the next one does. What Karen has learnt about
// you over earlier shifts stays: it lives in her ledger, not in the scene.
public static class ShiftRestart
{
    static int completed = -1;

    public static void Restart() => Reload(CompletedShifts());

    // How many shifts count as done: the one under way doesn't.
    public static int CompletedShifts()
    {
        ShiftManager shift = Object.FindAnyObjectByType<ShiftManager>();
        return shift == null ? -1 : shift.IsShiftActive ? shift.ShiftNumber - 1 : shift.ShiftNumber;
    }

    // Loads the store again (or `scenePath`, if given), with `completedShifts` done once it's
    // up; -1 leaves the shift count as the scene has it. The 3D replay comes and goes this way.
    public static void Reload(int completedShifts, string scenePath = null)
    {
        completed = completedShifts;
        GamePause.Set(false);

        SceneManager.sceneLoaded -= Restore;
        SceneManager.sceneLoaded += Restore;
        Scene active = SceneManager.GetActiveScene();
        string path = string.IsNullOrEmpty(scenePath) ? active.path : scenePath;
        int index = path == active.path ? active.buildIndex : SceneUtility.GetBuildIndexByScenePath(path);
#if UNITY_EDITOR
        // A scene that isn't in the build list can still be reloaded in the editor, by path.
        if (index < 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif
        SceneManager.LoadScene(index >= 0 ? index : active.buildIndex, LoadSceneMode.Single);
    }

    static void Restore(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= Restore;
        if (completed < 0) return;
        ShiftManager shift = Object.FindAnyObjectByType<ShiftManager>();
        if (shift != null) shift.SetShiftNumber(completed);
        completed = -1;
    }
}
