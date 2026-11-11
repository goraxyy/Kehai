using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Eval
{
    // Shadow filters (0.4.0, build plan §8.3 and §11, C4.1): copies of her belief fed the same
    // evidence, side by side in the same shift, each with its own predict step (p0–p3) or its own
    // sense numbers (s0–s2), and none of them with the Director's hint. They change nothing she
    // does; the eval scores each against where the player really was (this is Eval: it may know).
    //
    //   -belief-shadows p0,p1,p2,p3,s1,s2
    //
    // Also run, whenever shadows are: the mirror map's three arms (K0, K1, K2), scored against what
    // the player could really sense of her (PlayerSenses, C1.6). And at each moment she loses sight
    // of the player: is the player's real destination in each destination arm's top 3, and what
    // chance does each give the true cell 5, 10 and 20 s later?
    public sealed class BeliefShadows
    {
        public sealed class Arm
        {
            public string Id;
            public BeliefGrid Grid;
            public SenseModel Senses;
            public readonly BeliefAccuracy.Tally All = new BeliefAccuracy.Tally(), OutOfView = new BeliefAccuracy.Tally();
            public readonly BeliefAccuracy.Tally After5 = new BeliefAccuracy.Tally(), After10 = new BeliefAccuracy.Tally(), After20 = new BeliefAccuracy.Tally();
            public int LossChecks, DestinationInTop3, DestinationTop1;
            public double Ms;
            public int Ticks;
        }

        public sealed class MirrorArm
        {
            public MirrorMode Mode;
            public MirrorBelief Map;
            public int Samples, Right;
        }

        public static string[] Requested;      // from -belief-shadows
        public readonly List<Arm> Arms = new List<Arm>();
        public readonly List<MirrorArm> Mirrors = new List<MirrorArm>();

        KarenBrain brain;
        float sampleTimer, mirrorTimer;
        readonly List<(float at, Vector3 lostAt)> pending = new List<(float, Vector3)>();
        bool wasSeeing;
        Vector3 playerSensedHerAt;
        bool playerSensedHer;

        public static bool On => Requested != null && Requested.Length > 0;

        public void Attach(KarenBrain b)
        {
            Detach();
            brain = b;
            if (b == null || !On) return;
            KarenBrain.FeaturesWanted = Requested.Any(r => r.StartsWith("p2") || r.StartsWith("p3"));
            foreach (string id in Requested)
            {
                bool sense = id.StartsWith("s");
                var grid = new BeliefGrid(b.Map);
                var arm = new Arm { Id = id, Grid = grid, Senses = SenseModel.For(sense ? id : b.config.senses) };
                grid.Motion = sense ? HandMotion.Today : MotionModels.Create(id, b.Features) ?? HandMotion.Today;
                Arms.Add(arm);
            }
            foreach (MirrorMode m in new[] { MirrorMode.K0, MirrorMode.K1, MirrorMode.K2 })
                Mirrors.Add(new MirrorArm { Mode = m, Map = new MirrorBelief(b.Map, m) });
            b.Belief.Predicted += OnPredicted;
            b.Belief.Swept += OnSwept;
            b.Belief.WasResetTo += OnReset;
            b.Sensed += OnSensed;
            NoiseBus.Emitted += OnNoise;
            SyncFromBrain(resetMass: true);
        }

        public void Detach()
        {
            if (brain != null)
            {
                if (brain.Belief != null)
                {
                    brain.Belief.Predicted -= OnPredicted;
                    brain.Belief.Swept -= OnSwept;
                    brain.Belief.WasResetTo -= OnReset;
                }
                brain.Sensed -= OnSensed;
            }
            NoiseBus.Emitted -= OnNoise;
            brain = null;
        }

        // The layers that shape prediction are hers: copied in before every step.
        void SyncFromBrain(bool resetMass)
        {
            foreach (Arm a in Arms)
            {
                System.Array.Copy(brain.Belief.DesireDistance, a.Grid.DesireDistance, a.Grid.DesireDistance.Length);
                System.Array.Copy(brain.Belief.HabitPrior, a.Grid.HabitPrior, a.Grid.HabitPrior.Length);
                if (a.Grid.Motion is HandMotion h && h.Fitted.HasValue && brain.Features != null) brain.Features.WriteDanger(a.Grid.Danger);
                if (resetMass) a.Grid.ResetToPrior();
            }
        }

        void OnReset(Vector3 at, float sigma)
        {
            SyncFromBrain(resetMass: false);
            foreach (Arm a in Arms)
            {
                if (sigma <= 0f) a.Grid.ResetToPrior();
                else a.Grid.ResetTo(at, sigma);
            }
            foreach (MirrorArm m in Mirrors) m.Map.Reset(brain.Body.Position, Time.time);
        }

        // The same evidence, made into a bump by each arm's own sense numbers.
        void OnSensed(Observation o, SenseRaw raw)
        {
            foreach (Arm a in Arms) a.Grid.Apply(Remake(o, raw, a.Senses));
        }

        public static Observation Remake(Observation o, SenseRaw raw, SenseModel s)
        {
            float confidence = o.Confidence, sigma = o.Sigma;
            switch (raw.Channel)
            {
                case SenseChannel.Sight: sigma = s.SightSigma(raw.Awareness); break;
                case SenseChannel.Hearing:
                    if (raw.Carry > 0f) { confidence = s.HearingConfidence(raw.D, raw.Carry); sigma = s.HearingSigma(raw.D, raw.Straight, raw.LightsOut); }
                    break;
                case SenseChannel.Trace: s.Trace((TraceKind)raw.Kind, raw.Freshness, raw.Age, out confidence, out sigma); break;
                case SenseChannel.Testimony: sigma = s.TestimonySigma(raw.Age); break;
            }
            return new Observation(o.Channel, o.Position, confidence, sigma, o.Label, o.Heading, o.IsNegative);
        }

        void OnSwept(List<int> cells, List<float> p)
        {
            foreach (Arm a in Arms)
            {
                // The detection chance scales with the arm's own sweep number.
                var scaled = p;
                if (!Mathf.Approximately(a.Senses.SweepScale, 0.85f)) scaled = p.Select(x => Mathf.Clamp01(x / 0.85f * a.Senses.SweepScale)).ToList();
                a.Grid.ApplySweep(cells, scaled);
            }
        }

        void OnPredicted(float dt, float speed)
        {
            SyncFromBrain(resetMass: false);
            foreach (Arm a in Arms)
            {
                long t0 = Stopwatch.GetTimestamp();
                a.Grid.Predict(dt, speed);
                a.Ms += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                a.Ticks++;
            }
            foreach (MirrorArm m in Mirrors)
            {
                if (brain.Body.Sight.Awareness >= brain.Body.Sight.seeAt)
                    m.Map.SawEachOther(brain.Body.Position, Vector3.Distance(brain.Body.Position, brain.Body.Sight.LastSeenPosition), Time.time);
                m.Map.Predict(dt);
            }
        }

        void OnNoise(NoiseEvent e)
        {
            if (brain == null || (e.Author != NoiseAuthor.Karen && e.Author != NoiseAuthor.Director) || e.Kind == NoiseKind.KarenStep) return;
            foreach (MirrorArm m in Mirrors) m.Map.MadeNoise(e.Position, e.Carry, brain.Belief, Time.time, e.Kind == NoiseKind.Tell);
        }

        // At the eval's rate, against the truth.
        public void Tick(float dt, Vector3 player, Vector3? destination)
        {
            if (brain == null || Arms.Count + Mirrors.Count == 0) return;
            StoreMap map = brain.Map;
            int truth = map.CellAt(player);
            bool seeing = brain.Body.Sight.Awareness >= brain.Body.Sight.seeAt;

            sampleTimer += dt;
            if (sampleTimer >= 1f / BeliefAccuracy.SampleHz && truth >= 0)
            {
                sampleTimer = 0f;
                foreach (Arm a in Arms)
                {
                    BeliefScore.Result r = BeliefScore.Score(a.Grid.Snapshot(), map.CellPosition, truth);
                    a.All.Add(r);
                    if (!seeing) a.OutOfView.Add(r);
                }
            }

            // The moment she loses sight: destinations, then the true cell 5, 10, 20 s later.
            if (wasSeeing && !seeing)
            {
                pending.Add((Time.time, player));
                if (destination.HasValue && brain.Features != null && brain.Features.Count > 0)
                    foreach (Arm a in Arms)
                    {
                        if (!(a.Grid.Motion is DestinationMotion)) continue;
                        var top = brain.Features.Top(a.Grid, 3);
                        int at = top.FindIndex(t => t.place.Cell >= 0 && (t.place.Position - destination.Value).sqrMagnitude < 9f);
                        a.LossChecks++;
                        if (at >= 0) a.DestinationInTop3++;
                        if (at == 0) a.DestinationTop1++;
                    }
            }
            wasSeeing = seeing;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                float since = Time.time - pending[i].at;
                if (truth < 0) continue;
                foreach (Arm a in Arms)
                {
                    if (since >= 5f && since - dt < 5f) a.After5.Add(BeliefScore.Score(a.Grid.Snapshot(), map.CellPosition, truth));
                    if (since >= 10f && since - dt < 10f) a.After10.Add(BeliefScore.Score(a.Grid.Snapshot(), map.CellPosition, truth));
                    if (since >= 20f && since - dt < 20f) a.After20.Add(BeliefScore.Score(a.Grid.Snapshot(), map.CellPosition, truth));
                }
                if (since > 20.5f) pending.RemoveAt(i);
            }

            // The mirror map's primary (§11): every 2 s while the player can't sense her, is the area
            // she thinks you'd name for her the area where you last sensed her?
            PlayerSenses.Sense(PlayerPresence.Current, brain, out bool saw, out bool heard);
            if (saw || heard) { playerSensedHer = true; playerSensedHerAt = brain.Body.Position; }
            mirrorTimer += dt;
            if (mirrorTimer >= 2f)
            {
                mirrorTimer = 0f;
                if (!saw && !heard && playerSensedHer)
                {
                    int answer = map.RegionAt(playerSensedHerAt);
                    foreach (MirrorArm m in Mirrors)
                    {
                        m.Samples++;
                        if (m.Map.ThoughtRegion(brain.Body.Position) == answer) m.Right++;
                    }
                }
            }
        }

        public void AddTo(Dictionary<string, object> d)
        {
            foreach (Arm a in Arms)
            {
                string k = "shadow_" + a.Id + "_";
                d[k + "logp"] = a.All.LogP;
                d[k + "logp_out"] = a.OutOfView.LogP;
                d[k + "samples_out"] = a.OutOfView.Samples;
                d[k + "logp_5s"] = a.After5.LogP;
                d[k + "logp_10s"] = a.After10.LogP;
                d[k + "logp_20s"] = a.After20.LogP;
                d[k + "loss_checks"] = a.LossChecks;
                d[k + "dest_top3"] = a.DestinationInTop3;
                d[k + "dest_top1"] = a.DestinationTop1;
                d[k + "ms_per_tick"] = a.Ticks > 0 ? a.Ms / a.Ticks : 0.0;
            }
            foreach (MirrorArm m in Mirrors)
            {
                d["mirror_" + m.Mode.ToString().ToLowerInvariant() + "_samples"] = m.Samples;
                d["mirror_" + m.Mode.ToString().ToLowerInvariant() + "_right"] = m.Right;
            }
        }
    }
}
