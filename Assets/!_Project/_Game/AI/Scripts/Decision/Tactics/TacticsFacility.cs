using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Aiko
{
    // Tactics where Aiko acts as the building rather than the body: the lights, the shelves,
    // the bins, the HUD, the PA (AIKO.md §8.1–8.3).

    // ================================================================ §8.1 sight

    public sealed class BlackoutTactic : Tactic
    {
        public override string Id => "blackout";
        public override string Title => "Blackout";
        public override GoalId[] Goals => new[] { GoalId.Flush };
        public override int Tier => 3;
        public override int IntroducedShift => 3;
        public override float TensionCost => 0.45f;
        public override float Cooldown => 240f;
        public override float PanicPrior => 0.3f;
        public override TellKind Tell => TellKind.BallastWhine;
        public override float TellLead => 1.2f;
        public override string Chore => "find the torch, cross the maze to the breaker box, reset three breakers low hum to high";
        public override string Attacks => "sight";

        public override bool Available(AikoContext c, out string why)
        {
            why = PowerSystem.PowerOn ? null : "the lights are already out";
            return PowerSystem.PowerOn;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            // Escalation by career: one wing first, the whole store later.
            bool full = c.ShiftNumber >= 5;
            Vector3 believed = TacticHelpers.Believed(c);
            LightCircuit wing = LightControl.CircuitOf(believed);
            plan.Target = full ? "the whole store" : wing + " circuit";

            plan.Add(new Tell(TellKind.BallastWhine, _ => believed, TellLead));
            plan.Add(new Effect(full ? "blackout (full)" : $"blackout ({wing})", k =>
            {
                if (full) k.World.Blackout(k.Rng);
                else k.World.TripCircuit(wing);
                CustomerMemory.MakeNearbyJumpy(believed, 20f, 60f);
            }));

            // Then wait where the chore has to go: on the way to the breaker box.
            Landmark? box = c.Map.FindLandmark(LandmarkKind.BreakerBox);
            if (box.HasValue && c.Ledger.RoutePrior(c, c.Belief.PeakRegion, box.Value.Region, out int ambushRegion, out _))
            {
                Vector3 at = c.Map.Regions[ambushRegion].Centroid;
                plan.Go(at, AikoBody.Pace.Hurry, "wait on the route to the breakers", 1.5f);
                plan.Add(new Hold(60f, k => k.A.Panic > k.Config.ambushAbortPanic || PowerSystem.PowerOn, "silent at " + c.RegionName(ambushRegion)));
            }
            else if (box.HasValue)
            {
                Vector3 near = box.Value.Position + (believed - box.Value.Position).normalized * 8f;
                plan.Go(TacticHelpers.OnFloor(near), AikoBody.Pace.Hurry, "wait near the breakers", 2f);
                plan.Add(new Hold(45f, k => k.A.Panic > k.Config.ambushAbortPanic, "silent near the breaker box"));
            }
        }
    }

    public sealed class MirrorBlackTactic : Tactic
    {
        public override string Id => "mirror_black";
        public override string Title => "Mirror-black";
        public override GoalId[] Goals => new[] { GoalId.Stalk, GoalId.Flush };
        public override int Tier => 1;
        public override int IntroducedShift => 3;
        public override float TensionCost => 0.05f;
        public override float Cooldown => 30f;
        public override float PanicPrior => 0.08f;
        public override TellKind Tell => TellKind.Flicker;
        public override float TellLead => 0.9f;
        public override string Chore => "none — purely psychological";
        public override string Attacks => "sight";

        public override bool Available(AikoContext c, out string why)
        {
            bool ok = PowerSystem.PowerOn && LightProbe.NearestOn(TacticHelpers.Believed(c), 6f) != null && c.A.Confidence > 0.2f;
            why = ok ? null : "no working light over the believed position";
            return ok;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            Vector3 believed = TacticHelpers.Believed(c);
            plan.Target = c.RegionName(c.Belief.PeakRegion);
            plan.Add(new Tell(TellKind.Flicker, _ => believed, TellLead));
            plan.Add(new Effect("kill the light over " + plan.Target, k => k.World.MirrorBlack(believed)));
        }
    }

    public sealed class FogTactic : Tactic
    {
        public override string Id => "fog";
        public override string Title => "Fog";
        public override GoalId[] Goals => new[] { GoalId.Flush, GoalId.Herd };
        public override int Tier => 2;
        public override int IntroducedShift => 6;
        public override float TensionCost => 0.2f;
        public override float Cooldown => 150f;
        public override float PanicPrior => 0.15f;
        public override TellKind Tell => TellKind.FreezerHiss;
        public override string Chore => "cross it blind, or go the long way";
        public override string Attacks => "sight";

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            // The freezers stand along the back wall; flood whichever aisle end is nearest
            // to where she thinks you are.
            Vector3 believed = TacticHelpers.Believed(c);
            Vector3 at = TacticHelpers.OnFloor(new Vector3(believed.x, 0f, Mathf.Min(believed.z, -160f)));
            plan.Target = c.Where(at);
            plan.Add(new Tell(TellKind.FreezerHiss, _ => at, 1f));
            plan.Add(new Effect("vent a freezer at " + plan.Target, k => k.World.Fog(at, 4.5f, 70f)));
        }
    }

    public sealed class CameraBoltOnTactic : Tactic
    {
        public override string Id => "camera_bolt_on";
        public override string Title => "Camera bolt-on";
        public override GoalId[] Goals => new[] { GoalId.Herd, GoalId.Deny };
        public override int Tier => 2;
        public override int IntroducedShift => 8;
        public override float TensionCost => 0.15f;
        public override float Cooldown => 300f;
        public override float PanicPrior => 0.1f;
        public override TellKind Tell => TellKind.Drilling;
        public override float TellLead => 1.5f;
        public override string Chore => "stop hiding there, or unplug it and announce yourself";
        public override string Attacks => "sight";

        int spot = -1;

        public override bool Available(AikoContext c, out string why)
        {
            spot = -1;
            foreach (int region in c.Ledger.ConcealmentRegions(c, 3))
            {
                Vector3 p = c.Map.Regions[region].Centroid;
                if (CctvCamera.All.Any(cam => cam != null && cam.BoltedOn && Vector3.Distance(cam.transform.position, p) < 6f)) continue;
                spot = region;
                break;
            }
            why = spot < 0 ? "no habitual hiding spot without a camera" : null;
            return spot >= 0;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            if (spot < 0) return;
            Vector3 at = c.Map.Regions[spot].Centroid;
            plan.Target = c.RegionName(spot);
            plan.Go(at, AikoBody.Pace.Walk, "go to your hiding place", 2f);
            plan.Add(new Tell(TellKind.Drilling, k => k.Body.Position, TellLead));
            plan.Add(new Effect("bolt a camera over " + plan.Target, k => k.World.BoltOnCamera(at)));
        }
    }

    // ================================================================ §8.2 work

    public sealed class ShelfSweepTactic : Tactic
    {
        public override string Id => "shelf_sweep";
        public override string Title => "Shelf sweep";
        public override GoalId[] Goals => new[] { GoalId.Deny, GoalId.Flush };
        public override int Tier => 1;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0.1f;
        public override float Cooldown => 45f;
        public override float PanicPrior => 0.1f;
        public override TellKind Tell => TellKind.ShelfRattle;
        public override string Chore => "restock the bay — and the stock on the floor is a noise carpet";
        public override string Attacks => "work";

        ShelfUnit bay;

        public override bool Available(AikoContext c, out string why)
        {
            bay = TacticHelpers.BayToSweep(c, out string check);
            if (check != null) c.Think("CHECK", check);
            why = bay == null ? "no full bay far enough from the employee" : null;
            return bay != null;
        }

        public override float ExposureRisk(AikoContext c) =>
            bay == null ? 0f : Mathf.Clamp01(1f - TacticHelpers.WalkFromBelief(c, bay.transform.position) / 25f);

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            if (bay == null) return;
            ShelfUnit target = bay;
            Vector3 standIn = TacticHelpers.StandIn(target);
            plan.Target = c.Where(standIn);
            plan.Go(standIn, AikoBody.Pace.Walk, "walk to " + plan.Target, 1.5f);

            // Don't get caught doing it: wait until no employee noise is close by.
            plan.Add(new WaitUntil(k => !k.Brain.HeardPlayerWithin(10f, 3f), 10f, "wait for quiet"));
            plan.Add(new Tell(TellKind.ShelfRattle, _ => target.transform.position, 1f));

            var slots = target.GetComponentsInChildren<ShelfSlot>().Where(s => s.isFilled).ToList();
            int ejected = 0;
            float timer = 0f;
            plan.Add(new Process("strip " + plan.Target, (k, dt) =>
            {
                timer -= dt;
                if (timer > 0f) return false;
                timer = 0.4f;
                for (int i = 0; i < 4 && ejected < 8 && ejected < slots.Count; i++, ejected++)
                    if (slots[ejected] != null) slots[ejected].Eject();
                return ejected >= Mathf.Min(8, slots.Count);
            }));
            plan.Add(new Effect("mark sabotage", k => k.Brain.MarkSabotaged(target)));
            plan.Go(TacticHelpers.Retreat(c), AikoBody.Pace.Walk, "retreat", 2.5f);
        }
    }

    public sealed class SpillTactic : Tactic
    {
        public override string Id => "spill";
        public override string Title => "Spill";
        public override GoalId[] Goals => new[] { GoalId.Deny, GoalId.Herd };
        public override int Tier => 1;
        public override int IntroducedShift => 4;
        public override float TensionCost => 0.08f;
        public override float Cooldown => 45f;
        public override float PanicPrior => 0.08f;
        public override TellKind Tell => TellKind.BucketClank;
        public override string Chore => "mop it (3 s standing still) or walk through and leave a trail";
        public override string Attacks => "work";

        public override bool Available(AikoContext c, out string why)
        {
            why = c.World.DirtPrefab == null ? "no spill to make" : null;
            return c.World.DirtPrefab != null;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            // A chokepoint you're likely to cross next — the neck of the pocket you're in.
            Region best = null;
            float bestScore = float.MinValue;
            foreach (Region r in c.Map.Regions)
            {
                if (!r.IsChokepoint || r.IsDoor) continue;
                float walk = TacticHelpers.WalkFromBelief(c, r.Centroid);
                if (walk < 6f || walk > 30f) continue;
                float score = -Mathf.Abs(walk - 14f) - Vector3.Distance(r.Centroid, c.Body.Position) * 0.3f;
                if (score > bestScore) { bestScore = score; best = r; }
            }
            Vector3 at = best != null ? best.Centroid : TacticHelpers.OnFloor(TacticHelpers.Believed(c) + Vector3.forward * 8f);
            plan.Target = best != null ? best.Name : c.Where(at);
            if (TacticHelpers.DenyVetoed(c, TaskManager.TaskKind.Mop, at, out string check)) { c.Think("CHECK", check); return; }

            plan.Go(at, AikoBody.Pace.Walk, "go to " + plan.Target, 1.2f);
            plan.Add(new Tell(TellKind.BucketClank, k => k.Body.Position, 1f));
            plan.Add(new Effect("kick over a bucket at " + plan.Target, k => k.World.Spill(k.Body.Position)));
            plan.Go(TacticHelpers.Retreat(c), AikoBody.Pace.Walk, "retreat", 2.5f);
        }
    }

    public sealed class BinTamperTactic : Tactic
    {
        public override string Id => "bin_tamper";
        public override string Title => "Bin tamper";
        public override GoalId[] Goals => new[] { GoalId.Deny };
        public override int Tier => 1;
        public override int IntroducedShift => 4;
        public override float TensionCost => 0.06f;
        public override float Cooldown => 60f;
        public override float PanicPrior => 0.06f;
        public override TellKind Tell => TellKind.LidClatter;
        public override string Chore => "bag it and walk it out back again";
        public override string Attacks => "work";

        Trashcan bin;

        public override bool Available(AikoContext c, out string why)
        {
            bin = null;
            foreach (Trashcan t in Trashcan.All)
                if (t != null && t.UsageCount == 0 && TacticHelpers.WalkFromBelief(c, t.transform.position) > 10f) { bin = t; break; }
            why = bin == null ? "no emptied bin out of sight" : null;
            return bin != null;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            if (bin == null) return;
            Trashcan target = bin;
            plan.Target = c.Where(TacticHelpers.OnFloor(target.transform.position));
            if (TacticHelpers.DenyVetoed(c, TaskManager.TaskKind.Trash, target.transform.position, out string check)) { c.Think("CHECK", check); return; }
            plan.Go(TacticHelpers.OnFloor(target.transform.position), AikoBody.Pace.Walk, "go to the bin", 1.6f);
            plan.Add(new Tell(TellKind.LidClatter, _ => target.transform.position, 0.9f));
            plan.Add(new Effect("refill " + plan.Target, k => { for (int i = 0; i < 3; i++) target.RegisterUse(); }));
        }
    }

    public sealed class TaskFalsificationTactic : Tactic
    {
        public override string Id => "task_falsification";
        public override string Title => "Task falsification";
        public override GoalId[] Goals => new[] { GoalId.Deny };
        public override int Tier => 3;
        public override int IntroducedShift => 9;
        public override float TensionCost => 0.2f;
        public override float Cooldown => 150f;
        public override float PanicPrior => 0.2f;
        public override TellKind Tell => TellKind.CrtTick;
        public override float TellLead => 0.85f;
        public override string Chore => "physically check the store — the HUD is no longer ground truth";
        public override string Attacks => "trust";

        public override bool Available(AikoContext c, out string why)
        {
            why = HudFeed.Mode != Falsification.None ? "already lying" : null;
            return HudFeed.Mode == Falsification.None;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            // Early on she hides an unfinished job; later she adds jobs, and eventually real ones.
            TaskManager tasks = c.Tasks;
            Falsification how;
            TaskManager.TaskKind kind = TaskManager.TaskKind.Stock;
            string label = "Restock the shelves";

            var open = tasks.Tasks.Where(t => !t.IsComplete).ToList();
            if (c.ShiftNumber >= 11)
            {
                how = Falsification.RealTaskEarly;
                label = "Check the stockroom";
            }
            else if (open.Count > 0 && c.Rng.Chance(0.6f))
            {
                how = Falsification.ShowUndoneAsDone;
                kind = open[c.Rng.Range(0, open.Count)].Kind;
            }
            else
            {
                how = Falsification.FakeTask;
                label = c.Rng.Chance(0.5f) ? "Face up the dairy wall" : "Sign the delivery note";
            }

            plan.Target = $"{how} ({kind})";
            plan.Add(new Tell(TellKind.CrtTick, k => k.Body.Position, TellLead));
            plan.Add(new Effect("falsify the HUD: " + plan.Target, k => HudFeed.Falsify(how, kind, label, 90f)));
        }
    }

    public sealed class ToolTheftTactic : Tactic
    {
        public override string Id => "tool_theft";
        public override string Title => "Tool theft";
        public override GoalId[] Goals => new[] { GoalId.Deny };
        public override int Tier => 2;
        public override int IntroducedShift => 5;
        public override float TensionCost => 0.1f;
        public override float Cooldown => 180f;
        public override float PanicPrior => 0.08f;
        public override TellKind Tell => TellKind.MopRattle;
        public override string Chore => "find the mop";
        public override string Attacks => "work";

        ToolSnapPoint rack;

        public override bool Available(AikoContext c, out string why)
        {
            rack = Object.FindObjectsByType<ToolSnapPoint>()
                         .FirstOrDefault(s => s.tool != null && s.tool.type == ItemType.Mop && s.IsToolHome);
            why = rack == null ? "the mop isn't on its rack" : null;
            if (rack != null && TacticHelpers.WalkFromBelief(c, rack.transform.position) < 12f) { why = "the employee is near the rack"; return false; }
            return rack != null;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            if (rack == null) return;
            ToolSnapPoint home = rack;
            Item mop = home.tool;
            int hideRegion = c.Ledger.LeastVisitedRegion(c);
            Vector3 hide = c.Map.Regions[hideRegion].Centroid;
            plan.Target = "hide the mop in " + c.RegionName(hideRegion);

            plan.Go(TacticHelpers.OnFloor(home.transform.position), AikoBody.Pace.Walk, "go to the mop rack", 1.5f);
            plan.Add(new Tell(TellKind.MopRattle, _ => home.transform.position, 0.9f));
            plan.Add(new Effect("take the mop", k => k.Body.Carry(mop)));
            plan.Go(hide, AikoBody.Pace.Walk, plan.Target, 1.5f);
            plan.Add(new Effect("leave the mop in " + c.RegionName(hideRegion), k =>
            {
                k.Body.PutDown(mop);
                TraceRegistry.Add(TraceKind.MopAway, mop.transform.position, float.PositiveInfinity, default, mop, mop.transform);
            }));
        }
    }

    // Arms a refusal at the time clock. The effect lands when the player punches out.
    public sealed class OvertimeTactic : Tactic
    {
        public override string Id => "overtime";
        public override string Title => "The overtime";
        public override GoalId[] Goals => new[] { GoalId.Deny };
        public override int Tier => 4;
        public override int IntroducedShift => 10;
        public override float TensionCost => 0.3f;
        public override float Cooldown => 99999f;
        public override float PanicPrior => 0.3f;
        public override TellKind Tell => TellKind.PunchBuzz;
        public override string Chore => "more shift";
        public override string Attacks => "you";

        public override bool Available(AikoContext c, out string why)
        {
            bool ok = c.A.TaskLoad >= 0.8f && !c.Brain.OvertimeArmed && UsesThisShift == 0;
            why = ok ? null : "not close enough to clocking out";
            return ok;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            plan.Target = "the time clock";
            plan.Add(new Effect("arm the overtime", k => k.Brain.ArmOvertime()));
        }
    }

    // ================================================================ §8.3 sound and trust

    public abstract class PaTactic : Tactic
    {
        public override TellKind Tell => TellKind.PaChime;
        public override float TellLead => 1.3f;
        public override string Attacks => "trust";

        public override bool Available(AikoContext c, out string why)
        {
            if (c.World.Pa.Jammed) { why = "PA jammed"; return false; }
            if (c.World.Pa.Busy) { why = "PA busy"; return false; }
            why = null;
            return true;
        }
    }

    public sealed class PaDecoyTactic : PaTactic
    {
        public override string Id => "pa_decoy";
        public override string Title => "PA decoy";
        public override GoalId[] Goals => new[] { GoalId.Flush };
        public override int Tier => 2;
        public override int IntroducedShift => 5;
        public override float TensionCost => 0.12f;
        public override float Cooldown => 60f;
        public override float PanicPrior => 0.12f;
        public override string Chore => "go and look — or don't";

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            // An aisle she is nowhere near, that you'd have to cross the store to check.
            Region decoy = c.Map.Regions
                .Where(r => r.Area == "Sales floor" && !r.IsDoor && r.Cells.Count > 5)
                .OrderByDescending(r => Vector3.Distance(r.Centroid, c.Body.Position) + c.Rng.Value * 10f)
                .FirstOrDefault();
            if (decoy == null) return;
            bool real = c.Rng.Chance(0.4f);
            string where = StoreMap.ShortSign(decoy.Section);
            plan.Target = decoy.Name + (real ? " (true)" : " (lie)");
            plan.Add(Speak.Line($"Cleanup needed in {where}. Cleanup, {where}."));
            if (real) plan.Add(new Effect("make it true", k => k.World.Spill(decoy.Centroid)));
        }
    }

    public sealed class PaAnnouncePositionTactic : PaTactic
    {
        public override string Id => "pa_announce_position";
        public override string Title => "PA: your position";
        public override GoalId[] Goals => new[] { GoalId.Flush };
        public override int Tier => 2;
        public override int IntroducedShift => 5;
        public override float TensionCost => 0.15f;
        public override float Cooldown => 90f;
        public override float PanicPrior => 0.18f;
        public override string Chore => "move before the customers finish staring";

        public override bool Available(AikoContext c, out string why)
        {
            if (!base.Available(c, out why)) return false;
            if (c.A.Confidence < 0.25f) { why = "not sure enough to announce"; return false; }
            return true;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            Vector3 believed = TacticHelpers.Believed(c);
            string where = c.Map.RegionOf(believed)?.Section;
            where = string.IsNullOrEmpty(where) ? c.RegionName(c.Belief.PeakRegion) : where;
            plan.Target = where;
            plan.Add(Speak.Line($"For customer convenience, our employee {TacticHelpers.PlayerName(c)} is in {where}."));
            plan.Add(new Effect("customers turn to look", k =>
            {
                foreach (CustomerMemory m in CustomerMemory.All)
                    if (m != null && Vector3.Distance(m.transform.position, believed) < 22f) m.StareAt(believed, 6f);
            }));
        }
    }

    public sealed class PaTaskReadbackTactic : PaTactic
    {
        public override string Id => "pa_task_readback";
        public override string Title => "PA: your task list, slightly wrong";
        public override GoalId[] Goals => new[] { GoalId.Deny, GoalId.Flush };
        public override int Tier => 1;
        public override int IntroducedShift => 5;
        public override float TensionCost => 0.06f;
        public override float Cooldown => 90f;
        public override float PanicPrior => 0.05f;
        public override string Chore => "doubt your own list";

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            var open = c.Tasks.Tasks.Where(t => !t.IsComplete).Select(t => t.Label.ToLowerInvariant()).ToList();
            open.Add(c.Rng.Pick(new[] { "polish the trolleys", "count the coins", "face up the cereal" }));
            plan.Target = string.Join(", ", open);
            plan.Add(Speak.Line($"{TacticHelpers.PlayerName(c)}, your remaining tasks are: {string.Join(", ", open)}."));
        }
    }

    public sealed class PaCountdownTactic : PaTactic
    {
        public override string Id => "pa_countdown";
        public override string Title => "PA: countdown";
        public override GoalId[] Goals => new[] { GoalId.Deny };
        public override int Tier => 1;
        public override int IntroducedShift => 5;
        public override float TensionCost => 0.04f;
        public override float Cooldown => 60f;
        public override float PanicPrior => 0.07f;
        public override string Chore => "hurry";

        public override bool Available(AikoContext c, out string why)
        {
            if (!base.Available(c, out why)) return false;
            bool behind = c.Shift != null && c.Shift.TimeRemaining < 120f && c.A.TaskLoad < 0.7f;
            why = behind ? null : "not behind";
            return behind;
        }

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            int s = Mathf.CeilToInt(c.Shift.TimeRemaining);
            plan.Add(Speak.Line($"{TacticHelpers.PlayerName(c)}, you have {s / 60}:{s % 60:00} remaining. Your tasks are not complete."));
        }
    }

    public sealed class PaFootstepsTactic : PaTactic
    {
        public override string Id => "pa_footsteps";
        public override string Title => "PA: her footsteps, elsewhere";
        public override GoalId[] Goals => new[] { GoalId.Flush, GoalId.Stalk };
        public override int Tier => 2;
        public override int IntroducedShift => 5;
        public override float TensionCost => 0.1f;
        public override float Cooldown => 90f;
        public override float PanicPrior => 0.12f;
        public override TellKind Tell => TellKind.SpeakerCrackle;
        public override float TellLead => 0.9f;
        public override string Chore => "work out which footsteps are real";

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            // Close to where she thinks you are, far from where she actually is.
            Vector3 believed = TacticHelpers.Believed(c);
            Vector3 fake = believed + (believed - c.Body.Position).normalized * 8f;
            plan.Target = c.Where(fake);
            plan.Add(new Tell(TellKind.SpeakerCrackle, _ => fake, TellLead));
            plan.Add(new Effect("footsteps through the speaker near " + plan.Target,
                k => k.World.Pa.PlayNear(fake, ProceduralAudio.Tell(TellKind.Footsteps), 1f)));
        }
    }

    public sealed class PhantomChimeTactic : Tactic
    {
        public override string Id => "phantom_chime";
        public override string Title => "Phantom chime";
        public override GoalId[] Goals => new[] { GoalId.Flush };
        public override int Tier => 1;
        public override int IntroducedShift => 2;
        public override float TensionCost => 0.03f;
        public override float Cooldown => 40f;
        public override float PanicPrior => 0.05f;
        public override TellKind Tell => TellKind.DoorMotor;
        public override float TellLead => 0.9f;
        public override string Chore => "check the doors";
        public override string Attacks => "sound";

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            AutoDoubleDoor door = Object.FindObjectsByType<AutoDoubleDoor>()
                .OrderBy(d => Vector3.Distance(d.transform.position, TacticHelpers.Believed(c)))
                .FirstOrDefault();
            if (door == null) return;
            plan.Target = c.Where(door.transform.position);
            plan.Add(new Tell(TellKind.DoorMotor, _ => door.transform.position, TellLead));
            plan.Add(new Effect("cycle the doors with nobody there", k => k.World.PhantomChime(door)));
        }
    }

    public sealed class SilenceTactic : Tactic
    {
        public override string Id => "silence";
        public override string Title => "Silence";
        public override GoalId[] Goals => new[] { GoalId.Stalk };
        public override int Tier => 1;
        public override int IntroducedShift => 5;
        public override float TensionCost => 0.04f;
        public override float Cooldown => 90f;
        public override float PanicPrior => 0.1f;
        public override TellKind Tell => TellKind.SilenceFalls;
        public override float TellLead => 0.9f;
        public override string Chore => "notice the absence";
        public override string Attacks => "sound";

        public override void Plan(AikoContext c, PlanBuilder plan)
        {
            plan.Target = c.RegionName(c.Belief.PeakRegion);
            plan.Add(new Tell(TellKind.SilenceFalls, k => k.Body.Position, TellLead));
            plan.Add(new SetSilent(true));
            plan.Add(new MoveTo(k => TacticHelpers.Believed(k), AikoBody.Pace.Sneak, "creep toward " + plan.Target, 4f, 35f, follow: true));
            plan.Add(new SetSilent(false));
        }
    }
}
