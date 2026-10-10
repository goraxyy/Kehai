using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // How she gets about when she isn't doing something to you. Tier 0: no tension cost,
    // not bandit arms, always permitted.

    public sealed class PatrolTactic : Tactic
    {
        public override string Id => "patrol";
        public override string Title => "Patrol";
        public override GoalId[] Goals => new[] { GoalId.Patrol };
        public override int Tier => 0;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0f;
        public override float Cooldown => 0f;
        public override float PanicPrior => 0f;
        public override TellKind Tell => TellKind.Footsteps;
        public override string Chore => "none";
        public override string Attacks => "coverage";

        // Coverage: go where she hasn't looked for longest, weighted a little by belief —
        // patrol refreshes negative information as much as it hunts.
        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            Region best = null;
            float bestScore = float.MinValue;
            foreach (Region r in c.Map.Regions)
            {
                if (r.IsDoor || r.Cells.Count < 4 || StoreMap.IsOutdoors(r.Area)) continue;
                float since = c.Now - c.Brain.LastVisited(r.Id);
                float score = Mathf.Min(since, 240f) / 240f + c.Belief.RegionMass[r.Id] * 4f
                            - Vector3.Distance(r.Centroid, c.Body.Position) / 120f + c.Rng.Value * 0.2f;
                if (score > bestScore) { bestScore = score; best = r; }
            }
            if (best == null) return;
            plan.Target = best.Name;
            plan.Go(best.Centroid, KarenBody.Pace.Walk, "patrol to " + best.Name, 2f);
            plan.Add(new LookAround(2.5f));
        }
    }

    public sealed class InvestigateTactic : Tactic
    {
        public override string Id => "investigate";
        public override string Title => "Investigate";
        public override GoalId[] Goals => new[] { GoalId.Investigate };
        public override int Tier => 0;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0f;
        public override float Cooldown => 0f;
        public override float PanicPrior => 0.05f;
        public override TellKind Tell => TellKind.Footsteps;
        public override string Chore => "none";
        public override string Attacks => "you";
        public override float ExposureRisk(KarenContext c) => 0.2f;

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            int region = c.Belief.PeakRegion;
            plan.Target = c.RegionName(region);
            plan.Add(new MoveTo(k => k.Belief.RegionCentroid(region), KarenBody.Pace.Hurry, "investigate " + plan.Target, 1.6f)
                .When(k => k.Belief.RegionMass[region] > 0.02f, "belief still there"));
            plan.Add(new LookAround(3f));
        }
    }

    // Reduce entropy on purpose: visit the most likely regions in order of mass per metre,
    // checking this player's favourite hiding spots early rather than by distance (§7.5).
    public sealed class SweepTactic : Tactic
    {
        public override string Id => "sweep";
        public override string Title => "Sweep";
        public override GoalId[] Goals => new[] { GoalId.Sweep };
        public override int Tier => 0;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0f;
        public override float Cooldown => 0f;
        public override float PanicPrior => 0.03f;
        public override TellKind Tell => TellKind.Footsteps;
        public override string Chore => "none";
        public override string Attacks => "sight";

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            var hiding = c.Ledger.ConcealmentRegions(c, 3);
            var ranked = c.Map.Regions
                .Where(r => !r.IsDoor && r.Cells.Count >= 3)
                .Select(r => new
                {
                    r,
                    score = (c.Belief.RegionMass[r.Id] + (hiding.Contains(r.Id) ? 0.05f : 0f))
                            / (4f + Vector3.Distance(r.Centroid, c.Body.Position))
                })
                .OrderByDescending(x => x.score)
                .Take(3)
                .ToList();

            if (ranked.Count == 0) return;
            plan.Target = string.Join(", ", ranked.Select(x => x.r.Name));
            foreach (var x in ranked)
            {
                plan.Go(x.r.Centroid, KarenBody.Pace.Walk, "sweep " + x.r.Name, 2f);
                plan.Add(new LookAround(2f));
            }
        }
    }

    // Walk to a shopper who has seen the employee recently and "ask". Costs her time and
    // puts her in the open (§3.4) — the price of testimony.
    public sealed class PollWitnessesTactic : Tactic
    {
        public override string Id => "poll_witnesses";
        public override string Title => "Ask the customers";
        public override GoalId[] Goals => new[] { GoalId.Sweep, GoalId.Investigate };
        public override int Tier => 0;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0f;
        public override float Cooldown => 20f;
        public override float PanicPrior => 0.02f;
        public override TellKind Tell => TellKind.Footsteps;
        public override string Chore => "none";
        public override string Attacks => "social";
        public override float ExposureRisk(KarenContext c) => 0.25f;

        CustomerMemory witness;

        public override bool Available(KarenContext c, out string why)
        {
            witness = null;
            float best = 45f;
            foreach (CustomerMemory m in CustomerMemory.All)
            {
                if (m == null || !m.HasSighting || m.possessed) continue;
                float d = Vector3.Distance(m.transform.position, c.Body.Position);
                if (d < best) { best = d; witness = m; }
            }
            why = witness == null ? "no witness has seen the employee" : null;
            return witness != null;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            if (witness == null) return;
            CustomerMemory w = witness;
            plan.Target = w.name;
            plan.Add(new MoveTo(_ => w != null ? w.transform.position : c.Body.Position, KarenBody.Pace.Hurry, "ask " + w.name, 1.8f, 30f, follow: true));
            plan.Add(new Wait(1.5f, "listen"));
        }
    }

    // An antagonist that knows when to leave (§6.3). Walks to the far end of the store,
    // plays hold music over the PA, and patrols like it's shift one.
    public sealed class WithdrawTactic : Tactic
    {
        public override string Id => "withdraw";
        public override string Title => "Withdraw";
        public override GoalId[] Goals => new[] { GoalId.Withdraw };
        public override int Tier => 0;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0.04f;
        public override float Cooldown => 30f;
        public override float PanicPrior => -0.2f;
        public override TellKind Tell => TellKind.HoldMusic;
        public override string Chore => "none — the quiet is the point";
        public override string Attacks => "nothing";
        public override bool Learnable => true;

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            float[] far = TacticHelpers.DistanceFromBelief(c);
            Region best = null;
            float bestWalk = -1f;
            foreach (Region r in c.Map.Regions)
            {
                if (r.IsDoor || r.Cells.Count < 4 || StoreMap.IsOutdoors(r.Area)) continue;
                float d = far[r.Cells[0]];
                if (float.IsInfinity(d) || d <= bestWalk) continue;
                bestWalk = d;
                best = r;
            }
            if (best == null) return;

            plan.Target = best.Name;
            plan.Add(new Tell(TellKind.HoldMusic, k => k.Body.Position, 1f));
            plan.Go(best.Centroid, KarenBody.Pace.Walk, "withdraw to " + best.Name, 2.5f);
            plan.Add(new LookAround(6f, "idle, facing away"));
        }
    }
}
