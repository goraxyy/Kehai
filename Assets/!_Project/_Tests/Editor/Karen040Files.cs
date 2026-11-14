using System.Collections.Generic;
using System.IO;

// The 0.4.0 files outside Belief/ and Decision/ that the "she never reads the player" scan must
// cover (KarenTests.BodyBeliefAndDecisions_NeverReadThePlayer). Belief/ and Decision/ are scanned
// whole, so their new files are covered without being listed.
public static class Karen040Files
{
    public static readonly string[] Listed =
    {
        "Diagnostics/LearningRecord.cs",
        "Perception/SenseModel.cs",
    };

    public static IEnumerable<string> UnderTheRule(string aiRoot)
    {
        foreach (string f in Listed)
        {
            string path = Path.Combine(aiRoot, f);
            if (File.Exists(path)) yield return path;
        }
    }
}
