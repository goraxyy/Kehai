public enum KehaiEdition { Training, Testing, Gameplay }

public static class EditionRules
{
    public static bool ShowUI(KehaiEdition edition) => edition != KehaiEdition.Training;
    public static bool AllowDebugTools(KehaiEdition edition) => edition == KehaiEdition.Testing;
    public static bool AllowEvaluation(KehaiEdition edition) => edition != KehaiEdition.Gameplay;
    public static bool RecordReplays(KehaiEdition edition) => edition == KehaiEdition.Testing;
    // Gameplay has no data-consent flow yet; it writes no play data by default.
    public static bool RecordShiftData(KehaiEdition edition) => edition != KehaiEdition.Gameplay;
    public static bool KarenAppears(KehaiEdition edition, int shift, bool gameplaySimulation = false) =>
        (edition == KehaiEdition.Gameplay || gameplaySimulation) ? shift > 1 : true;
    public static bool TryParse(string value, out KehaiEdition edition)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "training": edition = KehaiEdition.Training; return true;
            case "testing": edition = KehaiEdition.Testing; return true;
            case "gameplay": edition = KehaiEdition.Gameplay; return true;
            default: edition = KehaiEdition.Gameplay; return false;
        }
    }
    public static string Define(KehaiEdition edition) => "KEHAI_" + edition.ToString().ToUpperInvariant();
    public static string VersionLabel(string version, KehaiEdition edition) =>
        version + (edition == KehaiEdition.Gameplay ? "" : "-" + edition.ToString().ToLowerInvariant());
}
