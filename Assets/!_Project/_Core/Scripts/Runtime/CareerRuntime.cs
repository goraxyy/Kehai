using System;
using System.IO;
using Kehai.Karen;
using UnityEngine;

public static class CareerRuntime
{
    public static int? PendingCompletedShifts { get; set; }
    public static int Seed { get; set; } = 1;
    public static bool GuideSkipped { get; private set; }
    // Evaluation-only schedule; parent sets this before reloading a non-learning benchmark.
    public static bool GameplaySimulation { get; set; }
    public static event Action<int, int, bool> CareerPreparing;
    public static event Action CareerCompleted;
    public static string SavePath => Path.Combine(Application.persistentDataPath, "career.json");
    static bool Persistent => Application.isPlaying && !Edition.Training && !Application.isBatchMode && KarenBootstrap.Override == null;
    [Serializable] sealed class SavedCareer { public int version = 1; public int completedShifts; public int seed = 1; public bool guideSkipped; }
    [Serializable] sealed class LegacyCareer { public int shiftsWorked = 0; public int shiftsClockedOut = 0; }

    public static int ReadCompletedShifts()
    {
        if (PendingCompletedShifts.HasValue) return Math.Max(0, PendingCompletedShifts.Value);
        if (!Persistent) return 0;
        try
        {
            if (File.Exists(SavePath))
            {
                var saved = JsonUtility.FromJson<SavedCareer>(File.ReadAllText(SavePath));
                if (saved != null && saved.version == 1)
                {
                    Seed = saved.seed;
                    GuideSkipped = saved.guideSkipped;
                    return Math.Max(0, saved.completedShifts);
                }
            }
            if (!File.Exists(KarenLedger.DefaultPath)) return 0;
            var legacy = JsonUtility.FromJson<LegacyCareer>(File.ReadAllText(KarenLedger.DefaultPath));
            return Math.Max(0, Math.Min(legacy?.shiftsWorked ?? 0, legacy?.shiftsClockedOut ?? 0));
        }
        catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
        { Debug.LogWarning("Could not read career placement: " + e.Message); return 0; }
    }
    public static int UpcomingShiftNumber => CareerRules.Upcoming(ReadCompletedShifts());
    public static void Prepare(int shift, int seed) => CareerPreparing?.Invoke(shift, seed, Edition.Training);
    public static void SaveProgress(int completed)
    {
        if (!Persistent) return;
        try
        {
            string temporary = SavePath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(new SavedCareer
                { completedShifts = completed, seed = Seed, guideSkipped = GuideSkipped }, true));
            if (File.Exists(SavePath)) File.Replace(temporary, SavePath, null);
            else File.Move(temporary, SavePath);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        { Debug.LogWarning("Could not save career progress: " + e.Message); }
    }
    public static void SkipGuide(int completed)
    {
        GuideSkipped = true;
        SaveProgress(completed);
    }
    public static void NewCareer()
    {
        GuideSkipped = false;
        PendingCompletedShifts = null;
        if (Persistent && File.Exists(SavePath)) File.Delete(SavePath);
    }
    public static void Complete()
    {
        KarenBrain brain = KarenBrain.Instance;
        if (Persistent && brain != null && brain.Ledger != null && brain.Ledger.Persistent)
        {
            brain.Ledger.Data.endingReached = true;
            brain.Ledger.Save();
        }
        CareerCompleted?.Invoke();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        PendingCompletedShifts = null;
        Seed = 1;
        GuideSkipped = false;
        GameplaySimulation = false;
        CareerPreparing = null;
        CareerCompleted = null;
    }
}
