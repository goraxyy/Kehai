using System.Collections.Generic;
using System.Diagnostics;
using Kehai.Karen;

namespace Kehai.Eval
{
    // Belief filters that run beside Karen's own, fed exactly the evidence her grid gets —
    // each sighting, noise, trace, testimony, sweep, reset — but each predicting with its own
    // motion model (Belief/MotionModel.cs). She acts on her own grid only; these just watch.
    //
    // They never get the Director's hint (KarenDirector.MaybeBias), which nudges her grid
    // toward where the player really is once she's lost them for 45 s. So a shadow measures
    // the filter alone, and two shadows differ only in how they think a person moves: the
    // comparison has no behaviour confound, because she did the same thing for all of them.
    //
    // Used by the ablation runner (-belief-shadows m0,m1,…) and by playtest builds, which run
    // one shadow and add both scores to the session (ShiftRecorder).
    public sealed class BeliefShadows : IBeliefTap
    {
        public static BeliefShadows Current { get; private set; }

        public readonly List<string> Ids = new List<string>();
        public readonly List<BeliefGrid> Grids = new List<BeliefGrid>();
        public readonly List<double> EvidenceMilliseconds = new List<double>();
        public readonly List<double> PredictMilliseconds = new List<double>();
        public int Ticks { get; private set; }

        readonly KarenBrain brain;           // null when following a bare grid (tests)
        readonly MotionFeatures features;
        BeliefGrid primary;
        readonly Stopwatch watch = new Stopwatch();

        BeliefShadows(KarenBrain brain, MotionFeatures features, IEnumerable<string> ids)
        {
            this.brain = brain;
            this.features = features;
            foreach (string id in ids)
            {
                string canonical = MotionModels.Canonical(id);
                if (canonical.Length == 0 || Ids.Contains(canonical)) continue;
                if (MotionModels.Create(canonical, features) == null)
                {
                    UnityEngine.Debug.LogWarning($"Belief shadows: no motion model \"{id}\", left out");
                    continue;
                }
                Ids.Add(canonical);
                EvidenceMilliseconds.Add(0.0);
                PredictMilliseconds.Add(0.0);
            }
        }

        // Starts shadows beside `brain`'s grid; they follow it through map rebuilds.
        public static BeliefShadows Attach(KarenBrain brain, IEnumerable<string> ids)
        {
            if (brain == null) return null;
            var shadows = new BeliefShadows(brain, brain.MotionInputs, ids);
            if (shadows.Ids.Count == 0) return null;
            brain.BeliefRebuilt += shadows.Rebuild;
            shadows.Rebuild();
            Current = shadows;
            return shadows;
        }

        // Shadows beside a bare grid, with no brain behind it (tests).
        public static BeliefShadows Beside(BeliefGrid grid, MotionFeatures features, IEnumerable<string> ids)
        {
            var shadows = new BeliefShadows(null, features, ids);
            shadows.Follow(grid);
            return shadows;
        }

        public void Detach()
        {
            if (brain != null) brain.BeliefRebuilt -= Rebuild;
            primary?.Taps.Remove(this);
            if (Current == this) Current = null;
        }

        public bool Follows(KarenBrain b) => b == brain && primary == b.Belief;

        void Rebuild() => Follow(brain.Belief);

        // A new grid each (the map changed, or they've just started), starting from hers.
        void Follow(BeliefGrid grid0)
        {
            primary?.Taps.Remove(this);
            primary = grid0;
            Grids.Clear();
            if (primary == null) return;
            foreach (string id in Ids)
            {
                var grid = new BeliefGrid(primary.Map, MotionModels.Create(id, features))
                {
                    // The same knowledge of the store as hers: the same layers, by reference.
                    DesireDistance = primary.DesireDistance,
                    HabitPrior = primary.HabitPrior,
                    Floor = primary.Floor,
                    HabitGain = primary.HabitGain
                };
                grid.CopyFrom(primary);
                Grids.Add(grid);
            }
            primary.Taps.Add(this);
        }

        public BeliefGrid Grid(string id)
        {
            int i = Ids.IndexOf(MotionModels.Canonical(id));
            return i >= 0 && i < Grids.Count ? Grids[i] : null;
        }

        // ---- the same evidence, each in turn ----

        public void OnEvidence(BeliefGrid grid, Likelihood likelihood, bool collapsed)
        {
            for (int i = 0; i < Grids.Count; i++)
            {
                watch.Restart();
                Grids[i].Correct(likelihood);
                EvidenceMilliseconds[i] += watch.Elapsed.TotalMilliseconds;
            }
        }

        public void OnBias(BeliefGrid grid, int region, float amount) { }   // the point of a shadow

        public void OnPredict(BeliefGrid grid, float dt, float speed)
        {
            Ticks++;
            for (int i = 0; i < Grids.Count; i++)
            {
                watch.Restart();
                Grids[i].Predict(dt, speed);
                PredictMilliseconds[i] += watch.Elapsed.TotalMilliseconds;
            }
        }

        public void OnReset(BeliefGrid grid)
        {
            for (int i = 0; i < Grids.Count; i++) Grids[i].CopyFrom(grid);
        }
    }
}
