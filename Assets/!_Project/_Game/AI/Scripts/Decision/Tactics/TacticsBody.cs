using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Tactics about space, people, and you (Karen.md §8.4–8.6), plus the blink channel.

    // ================================================================ §8.4 space

    // Min-cut on the region graph from the believed position to everywhere the employee
    // would rather be: the doors, the staff room, the breakers. The cut edges are the
    // shopping list of things to block (§5.3).
    public static class Herding
    {
        public static List<(int from, int to, Vector3 at)> Cut(KarenContext c)
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
        public static bool Winnable(KarenContext c, IEnumerable<int> alsoClosed, out string why)
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
        public override ActionId[] Actions => new[] { ActionId.Corner };
        public override int Tier => 2;
        public override int IntroducedShift => 7;
        public override float TensionCost => 0.2f;
        public override float Cooldown => 120f;
        public override float PanicPrior => 0.15f;
        public override TellKind Tell => TellKind.Scrape;
        public override string Chore => "clear it (6 s, loud) or take the long way";
        public override string Attacks => "space";

        (int from, int to, Vector3 at) edge;

        public override bool Available(KarenContext c, out string why)
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

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            var e = edge;
            Region beyond = c.Map.Regions[e.to];
            Vector3 across = beyond.Centroid - c.Map.Regions[e.from].Centroid;
            across = Vector3.Cross(Vector3.up, across.normalized);
            int width = beyond.Links.Where(l => l.To == e.from).Select(l => l.Capacity).DefaultIfEmpty(3).First();
            plan.Target = $"{c.RegionName(e.from)} | {beyond.Name}";

            plan.Go(e.at, KarenBody.Pace.Hurry, "drag crates to " + beyond.Name, 1.5f);
            plan.Add(new Tell(TellKind.Scrape, k => k.Body.Position, 1f));
            plan.Add(new Effect("crate wall at " + plan.Target, k =>
            {
                k.World.Crates(e.at, across, Mathf.Clamp(width * StoreMap.CellSize * 0.6f, 1.4f, 4.5f), e.to);
                k.Brain.Close(e.to, 150f);
            }));
            plan.Go(TacticHelpers.Retreat(c), KarenBody.Pace.Walk, "step back", 2f);
        }
    }

    public sealed class ShelfRelocationTactic : Tactic
    {
        public override string Id => "shelf_relocation";
        public override string Title => "Shelf relocation";
        public override ActionId[] Actions => new[] { ActionId.Corner };
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

        public override bool Available(KarenContext c, out string why)
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

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            ShelfUnit target = bay;
            Vector3 destination = to;
            Vector3 from = target.transform.position;
            plan.Target = $"{c.Where(from)} → {c.Where(destination)}";
            plan.Go(TacticHelpers.StandIn(target), KarenBody.Pace.Walk, "go to the bay", 2f);
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

    // ================================================================ §8.5 social

    public sealed class MimicryTactic : Tactic
    {
        public override string Id => "mimicry";
        public override string Title => "Mimicry";
        public override ActionId[] Actions => new[] { ActionId.Flush };
        public override int Tier => 3;
        public override int IntroducedShift => 6;
        public override float TensionCost => 0.3f;
        public override float Cooldown => 180f;
        public override float PanicPrior => 0.25f;
        public override TellKind Tell => TellKind.CustomerFreeze;
        public override string Chore => "work out which shopper isn't one";
        public override string Attacks => "social";

        CustomerNPC puppet;

        public override bool Available(KarenContext c, out string why)
        {
            puppet = TacticHelpers.CustomerNear(c, TacticHelpers.Believed(c), 35f);
            why = puppet == null ? "no free shopper near the believed position" : null;
            return puppet != null;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            CustomerNPC target = puppet;
            plan.Target = target.name;
            // The shopper stopping dead is the tell itself, so the freeze starts with it.
            plan.Add(new Tell(TellKind.CustomerFreeze, _ => { target.Freeze(1f); return target.transform.position; }, 1f));
            plan.Add(new Effect("possess " + target.name, k => k.World.Possess(target, 90f)));
        }
    }

    // The understudy (0.3.0): Kenji, "the new hire", comes in through the front doors and
    // shadows you at 10–20 m. While he can see you he stands still and watches, and she knows
    // where you are. Walk up to him and tell him she needs him on the tills, and he leaves.
    // Once a shift. The PA welcomes him only if it's on.
    public sealed class UnderstudyTactic : Tactic
    {
        public override string Id => "understudy";
        public override string Title => "The understudy";
        public override ActionId[] Actions => new[] { ActionId.Flush };
        public override int Tier => 3;
        public override int IntroducedShift => 8;
        public override float TensionCost => 0.25f;
        public override float Cooldown => 99999f;
        public override float PanicPrior => 0.15f;
        public override TellKind Tell => TellKind.PaChime;
        public override string Chore => "walk up to Kenji and send him to the tills";
        public override string Attacks => "social";

        public override bool Available(KarenContext c, out string why)
        {
            if (UsesThisShift > 0) { why = "already hired this shift"; return false; }
            if (Understudy.Current != null) { why = "Kenji's already in"; return false; }
            if (c.World == null || c.World.CustomerPrefab == null) { why = "no one to hire"; return false; }
            why = null;
            return true;
        }

        public override bool EffectRunning(KarenContext c) => Understudy.Current != null && !Understudy.Current.Leaving;

        // You deal with it at Kenji.
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = default;
            if (!EffectRunning(c)) return false;
            at = Understudy.Current.transform.position;
            return true;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            Landmark? door = c.Map.FindLandmark(LandmarkKind.AutoDoor);
            Vector3 at = door.HasValue ? door.Value.Position : c.Body.Position;
            plan.Target = "Kenji";
            if (c.World.Pa.Powered)
                plan.Add(new Speak(k => $"Please welcome our new hire, Kenji! Kenji will be shadowing {TacticHelpers.PlayerName(k)} today.", false));
            else
                c.Think("CHECK", "the PA is switched off: Kenji comes in unannounced");
            plan.Add(new Effect("hire the understudy", k => k.World.HireUnderstudy(TacticHelpers.OnFloor(at))));
        }
    }

    // ================================================================ §8.6 you

    public sealed class StalkTactic : Tactic
    {
        public override string Id => "stalk";
        public override string Title => "Stalk-and-withdraw";
        public override ActionId[] Actions => new[] { ActionId.Loom };
        public override int Tier => 1;
        public override int IntroducedShift => 2;
        public override float TensionCost => 0.08f;
        public override float Cooldown => 60f;
        public override float PanicPrior => 0.15f;
        public override TellKind Tell => TellKind.Footsteps;
        public override string Chore => "none — dread";
        public override string Attacks => "you";
        public override float ExposureRisk(KarenContext c) => 0.1f;

        Vector3 vantage;

        public override bool Available(KarenContext c, out string why)
        {
            bool ok = TacticHelpers.Vantage(c, out vantage);
            why = ok ? null : "no vantage point on the believed position";
            return ok;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            Vector3 at = vantage;
            plan.Target = c.Where(at);
            plan.Go(at, KarenBody.Pace.Walk, "walk to the end of the aisle", 1.2f);
            plan.Add(new Tell(TellKind.Footsteps, k => k.Body.Position, 1f));
            plan.Add(new Face(k => TacticHelpers.Believed(k), "look down the aisle"));
            plan.Add(new Wait(2f, "hold for two seconds"));
            plan.Add(new Effect("leave, without pursuing", k => k.Brain.SuppressPursuit(12f)));
            plan.Go(TacticHelpers.Retreat(c), KarenBody.Pace.Walk, "leave", 2f);
        }
    }

    public sealed class AmbushTactic : Tactic
    {
        public override string Id => "ambush";
        public override string Title => "Ambush";
        public override ActionId[] Actions => new[] { ActionId.LieInWait };
        public override int Tier => 2;
        public override int IntroducedShift => 3;
        public override float TensionCost => 0.2f;
        public override float Cooldown => 150f;
        public override float PanicPrior => 0.3f;
        public override TellKind Tell => TellKind.SilenceFalls;
        public override string Chore => "notice that she has gone quiet";
        public override string Attacks => "you";

        int spot = -1;
        string reason;
        Tactic forChore;

        public override bool Available(KarenContext c, out string why)
        {
            why = null;
            if (!Spot(c, out spot, out _, out reason, out forChore)) { why = "nowhere she's sure you'll pass"; return false; }
            return true;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            Vector3 at = c.Map.CellPosition[spot];
            Tactic chore = forChore;
            plan.Target = c.Map.NameAt(at) + " — " + reason;
            plan.Add(new SetSilent(true));
            plan.Go(at, KarenBody.Pace.Hurry, "pre-position at " + c.Map.NameAt(at), 1.5f);
            plan.Add(new Tell(TellKind.SilenceFalls, k => k.Body.Position, 1f));
            plan.Add(new Hold(c.Config.ambushMaxWait,
                k => k.A.Panic > k.Config.ambushAbortPanic || k.A.Awareness >= k.Body.Sight.confirmAt ||
                     chore != null && !chore.EffectRunning(k),
                "wait, silent (abort if panic > 0.85)"));
            plan.Add(new SetSilent(false));
        }

        // Where to lie in wait, and how sure she is it's on your way (0.2.0): where most of her
        // guesses about your route pass, while she has them; while a trick of hers still leaves
        // you a chore in one place (0.3.0, FixAt), on your way there — so Blackout then Ambush
        // waits by the breaker box, and Tool theft then Ambush by the mop; else the Ledger's
        // usual route to your next job.
        public static bool Spot(KarenContext c, out int cell, out float sure, out string reason) =>
            Spot(c, out cell, out sure, out reason, out _);

        public static bool Spot(KarenContext c, out int cell, out float sure, out string reason, out Tactic chore)
        {
            cell = -1;
            sure = 0f;
            reason = null;
            chore = null;
            RoutePredictor p = c.Brain != null ? c.Brain.Predictor : null;
            if (p != null && p.Active && !c.Body.Sight.SeesNow &&
                CutTactic.ChooseSpot(c.Map, p, c.Body.Position, c.Config.hurrySpeed, Time.time, out CutTactic.CutSpot s))
            {
                cell = s.Cell;
                sure = s.Weight;
                reason = $"most of her guesses pass here ({s.Name}, {s.Weight:P0})";
                return true;
            }
            if (ChoreWaiting(c, out Tactic by, out Vector3 fix))
            {
                int goal = c.Map.CellAt(fix);
                if (goal >= 0)
                {
                    cell = OnTheWay(c, goal, 8f);
                    if (cell >= 0)
                    {
                        sure = 0.6f;
                        chore = by;
                        reason = $"{by.Title} left you a chore: on your way to {c.Map.NameAt(fix)}";
                        return true;
                    }
                }
            }
            int region = c.Ledger.AmbushRegion(c, out reason);
            if (region < 0) return false;
            cell = c.Map.CellAt(c.Map.Regions[region].Centroid);
            sure = c.Ledger.BestRouteConfidence(c, out _);
            return cell >= 0;
        }

        // A chore one of her tricks left you, still waiting in one place: her last trick's first,
        // then any other of hers still in play.
        public static bool ChoreWaiting(KarenContext c, out Tactic by, out Vector3 at)
        {
            at = default;
            Tactic last = c.Last.Id != null ? TacticLibrary.Get(c.Last.Id) : null;
            if (last != null && !last.Draft && last.EffectRunning(c) && last.FixAt(c, out at)) { by = last; return true; }
            foreach (Tactic t in TacticLibrary.Live)
                if (t != last && t.EffectRunning(c) && t.FixAt(c, out at)) { by = t; return true; }
            by = null;
            return false;
        }

        // A cell `before` metres short of `goal` on the shortest walk to it from where she
        // believes you are, over her part of the building.
        static int OnTheWay(KarenContext c, int goal, float before)
        {
            float[] toGoal = c.Map.Distances(goal, float.MaxValue, null, TacticHelpers.NotHers(c.Map));
            int cell = c.Map.CellAt(TacticHelpers.Believed(c));
            if (cell < 0 || float.IsInfinity(toGoal[cell])) return -1;
            for (int guard = c.Map.CellCount; guard > 0 && toGoal[cell] > before; guard--)
            {
                int next = -1;
                float best = toGoal[cell];
                for (int e = c.Map.EdgeStart[cell]; e < c.Map.EdgeStart[cell + 1]; e++)
                    if (toGoal[c.Map.EdgeTo[e]] < best) { best = toGoal[c.Map.EdgeTo[e]]; next = c.Map.EdgeTo[e]; }
                if (next < 0) break;
                cell = next;
            }
            return cell;
        }
    }

    // Hunt's own move: run at you. It used to be the chase, a trick; since 0.2.0 it's what Hunt
    // does with no trick at all. Its pacing is the Hunt action's (KarenDirector.HuntAllowed):
    // a hunt costs huntTensionCost and waits huntCooldownSeconds after the last, whichever of
    // its moves runs, so the move itself costs nothing more.
    public sealed class HuntTactic : Tactic
    {
        public override string Id => "hunt";
        public override string Title => "Hunt";
        public override ActionId[] Actions => new[] { ActionId.Hunt };
        public override bool OwnMove => true;
        public override int Tier => 0;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0f;
        public override float Cooldown => 0f;
        public override float PanicPrior => 0.35f;
        public override TellKind Tell => TellKind.Screech;
        public override string Chore => "get out of her sight: if she touches you, she gets you";
        public override string Attacks => "you";

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            plan.Target = c.Where(c.Body.Sight.LastSeenPosition);
            plan.Add(new Tell(TellKind.Screech, k => k.Body.Position, 1f));
            plan.Add(new Effect("eye goes red", k => k.Body.SetMood(KarenBody.Mood.Hunt)));
            plan.Add(new Pursue(25f));
        }
    }

    // Below 15% energy she turns helpful (§8.6, §10.3), and means it.
    public sealed class FavourTactic : Tactic
    {
        public override string Id => "favour";
        public override string Title => "Peace offering";
        public override ActionId[] Actions => new[] { ActionId.Lure };
        public override int Tier => 1;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0f;
        public override float Cooldown => 60f;
        public override float PanicPrior => -0.1f;
        public override TellKind Tell => TellKind.PaChime;
        public override float TellLead => 1.3f;
        public override string Chore => "drink it";
        public override string Attacks => "nothing, sincerely";

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            Vector3 believed = TacticHelpers.Believed(c);
            bool last = c.Brain.EndgameCoffeeDue;

            // Which kindness: the coffee, a spill mopped for you, or where your mop went.
            Dirt spill = Dirt.Nearest(believed, 15f);
            if (!last && spill != null && c.Rng.Chance(0.35f))
            {
                plan.Target = "mop a spill for you";
                plan.Go(TacticHelpers.OnFloor(spill.transform.position), KarenBody.Pace.Walk, "go to the spill", 1.2f);
                plan.Add(new Tell(TellKind.PaChime, k => k.Body.Position, TellLead));
                plan.Add(new Effect("mop it", k => { if (spill != null) Object.Destroy(spill.gameObject); }));
                plan.Add(Speak.Line("I cleaned that up for you. Take a moment."));
                return;
            }

            plan.Target = last ? "the last coffee" : "a coffee";
            plan.Go(TacticHelpers.OnFloor(believed), KarenBody.Pace.Walk, "bring you a coffee", 4f);
            plan.Add(new Tell(TellKind.PaChime, k => k.Body.Position, TellLead));
            plan.Add(new Effect(last ? "leave the last coffee" : "leave a coffee", k => k.Brain.ServeCoffee(k.Body.Position + k.Body.Forward * 0.8f, last)));
            plan.Add(Speak.Line(last ? $"{TacticHelpers.PlayerName(c)}. I made you a coffee. You've earned it."
                                     : "I've made you a coffee. Stay a little longer."));
            plan.Go(TacticHelpers.Retreat(c), KarenBody.Pace.Walk, "give you space", 3f);
        }
    }
}
