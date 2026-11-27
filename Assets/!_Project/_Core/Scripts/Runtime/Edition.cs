using System;
using UnityEngine;

// Player editions are compile-time stamps, never command-line switches a player can override.
// Unstamped editor sessions deliberately retain Testing's Karen and diagnostics.
public static class Edition
{
    public static KehaiEdition Current
    {
        get
        {
#if UNITY_EDITOR
            return EditorOverride ?? KehaiEdition.Testing;
#elif KEHAI_TRAINING
            return KehaiEdition.Training;
#elif KEHAI_TESTING
            return KehaiEdition.Testing;
#else
            return KehaiEdition.Gameplay;
#endif
        }
    }
#if UNITY_EDITOR
    public static KehaiEdition? EditorOverride { get; set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    // An editor batch run may play as another edition (0.5.2, C6.6), to compare it with a built
    // player: -kehai-edition training|testing|gameplay. Editor only; players keep their stamp.
    static void Reset() => EditorOverride = EditionRules.FromCommandLine(Environment.GetCommandLineArgs());
#endif
    public static bool Training => Current == KehaiEdition.Training;
    public static bool Testing => Current == KehaiEdition.Testing;
    public static bool Gameplay => Current == KehaiEdition.Gameplay;
    public static bool ShowUI => EditionRules.ShowUI(Current);
    public static bool AllowDebugTools => EditionRules.AllowDebugTools(Current);
    public static bool AllowEvaluation => EditionRules.AllowEvaluation(Current);
    public static bool AllowGuide => !Training && !Application.isBatchMode;
    public static bool RecordReplays => EditionRules.RecordReplays(Current);
    public static bool RecordShiftData => EditionRules.RecordShiftData(Current);
    public static bool ShouldKarenAppear(int shift) => EditionRules.KarenAppears(Current, shift, CareerRuntime.GameplaySimulation);
    public static bool KarenAppears(KehaiEdition edition, int shift) => EditionRules.KarenAppears(edition, shift);
    public static bool TryParse(string value, out KehaiEdition edition) => EditionRules.TryParse(value, out edition);
    public static string Define(KehaiEdition edition) => EditionRules.Define(edition);
    public static string VersionLabel(string version, KehaiEdition edition) => EditionRules.VersionLabel(version, edition);
}
