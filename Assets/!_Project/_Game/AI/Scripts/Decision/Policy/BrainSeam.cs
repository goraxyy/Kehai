using System.Collections.Generic;
using System.Linq;

namespace Kehai.Karen
{
    // The seam between the planner and a trained brain (0.4.0, build plan §5.2, C5.1). Filled in by C5.1.
    public static class BrainSeam
    {
        // Every (action, trick) pair the library can run, by name: "flush:blackout".
        public static IEnumerable<string> AllPairs =>
            TacticLibrary.All.SelectMany(t => t.Actions.Select(a => KarenBrain.PickKey(a, t)));

        public static bool KnownOutput(string name) => AllPairs.Contains(name);
    }
}
