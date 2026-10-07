using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Aiko
{
    // Tactics about space, people, and you (Aiko.md §8.4–8.6), plus the blink channel.

    // ================================================================ §8.4 space

    // Min-cut on the region graph from the believed position to everywhere the employee
    // would rather be: the doors, the staff room, the breakers. The cut edges are the
    // shopping list of things to block (§5.3).
    public static class Herding
    {
        public static List<(int from, int to, Vector3 at)> Cut(AikoContext c)
        {
            var sinks = new HashSet<int>();
            foreach (Landmark l in c.Map.Landmarks)
            {
                if (l.Region < 0) continue;
                switch (l.Kind)
                {
                    case LandmarkKind.AutoDoor:
                    case LandmarkKind.CoffeeMachine:
                    case LandmarkKind.TimeClock:
                    case LandmarkKind.BreakerBox:
                        sinks.Add(l.Region);
                        break;
                }
            }
            sinks.Remove(c.Belief.PeakRegion);
            return c.Map.MinCut(c.Belief.PeakRegion, sinks, c.Brain.ClosedRegions);
        }

        // Fairness rule 6: every unfinished job, and the time clock, must stay reachable.
        public static bool Winnable(AikoContext c, IEnumerable<int> alsoClosed, out string why)
        {
            var closed = new HashSet<int>(c.Brain.ClosedRegions);
            foreach (int r in alsoClosed) closed.Add(r);
            return c.Brain.StillWinnable(closed, out why);
        }
    }

    public sealed class CrateWallTactic : Tactic
    {
        public override string Id => "crate_wall";
        public override string Title => "Crate wall";
        public override GoalId[] Goals => new[] { GoalId.Herd };
        public override int Tier => 2;
        public override int IntroducedShift => 7;
        public override float TensionCost => 0.2f;
        public override float Cooldown => 120f;
        public override float PanicPrior => 0.15f;
        public override TellKind Tell => TellKind.Scrape;
        public override string Chore => "clear it (6 s, loud) or take the long way";
        public override string Attacks => "space";

        (int from, int to, Vector3 at) edge;

        public override bool Available(AikoContext c, out string why)
        {
            var cut = Herding.Cut(c);
            if (cut.Count == 0 || cut.Count > 4) { why = cut.Count == 0 ? "no cut to close" : $"cut too wide ({cut.Count})"; return false; }

            // Close the edge nearest to her body that keeps the shift winnable.
            foreach (var e in cut.OrderBy(e => Vector3.Distance(e.at, c.Body.Position)))
            {
                if (c.Map.Regions[e.to].IsDoor) continue;
                if (!Herding.Winnable(c, new[] { e.to }, out string check)) { c.Think("CHECK", "fairness rule 6 — " + check + " → crate wall VETOED"); continue; }
                edge = e;
                why = null;
                return true;
            }
            why = "every cut edge would make the shift unwinnable";
            return false;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            var e = edge;
            Region beyond = c.Map.Regions[e.to];
            Vector3 across = beyond.Centroid - c.Map.Regions[e.from].Centroid;
            across = Vector3.Cross(Vector3.up, across.normalized);
            int width = beyond.Links.Where(l => l.To == e.from).Select(l => l.Capacity).DefaultIfEmpty(3).First();
            plan.Target = $"{c.RegionName(e.from)} | {beyond.Name}";

            plan.Go(e.at, AikoBody.Pace.Hurry, "drag crates to " + beyond.Name, 1.5f);
            plan.Add(new Tell(TellKind.Scrape, k => k.Body.Position, 1f));
            plan.Add(new Effect("crate wall at " + plan.Target, k =>
            {
                k.World.Crates(e.at, across, Mathf.Clamp(width * StoreMap.CellSize * 0.6f, 1.4f, 4.5f), e.to);
                k.Brain.Close(e.to, 150f);
            }));
            plan.Go(TacticHelpers.Retreat(c), AikoBody.Pace.Walk, "step back", 2f);
        }
    }

    public sealed class DoorLockTactic : Tactic
    {
        public override string Id => "door_lock";
        public override string Title => "Door lock";
        public override GoalId[] Goals => new[] { GoalId.Herd };
        public override int Tier => 2;
        public override int IntroducedShift => 7;
        public override float TensionCost => 0.15f;
        public override float Cooldown => 120f;
        public override float PanicPrior => 0.12f;
        public override TellKind Tell => TellKind.Clunk;
        public override string Chore => "find another way round";
        public override string Attacks => "space";

        HingeDoor door;
        int doorRegion = -1;

        public override bool Available(AikoContext c, out string why)
        {
            door = null;
            float best = float.MaxValue;
            foreach (HingeDoor d in Object.FindObjectsByType<HingeDoor>())
            {
                if (c.World.IsLocked(d)) continue;
                int region = c.Map.RegionAt(d.transform.position);
                if (region < 0) continue;
                float walk = TacticHelpers.WalkFromBelief(c, d.transform.position);
                if (walk < 5f || walk > 40f) continue;
                if (!Herding.Winnable(c, new[] { region }, out string check)) continue;
                if (walk < best) { best = walk; door = d; doorRegion = region; }
            }
            why = door == null ? "no door she can lock and keep the shift winnable" : null;
            return door != null;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            HingeDoor target = door;
            int region = doorRegion;
            plan.Target = c.RegionName(region);
            plan.Add(new Tell(TellKind.Clunk, _ => target.transform.position, 0.9f));
            plan.Add(new Effect("lock " + plan.Target, k =>
            {
                k.World.LockDoor(target, 75f);
                k.Brain.Close(region, 75f);
            }));
        }
    }

    public sealed class ShelfRelocationTactic : Tactic
    {
        public override string Id => "shelf_relocation";
        public override string Title => "Shelf relocation";
        public override GoalId[] Goals => new[] { GoalId.Herd };
        public override int Tier => 3;
        public override int IntroducedShift => 7;
        public override float TensionCost => 0.35f;
        public override float Cooldown => 300f;
        public override float PanicPrior => 0.2f;
        public override TellKind Tell => TellKind.Grinding;
        public override float TellLead => 2f;
        public override string Chore => "relearn the route";
        public override string Attacks => "space";

        ShelfUnit bay;
        Vector3 to;

        public override bool Available(AikoContext c, out string why)
        {
            bay = null;
            // A bay out of sight of the employee, next to open floor.
            foreach (Bay b in c.Map.Bays.OrderBy(_ => c.Rng.Value))
            {
                if (b.Unit == null || StoreMap.AreaAt(b.Position) != "Sales floor") continue;
                if (TacticHelpers.WalkFromBelief(c, b.Position) < 15f) continue;
                foreach (Vector3 step in new[] { Vector3.right * 5f, Vector3.left * 5f, Vector3.forward * 5f, Vector3.back * 5f })
                {
                    Vector3 candidate = b.Position + step;
                    if (StoreMap.AreaAt(candidate) != "Sales floor") continue;
                    if (Physics.CheckBox(candidate + Vector3.up, new Vector3(2.1f, 0.9f, 0.6f), b.Unit.transform.rotation, ~0, QueryTriggerInteraction.Ignore)) continue;
                    bay = b.Unit;
                    to = candidate;
                    break;
                }
                if (bay != null) break;
            }
            why = bay == null ? "no bay with room to move" : null;
            return bay != null;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            ShelfUnit target = bay;
            Vector3 destination = to;
            Vector3 from = target.transform.position;
            plan.Target = $"{c.Where(from)} → {c.Where(destination)}";
            plan.Go(TacticHelpers.StandIn(target), AikoBody.Pace.Walk, "go to the bay", 2f);
            plan.Add(new Tell(TellKind.Grinding, _ => target.transform.position, TellLead));
            bool started = false;
            plan.Add(new Process("roll the bay", (k, dt) =>
            {
                if (!started) { started = true; k.Brain.StartCoroutine(k.World.SlideBay(target, destination, 4f)); }
                return (target.transform.position - destination).sqrMagnitude < 0.01f;
            }, 8f));
            plan.Add(new Effect("check the store is still winnable", k =>
            {
                if (!k.Brain.StillWinnableByNavMesh(out string why))
                {
                    k.Think("CHECK", "fairness rule 6 — " + why + " → bay rolled back");
                    k.Brain.StartCoroutine(k.World.SlideBay(target, from, 3f));
                }
                else
                {
                    k.Brain.MazeChanged();
                }
            }));
        }
    }

    // Block every cut edge but one and wait by the survivor. No scripting — §5.3 executed.
    public sealed class FunnelTactic : Tactic
    {
        public override string Id => "funnel";
        public override string Title => "Funnel";
        public override GoalId[] Goals => new[] { GoalId.Herd };
        public override int Tier => 3;
        public override int IntroducedShift => 8;
        public override float TensionCost => 0.35f;
        public override float Cooldown => 180f;
        public override float PanicPrior => 0.25f;
        public override TellKind Tell => TellKind.Scrape;
        public override string Chore => "walk into it, or clear a wall";
        public override string Attacks => "space";

        List<(int from, int to, Vector3 at)> cut;

        public override bool Available(AikoContext c, out string why)
        {
            cut = Herding.Cut(c);
            if (cut.Count < 2 || cut.Count > 4) { why = $"cut of {cut.Count} edges can't make a funnel"; return false; }
            var closing = cut.Skip(1).Select(e => e.to).ToList();
            if (!Herding.Winnable(c, closing, out string check)) { c.Think("CHECK", "fairness rule 6 — " + check + " → funnel VETOED"); why = "would be unwinnable"; return false; }
            why = null;
            return true;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            var ordered = cut.OrderBy(e => Vector3.Distance(e.at, c.Body.Position)).ToList();
            var survivor = ordered[ordered.Count - 1];
            plan.Target = "leave " + c.RegionName(survivor.to) + " open";
            for (int i = 0; i < ordered.Count - 1; i++)
            {
                var e = ordered[i];
                Vector3 across = Vector3.Cross(Vector3.up, (c.Map.Regions[e.to].Centroid - c.Map.Regions[e.from].Centroid).normalized);
                plan.Go(e.at, AikoBody.Pace.Hurry, "block " + c.RegionName(e.to), 1.5f);
                plan.Add(new Tell(TellKind.Scrape, k => k.Body.Position, 1f));
                plan.Add(new Effect("crate wall at " + c.RegionName(e.to), k =>
                {
                    k.World.Crates(e.at, across, 3f, e.to);
                    k.Brain.Close(e.to, 150f);
                }));
            }
            plan.Go(survivor.at, AikoBody.Pace.Sneak, "wait by the way out", 3f);
            plan.Add(new Hold(60f, k => k.A.Panic > k.Config.ambushAbortPanic || k.A.Awareness > 0.85f, "silent at the survivor"));
        }
    }

    // ================================================================ §8.5 social

    public sealed class MimicryTactic : Tactic
    {
        public override string Id => "mimicry";
        public override string Title => "Mimicry";
        public override GoalId[] Goals => new[] { GoalId.Flush };
        public override int Tier => 3;
        public override int IntroducedShift => 6;
        public override float TensionCost => 0.3f;
        public override float Cooldown => 180f;
        public override float PanicPrior => 0.25f;
        public override TellKind Tell => TellKind.CustomerFreeze;
        public override string Chore => "work out which shopper isn't one";
        public override string Attacks => "social";

        CustomerNPC puppet;

        public override bool Available(AikoContext c, out string why)
        {
            puppet = TacticHelpers.CustomerNear(c, TacticHelpers.Believed(c), 35f);
            why = puppet == null ? "no free shopper near the believed position" : null;
            return puppet != null;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            CustomerNPC target = puppet;
            plan.Target = target.name;
            // The shopper stopping dead is the tell itself, so the freeze starts with it.
            plan.Add(new Tell(TellKind.CustomerFreeze, _ => { target.Freeze(1f); return target.transform.position; }, 1f));
            plan.Add(new Effect("possess " + target.name, k => k.World.Possess(target, 90f)));
        }
    }

    public sealed class WitnessTactic : Tactic
    {
        public override string Id => "witness";
        public override string Title => "The witness";
        public override GoalId[] Goals => new[] { GoalId.Flush, GoalId.Herd };
        public override int Tier => 2;
        public override int IntroducedShift => 6;
        public override float TensionCost => 0.12f;
        public override float Cooldown => 90f;
        public override float PanicPrior => 0.1f;
        public override TellKind Tell => TellKind.CustomerFreeze;
        public override float TellLead => 0.8f;
        public override string Chore => "you can't tell a shopper to leave";
        public override string Attacks => "social";

        CustomerNPC shopper;

        public override bool Available(AikoContext c, out string why)
        {
            shopper = TacticHelpers.CustomerNear(c, TacticHelpers.Believed(c), 40f);
            why = shopper == null ? "no shopper to send" : null;
            return shopper != null;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            CustomerNPC target = shopper;
            Vector3 believed = TacticHelpers.Believed(c);
            plan.Target = $"{target.name} → {c.RegionName(c.Belief.PeakRegion)}";
            plan.Add(new Tell(TellKind.CustomerFreeze, _ => target.transform.position, TellLead));
            plan.Add(new Effect("send " + target.name + " to look", k => target.SendToLook(believed, 12f)));
        }
    }

    public sealed class UnderstudyTactic : PaTactic
    {
        public override string Id => "understudy";
        public override string Title => "The understudy";
        public override GoalId[] Goals => new[] { GoalId.Flush };
        public override int Tier => 3;
        public override int IntroducedShift => 8;
        public override float TensionCost => 0.25f;
        public override float Cooldown => 99999f;
        public override float PanicPrior => 0.15f;
        public override string Chore => "lose him, politely";
        public override string Attacks => "social";

        public override bool Available(AikoContext c, out string why)
        {
            if (UsesThisShift > 0) { why = "already hired this shift"; return false; }
            if (c.World.CustomerPrefab == null) { why = "no one to hire"; return false; }
            why = null;
            return true;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            Landmark? door = c.Map.FindLandmark(LandmarkKind.AutoDoor);
            Vector3 at = door.HasValue ? door.Value.Position : c.Body.Position;
            plan.Target = "Kenji";
            plan.Add(Speak.Line($"Please welcome our new hire, Kenji! Kenji will be shadowing {TacticHelpers.PlayerName(c)} today."));
            plan.Add(new Effect("hire the understudy", k => k.World.HireUnderstudy(TacticHelpers.OnFloor(at))));
        }
    }

    // ================================================================ §8.6 you

    public sealed class StalkTactic : Tactic
    {
        public override string Id => "stalk";
        public override string Title => "Stalk-and-withdraw";
        public override GoalId[] Goals => new[] { GoalId.Stalk };
        public override int Tier => 1;
        public override int IntroducedShift => 2;
        public override float TensionCost => 0.08f;
        public override float Cooldown => 60f;
        public override float PanicPrior => 0.15f;
        public override TellKind Tell => TellKind.Footsteps;
        public override string Chore => "none — dread";
        public override string Attacks => "you";
        public override float ExposureRisk(AikoContext c) => 0.1f;

        Vector3 vantage;

        public override bool Available(AikoContext c, out string why)
        {
            bool ok = TacticHelpers.Vantage(c, out vantage);
            why = ok ? null : "no vantage point on the believed position";
            return ok;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            Vector3 at = vantage;
            plan.Target = c.Where(at);
            plan.Go(at, AikoBody.Pace.Walk, "walk to the end of the aisle", 1.2f);
            plan.Add(new Tell(TellKind.Footsteps, k => k.Body.Position, 1f));
            plan.Add(new Face(k => TacticHelpers.Believed(k), "look down the aisle"));
            plan.Add(new Wait(2f, "hold for two seconds"));
            plan.Add(new Effect("leave, without pursuing", k => k.Brain.SuppressPursuit(12f)));
            plan.Go(TacticHelpers.Retreat(c), AikoBody.Pace.Walk, "leave", 2f);
        }
    }

    public sealed class AmbushTactic : Tactic
    {
        public override string Id => "ambush";
        public override string Title => "Ambush";
        public override GoalId[] Goals => new[] { GoalId.Ambush };
        public override int Tier => 2;
        public override int IntroducedShift => 8;
        public override float TensionCost => 0.2f;
        public override float Cooldown => 150f;
        public override float PanicPrior => 0.3f;
        public override TellKind Tell => TellKind.SilenceFalls;
        public override string Chore => "notice that she has gone quiet";
        public override string Attacks => "you";

        int spot = -1;
        string reason;

        public override bool Available(AikoContext c, out string why)
        {
            spot = c.Ledger.AmbushRegion(c, out reason);
            why = spot < 0 ? "no route prior strong enough" : null;
            return spot >= 0;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            Vector3 at = c.Map.Regions[spot].Centroid;
            plan.Target = c.RegionName(spot) + " — " + reason;
            plan.Add(new SetSilent(true));
            plan.Go(at, AikoBody.Pace.Hurry, "pre-position at " + c.RegionName(spot), 1.5f);
            plan.Add(new Tell(TellKind.SilenceFalls, k => k.Body.Position, 1f));
            plan.Add(new Hold(c.Config.ambushMaxWait,
                k => k.A.Panic > k.Config.ambushAbortPanic || k.A.Awareness >= k.Body.Sight.confirmAt,
                "wait, silent (abort if panic > 0.85)"));
            plan.Add(new SetSilent(false));
        }
    }

    // The chase: rare and expensive, a guaranteed quiet afterwards, and being caught is a
    // written warning, not death.
    public sealed class ChaseTactic : Tactic
    {
        public override string Id => "chase";
        public override string Title => "The chase";
        public override GoalId[] Goals => new[] { GoalId.Pursue };
        public override int Tier => 4;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0.6f;
        public override float Cooldown => 90f;
        public override float PanicPrior => 0.35f;
        public override TellKind Tell => TellKind.Screech;
        public override string Chore => "a written warning, a lecture, and overtime if she catches you";
        public override string Attacks => "you";

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            plan.Target = c.Where(c.Body.Sight.LastSeenPosition);
            plan.Add(new Tell(TellKind.Screech, k => k.Body.Position, 1f));
            plan.Add(new Effect("eye goes red", k => k.Body.SetMood(AikoBody.Mood.Hunt)));
            plan.Add(new Pursue(25f));
        }
    }

    // When the chase isn't affordable she still closes distance — at a pace a walking
    // employee can keep ahead of.
    public sealed class FollowTactic : Tactic
    {
        public override string Id => "follow";
        public override string Title => "Follow";
        public override GoalId[] Goals => new[] { GoalId.Pursue };
        public override int Tier => 1;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0.03f;
        public override float Cooldown => 5f;
        public override float PanicPrior => 0.1f;
        public override TellKind Tell => TellKind.Footsteps;
        public override string Chore => "keep walking";
        public override string Attacks => "you";

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            plan.Target = "close distance";
            plan.Add(new Tell(TellKind.Footsteps, k => k.Body.Position, 0.8f));
            plan.Add(new MoveTo(k => k.Body.Sight.Time_SinceSeen() < 3f ? k.Body.Sight.LastSeenPosition : k.Belief.PeakPosition,
                                AikoBody.Pace.Hurry, "follow", 2.5f, 12f, follow: true));
        }
    }

    // Below 15% energy she turns helpful (§8.6, §10.3), and means it.
    public sealed class FavourTactic : Tactic
    {
        public override string Id => "favour";
        public override string Title => "The favour";
        public override GoalId[] Goals => new[] { GoalId.Assist };
        public override int Tier => 1;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0f;
        public override float Cooldown => 60f;
        public override float PanicPrior => -0.1f;
        public override TellKind Tell => TellKind.PaChime;
        public override float TellLead => 1.3f;
        public override string Chore => "drink it";
        public override string Attacks => "nothing, sincerely";

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            Vector3 believed = TacticHelpers.Believed(c);
            bool last = c.Brain.EndgameCoffeeDue;

            // Which kindness: the coffee, a spill mopped for you, or where your mop went.
            Dirt spill = Dirt.Nearest(believed, 15f);
            if (!last && spill != null && c.Rng.Chance(0.35f))
            {
                plan.Target = "mop a spill for you";
                plan.Go(TacticHelpers.OnFloor(spill.transform.position), AikoBody.Pace.Walk, "go to the spill", 1.2f);
                plan.Add(new Tell(TellKind.PaChime, k => k.Body.Position, TellLead));
                plan.Add(new Effect("mop it", k => { if (spill != null) Object.Destroy(spill.gameObject); }));
                plan.Add(Speak.Line("I cleaned that up for you. Take a moment."));
                return;
            }

            plan.Target = last ? "the last coffee" : "a coffee";
            plan.Go(TacticHelpers.OnFloor(believed), AikoBody.Pace.Walk, "bring you a coffee", 4f);
            plan.Add(new Tell(TellKind.PaChime, k => k.Body.Position, TellLead));
            plan.Add(new Effect(last ? "leave the last coffee" : "leave a coffee", k => k.Brain.ServeCoffee(k.Body.Position + k.Body.Forward * 0.8f, last)));
            plan.Add(Speak.Line(last ? $"{TacticHelpers.PlayerName(c)}. I made you a coffee. You've earned it."
                                     : "I've made you a coffee. Stay a little longer."));
            plan.Go(TacticHelpers.Retreat(c), AikoBody.Pace.Walk, "give you space", 3f);
        }
    }

    // ================================================================ IDEAS.md: blink

    // Exploit the window, don't chase the latency: a blink is ~300 ms and she learns of it
    // ~100 ms in, so she knows how long you have left with your eyes shut — and moves then.
    public sealed class BlinkAdvanceTactic : Tactic
    {
        public override string Id => "blink_advance";
        public override string Title => "Blink advance";
        public override GoalId[] Goals => new[] { GoalId.Stalk, GoalId.Pursue };
        public override int Tier => 1;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0.05f;
        public override float Cooldown => 8f;
        public override float PanicPrior => 0.2f;
        public override TellKind Tell => TellKind.EyeFlicker;
        public override float TellLead => 0.9f;
        public override string Chore => "don't blink";
        public override string Attacks => "you";

        public override bool Available(AikoContext c, out string why)
        {
            if (!c.Features.Blink) { why = "rung has no blink channel"; return false; }
            if (!c.Brain.BlinkLive) { why = "no blink signal"; return false; }
            SightSensor s = c.Body.Sight;
            if (!s.SeesNow || !s.IsWatched) { why = "not being looked at"; return false; }
            why = null;
            return true;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            plan.Target = "move while your eyes are shut";
            plan.Add(new Tell(TellKind.EyeFlicker, k => k.Body.Position, TellLead));
            plan.Add(new WaitUntil(k => k.Brain.EyesClosed, 12f, "wait for a blink", failOnTimeout: true));
            plan.Add(new Effect("advance inside the blink", k =>
            {
                float window = Mathf.Max(0.08f, k.Brain.PredictedReopenIn);
                k.Body.BurstToward(k.Body.Sight.LastSeenPosition, k.Config.blinkAdvanceSpeed, window, k.Config.blinkAdvanceMaxDistance);
            }));
            plan.Add(new Wait(0.6f, "stand very still"));
        }
    }
}
