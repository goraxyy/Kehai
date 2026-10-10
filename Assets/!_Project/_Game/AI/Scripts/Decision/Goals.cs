using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Kehai.Karen
{
    // Goal selection by utility, not by state (Karen.md §6.3). Every goal is scored every
    // decision; the best one wins, with hysteresis so she doesn't flap between two.
    //
    //   U(g) = w_conf·Fit(g) + w_stress·ExpectedPanicDelta(g)·sign(pressure)
    //        + w_task·TaskLeverage(g) + w_novel·Novelty(g)
    //        − w_cost·TensionCost(g)/Tension − w_risk·ExposureRisk(g)
    //        × Permission(g)
    public sealed class GoalSet
    {
        public GoalId Current { get; private set; } = GoalId.Patrol;
        public float CommittedAt { get; private set; } = -999f;
        public readonly List<ThoughtOption> LastOptions = new List<ThoughtOption>();

        readonly Dictionary<GoalId, float> lastChosen = new Dictionary<GoalId, float>();
        static readonly GoalId[] goals = (GoalId[])System.Enum.GetValues(typeof(GoalId));

        public void Reset()
        {
            Current = GoalId.Patrol;
            CommittedAt = -999f;
            lastChosen.Clear();
        }

        // Picks the goal to pursue now. `forced` bypasses the minimum commit time — used
        // when an interrupt has fired or the current plan has ended.
        public GoalId Choose(KarenContext c, bool forced, out string because)
        {
            LastOptions.Clear();
            var scores = new Dictionary<GoalId, float>();
            var whys = new Dictionary<GoalId, string>();

            foreach (GoalId g in goals)
            {
                float u = Score(g, c, out string why);
                scores[g] = u;
                whys[g] = why;
            }

            // Stay with the current goal unless something beats it clearly.
            GoalId best = Current;
            float bestScore = scores[Current] + c.Config.hysteresis;
            bool mayLeave = forced || c.Now - CommittedAt >= c.Config.minCommitSeconds;

            foreach (GoalId g in goals)
            {
                if (g == Current) continue;
                if (scores[g] > bestScore && (mayLeave || g == GoalId.Pursue))
                {
                    best = g;
                    bestScore = scores[g];
                }
            }

            foreach (GoalId g in goals.OrderByDescending(g => scores[g]).Take(4))
                LastOptions.Add(new ThoughtOption { Name = g.ToString(), Utility = scores[g], Why = whys[g] });

            because = whys[best];
            if (best != Current || forced)
            {
                Current = best;
                CommittedAt = c.Now;
                lastChosen[best] = c.Now;
            }
            return best;
        }

        float Score(GoalId g, KarenContext c, out string why)
        {
            Appraisal a = c.A;
            KarenConfig k = c.Config;

            // Which tactics could serve this goal right now at all?
            var available = HtnPlanner.Candidates(g, c, null);
            if (available.Count == 0)
            {
                why = "no tactic available";
                return 0f;
            }

            float fit = Fit(g, c, out string fitWhy);
            if (fit <= 0f)
            {
                why = fitWhy;
                return 0f;
            }

            // Expected panic delta: what this goal's tactics have done to *this* player,
            // pushed the way the Director wants panic to move.
            float epd = available.Average(t => c.Ledger.ExpectedPanicDelta(t, c));
            float stress = epd * Mathf.Clamp(a.Pressure, -1f, 1f);

            float task = g == GoalId.Deny ? a.TaskLoad : 0f;

            float novelty = 1f;
            if (lastChosen.TryGetValue(g, out float when))
                novelty = Mathf.Clamp01((c.Now - when) / 60f);
            if (!Tactical(g)) novelty = 0.5f;

            float cheapest = available.Min(t => t.TensionCost);
            float cost = cheapest <= 0f ? 0f : Mathf.Min(1f, cheapest / Mathf.Max(0.05f, a.Tension));
            float risk = available.Min(t => t.ExposureRisk(c));

            float u = k.wConfidence * fit + k.wStress * stress + k.wTask * task + k.wNovelty * novelty
                      - k.wCost * cost - k.wRisk * risk;

            u *= c.Director.GoalPermission(g, c);

            why = $"{fitWhy}; stress {stress:+0.00;-0.00}, task {task:0.00}, novelty {novelty:0.0}, cost {cost:0.00}, risk {risk:0.00}";
            return Mathf.Max(0f, u);
        }

        static bool Tactical(GoalId g) => g != GoalId.Patrol && g != GoalId.Investigate && g != GoalId.Sweep && g != GoalId.Pursue;

        // How well the situation suits the goal, before any learning (the "Confidence(g)"
        // term of the utility). Each line reads as the rule it is.
        static float Fit(GoalId g, KarenContext c, out string why)
        {
            Appraisal a = c.A;
            switch (g)
            {
                case GoalId.Patrol:
                    why = "maintain coverage";
                    return 0.15f;

                case GoalId.Investigate:
                    if (a.Confidence < 0.06f) { why = "nothing to investigate"; return 0f; }
                    why = $"conf {a.Confidence:0.00}, stale {a.Staleness:0}s";
                    return 0.2f + 0.55f * Mathf.Clamp01(a.Confidence / 0.35f) * (1f - Mathf.Clamp01(a.Staleness / 45f));

                case GoalId.Sweep:
                    why = $"H {a.EntropyNorm:0.00} of max";
                    return 0.12f + 0.45f * a.EntropyNorm * (a.Staleness > 10f ? 1f : 0.4f);

                case GoalId.Flush:
                    if (a.EntropyNorm < 0.5f || a.Staleness < 15f) { why = "not lost enough to flush"; return 0f; }
                    why = $"lost: H {a.EntropyNorm:0.00}, stale {a.Staleness:0}s — make them make a noise";
                    return 0.25f + 0.5f * a.EntropyNorm * Mathf.Clamp01(a.Staleness / 60f);

                case GoalId.Deny:
                    why = $"TaskLoad {a.TaskLoad:0.00}";
                    return 0.15f;

                case GoalId.Herd:
                    if (a.Confidence < 0.25f) { why = "no confident peak to herd from"; return 0f; }
                    why = $"conf {a.Confidence:0.00} — close the cut";
                    return 0.2f + 0.5f * a.Confidence;

                case GoalId.Ambush:
                    float route = c.Ledger.BestRouteConfidence(c, out string routeWhy);
                    if (route < 0.35f) { why = "no confident route prior"; return 0f; }
                    why = routeWhy;
                    return 0.15f + 0.65f * route;

                case GoalId.Stalk:
                    if (a.Confidence < 0.12f) { why = "don't know where to be seen"; return 0f; }
                    float below = Mathf.Max(0f, a.Setpoint - a.Panic);
                    why = $"panic {a.Panic:0.00} below setpoint {a.Setpoint:0.00}";
                    return 0.1f + 0.8f * below;

                case GoalId.Pursue:
                    SightSensor s = c.Body.Sight;
                    if (a.Awareness >= s.confirmAt) { why = $"sighting confirmed ({a.Awareness:0.00})"; return 1.05f; }
                    if (a.Awareness >= s.seeAt) { why = $"seen ({a.Awareness:0.00})"; return 0.55f + (a.Awareness - s.seeAt); }
                    why = "not seen";
                    return 0f;

                case GoalId.Withdraw:
                    float above = Mathf.Max(0f, a.Panic - a.Setpoint);
                    why = a.InRecovery ? "recovery window (§9.4)" : $"panic {a.Panic:0.00} above setpoint {a.Setpoint:0.00}";
                    return 0.05f + 1.2f * above + (a.InRecovery ? 0.45f : 0f);

                case GoalId.Assist:
                    if (a.EnergyBelief >= 0.15f) { why = "employee still has energy"; return 0f; }
                    why = $"energy ~{a.EnergyBelief:0.00} — a burnt-out employee who stays is the objective";
                    return 0.9f;
            }
            why = string.Empty;
            return 0f;
        }
    }

    // Turns a goal into a plan: pick one of the goal's tactics (by learned value where the
    // rung allows, by authored prior otherwise), then decompose it into primitives under a
    // time budget (Karen.md §6.4).
    public static class HtnPlanner
    {
        // Tactics that could serve `goal` right now. Refusals are appended to `refusals`
        // when it's supplied, so the thought log can say why a tactic was ruled out.
        public static List<Tactic> Candidates(GoalId goal, KarenContext c, List<string> refusals)
        {
            var result = new List<Tactic>();
            foreach (Tactic t in TacticLibrary.For(goal))
            {
                string why = null;
                bool ok =
                    Introduced(t, c, ref why) &&
                    Permitted(t, c, ref why) &&
                    OffCooldown(t, c, ref why) &&
                    Affordable(t, c, ref why) &&
                    t.Available(c, out why);

                if (ok) result.Add(t);
                else refusals?.Add($"{t.Id}: {why}");
            }
            return result;
        }

        static bool Introduced(Tactic t, KarenContext c, ref string why)
        {
            if (!c.Features.Goals && t.Tier > 0) { why = "rung has no tactics"; return false; }
            if (c.Brain.CareerOverride || t.IntroducedShift <= c.ShiftNumber) return true;
            why = $"introduced on shift {t.IntroducedShift}";
            return false;
        }

        static bool Permitted(Tactic t, KarenContext c, ref string why)
        {
            if (c.Director.PermitsTier(t.Tier, c)) return true;
            why = $"tier {t.Tier} not permitted ({c.Director.PhaseName})";
            return false;
        }

        static bool OffCooldown(Tactic t, KarenContext c, ref string why)
        {
            if (!t.OnCooldown(c.Now)) return true;
            why = $"cooldown {t.Cooldown - (c.Now - t.LastUsed):0}s";
            return false;
        }

        static bool Affordable(Tactic t, KarenContext c, ref string why)
        {
            if (t.TensionCost <= c.A.Tension + 1e-4f) return true;
            why = $"costs {t.TensionCost:0.00}, budget {c.A.Tension:0.00}";
            return false;
        }

        public static PlanTree Plan(GoalId goal, KarenContext c, System.Func<KarenContext, bool> interrupt,
                                    out Tactic chosen, out List<ThoughtOption> options, out string because)
        {
            var refusals = new List<string>();
            List<Tactic> candidates = Candidates(goal, c, refusals);
            options = new List<ThoughtOption>();
            chosen = null;
            because = null;

            if (candidates.Count == 0)
            {
                because = "no candidates: " + string.Join("; ", refusals.Take(3));
                return null;
            }

            // Score every candidate. The bandit (rungs D+) learns per player; below that the
            // authored prior alone decides, with a seeded jitter to break ties.
            var scored = new List<(Tactic t, float s, string why)>();
            foreach (Tactic t in candidates)
            {
                float s;
                string why;
                if (c.Features.Bandit && t.Learnable)
                {
                    s = c.Ledger.BanditScore(t, c, out why);
                }
                else
                {
                    s = c.Ledger.PriorScore(t, c) + c.Rng.Range(0f, 0.05f);
                    why = $"prior {t.PanicPrior:+0.00;-0.00}";
                }
                scored.Add((t, s, why));
            }
            scored.Sort((x, y) => y.s.CompareTo(x.s));

            foreach (var entry in scored.Take(4))
                options.Add(new ThoughtOption { Name = entry.t.Id, Utility = entry.s, Why = entry.why });

            foreach (var entry in scored)
            {
                var builder = new PlanBuilder(c.Config.planBudgetMs);
                entry.t.Plan(c, builder);
                if (builder.Steps.Count == 0) continue;   // decomposition found nothing to do

                chosen = entry.t;
                because = entry.why + (builder.Target != null ? $" → {builder.Target}" : string.Empty)
                                    + (builder.Truncated ? " (partial: budget)" : string.Empty);
                return new PlanTree($"{goal}({entry.t.Id})", goal, entry.t, builder.Steps, interrupt) { Target = builder.Target };
            }

            because = "every candidate decomposed to nothing";
            return null;
        }

        public static string Describe(List<ThoughtOption> options)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < options.Count; i++)
            {
                if (i > 0) sb.Append(" > ");
                sb.Append(options[i].Name).Append('(').Append(options[i].Utility.ToString("0.00")).Append(')');
            }
            return sb.ToString();
        }
    }
}
