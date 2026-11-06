using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Eval
{
    // The 0.2.0 build plan's measurements (§5), taken from the truth: the harness may know
    // where the player is; her decisions may not. Ticked every frame by EpisodeMetrics, like
    // BeliefAccuracy.
    //
    //   gap after loss   metres from her to you 5 s after she loses sight of you in a hunt (primary 2)
    //   hunt, sprint     seconds hunting, and seconds her body moves faster than a hurry
    //   outside          seconds her body stands outside her part of the building (must be 0)
    //   far sightings    "seen" from beyond 18 m, which couldn't happen before 0.2.0
    //   blind catches    catches where you hadn't seen her in the 5 s before
    //   throws, cuts     throws and hits, catches within 5 s of a throw, and Cuts whose spot you
    //                    came within 3 m of before its window closed
    //
    // The ablation's `main` build compiles this file without the parts marked 0.2.0 only.
    public sealed class HuntMetrics
    {
        const float After = 5f, Far = 18f, CutHit = 3f;

        public readonly List<float> GapAfterLoss = new List<float>();
        public float HuntSeconds, SprintSeconds, OutsideSeconds;
        public int FarSightings, BlindCatches;
        public int Throws, ThrowHits, CatchesAfterThrow, Cuts, CutsRight;

        readonly List<float> due = new List<float>();
        readonly List<(Vector3 at, float until)> cuts = new List<(Vector3, float)>();
        bool seeing;
        int catches;
        float sawHerAt = -99f, threwAt = -99f;
        Vector3 lastHer;

        public void Begin()
        {
            GapAfterLoss.Clear();
            HuntSeconds = SprintSeconds = OutsideSeconds = 0f;
            FarSightings = BlindCatches = Throws = ThrowHits = CatchesAfterThrow = Cuts = CutsRight = 0;
            due.Clear();
            cuts.Clear();
            seeing = false;
            sawHerAt = threwAt = -99f;
            KarenBrain brain = KarenBrain.Instance;
            catches = brain != null ? brain.Stats.Catches : 0;
            lastHer = brain != null && brain.Body != null ? brain.Body.Position : Vector3.zero;
            // -- 0.2.0 only --
            ThrowEvents.Thrown += OnThrown;
            ThrowEvents.Landed += OnLanded;
            CutTactic.Aimed += OnCut;
            // -- end --
        }

        public void End()
        {
            // -- 0.2.0 only --
            ThrowEvents.Thrown -= OnThrown;
            ThrowEvents.Landed -= OnLanded;
            CutTactic.Aimed -= OnCut;
            // -- end --
        }

        // -- 0.2.0 only --
        void OnThrown(ThrowEvent e)
        {
            Throws++;
            threwAt = Time.time;
        }

        void OnLanded(ThrowEvent e)
        {
            if (e.Hit) ThrowHits++;
        }

        void OnCut(Vector3 spot, float until)
        {
            Cuts++;
            cuts.Add((spot, until));
        }
        // -- end --

        public void Tick(float dt, Vector3 player)
        {
            KarenBrain brain = KarenBrain.Instance;
            if (brain == null || brain.Body == null || dt <= 0f) return;
            Vector3 her = brain.Body.Position;
            SightSensor sight = brain.Body.Sight;

            bool sees = sight.Awareness >= sight.seeAt;
            if (sees && !seeing && Flat(her - player) > Far) FarSightings++;
            if (!sees && seeing && brain.IsChasing) due.Add(Time.time + After);
            seeing = sees;
            for (int i = due.Count - 1; i >= 0; i--)
            {
                if (Time.time < due[i]) continue;
                GapAfterLoss.Add(Flat(her - player));
                due.RemoveAt(i);
            }

            if (brain.IsChasing) HuntSeconds += dt;
            if (Flat(her - lastHer) / dt > brain.config.hurrySpeed + 0.5f) SprintSeconds += dt;
            lastHer = her;
            if (!Hers(her)) OutsideSeconds += dt;

            if (EnvWorld.CanSee(PlayerPresence.Current, brain, out _)) sawHerAt = Time.time;
            if (brain.Stats.Catches > catches)
            {
                catches = brain.Stats.Catches;
                if (Time.time - sawHerAt > After) BlindCatches++;
                if (Time.time - threwAt <= After) CatchesAfterThrow++;
            }

            for (int i = cuts.Count - 1; i >= 0; i--)
            {
                if (Flat(cuts[i].at - player) <= CutHit) { CutsRight++; cuts.RemoveAt(i); }
                else if (Time.time > cuts[i].until) cuts.RemoveAt(i);
            }
        }

        // Her part of the building (0.2.0): the store, the backstreet and the pavement, by the
        // same names on both builds.
        static bool Hers(Vector3 p) => !NotHers.Contains(StoreMap.AreaAt(p));

        static readonly HashSet<string> NotHers = new HashSet<string> { "Street", "West alley", "East alley", "South yard" };

        static float Flat(Vector3 d)
        {
            d.y = 0f;
            return d.magnitude;
        }

        public void AddTo(Dictionary<string, object> d)
        {
            d["gap_after_loss_m"] = GapAfterLoss.Count > 0 ? GapAfterLoss.Average() : -1f;
            d["gap_after_loss_n"] = GapAfterLoss.Count;
            d["hunt_s"] = HuntSeconds;
            d["sprint_s"] = SprintSeconds;
            d["outside_s"] = OutsideSeconds;
            d["far_sightings"] = FarSightings;
            d["blind_catches"] = BlindCatches;
            d["throws"] = Throws;
            d["throw_hits"] = ThrowHits;
            d["catches_after_throw"] = CatchesAfterThrow;
            d["cuts"] = Cuts;
            d["cuts_right"] = CutsRight;
        }
    }
}
