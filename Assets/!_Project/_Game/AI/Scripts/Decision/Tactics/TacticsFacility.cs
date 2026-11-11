using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Tactics where Karen acts as the building rather than the body: the lights, the fog, the
    // shelves, the bins, the mop, the clock, the doors (Karen.md §8.1–8.3).

    // ================================================================ §8.1 sight

    public sealed class BlackoutTactic : Tactic
    {
        public override string Id => "blackout";
        public override bool LeavesChore => true;
        public override string Title => "Blackout";
        public override ActionId[] Actions => new[] { ActionId.Flush };
        public override int Tier => 3;
        public override int IntroducedShift => 3;
        public override float TensionCost => 0.45f;
        public override float Cooldown => 240f;
        public override float PanicPrior => 0.3f;
        public override TellKind Tell => TellKind.BallastWhine;
        public override float TellLead => 1.2f;
        public override string Chore => "find the torch, cross the maze to the breaker box, reset three breakers low hum to high";
        public override string Attacks => "sight";

        public override bool Available(KarenContext c, out string why)
        {
            why = PowerSystem.PowerOn ? null : "the lights are already out";
            return PowerSystem.PowerOn;
        }

        // The lights she put out are still out: the whole store, or a wing's circuit.
        public override bool EffectRunning(KarenContext c) =>
            !PowerSystem.PowerOn || BreakerPanel.Instance != null && !BreakerPanel.Instance.AllOn;

        // You fix it at the breaker box.
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = default;
            if (!EffectRunning(c) || c.Map == null) return false;
            Landmark? box = c.Map.FindLandmark(LandmarkKind.BreakerBox);
            if (!box.HasValue) return false;
            at = box.Value.Position;
            return true;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
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
            // No wait on the breaker route any more: that's Ambush's to do, and a chain of the two
            // is the combo room's to find (0.2.0).
        }
    }

    // Fog (0.3.0): the fridges of the room she thinks you're in hiss and pour out fog until it
    // fills the room. Nobody in it sees past the visibility (2 m), her and her cameras included;
    // her hearing is unchanged. It lasts until you reset the fridge control box in the backstreet.
    public sealed class FogTactic : Tactic
    {
        public override string Id => "fog";
        public override bool LeavesChore => true;
        public override string Title => "Fog";
        public override ActionId[] Actions => new[] { ActionId.Flush, ActionId.Corner };
        public override int Tier => 2;
        public override int IntroducedShift => 6;
        public override float TensionCost => 0.2f;
        public override float Cooldown => 150f;
        public override float PanicPrior => 0.15f;
        public override TellKind Tell => TellKind.FreezerHiss;
        public override string Chore => "reset the fridge control box in the backstreet, or cross the room blind";
        public override string Attacks => "sight";

        // How sure she has to be that you're in a room to fog it.
        public const float MinMass = 0.25f;

        // The room this plan fogged, for the placement memory's reward.
        public string LastRoom { get; private set; }

        public override bool Available(KarenContext c, out string why)
        {
            if (c.World == null) { why = "no building"; return false; }
            if (FogState.Any) { why = "a room is already fogged"; return false; }
            bool any = Rooms(c).Count > 0;
            why = any ? null : "she doesn't think you're in a room with fridges";
            return any;
        }

        public override bool EffectRunning(KarenContext c)
        {
            foreach (string room in KarenWorld.FogRooms) if (FogState.IsFogged(room)) return true;
            return false;
        }

        // You fix it at the fridge control box, by the breaker box in the backstreet.
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = default;
            if (!EffectRunning(c)) return false;
            if (FogState.ResetBox != null) { at = FogState.ResetBox.position; return true; }
            Landmark? breakers = c.Map != null ? c.Map.FindLandmark(LandmarkKind.BreakerBox) : null;
            if (!breakers.HasValue) return false;
            at = breakers.Value.Position;
            return true;
        }

        // The rooms with fridges where her belief puts you, each scored by how sure she is.
        public static List<PlaceCandidate> Rooms(KarenContext c)
        {
            var result = new List<PlaceCandidate>();
            if (c.World == null || c.Map == null || c.Belief == null) return result;
            foreach (string room in KarenWorld.FogRooms)
            {
                if (c.World.FridgesIn(room).Count == 0) continue;
                float mass = 0f;
                for (int r = 0; r < c.Map.Regions.Count && r < c.Belief.RegionMass.Length; r++)
                    if (StoreMap.AreaAt(c.Map.Regions[r].Centroid) == room) mass += c.Belief.RegionMass[r];
                if (mass >= MinMass) result.Add(new PlaceCandidate(room, Mathf.Clamp01(mass)));
            }
            return result;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            List<PlaceCandidate> rooms = Rooms(c);
            int pick = c.Ledger != null ? c.Ledger.PickPlace(c, this, rooms) : (rooms.Count > 0 ? 0 : -1);
            if (pick < 0) return;
            string room = rooms[pick].Key;
            LastRoom = room;

            Vector3 believed = TacticHelpers.Believed(c);
            Vector3 hiss = believed;
            float best = float.MaxValue;
            foreach (Transform f in c.World.FridgesIn(room))
            {
                float d = Vector3.Distance(f.position, believed);
                if (d < best) { best = d; hiss = f.position; }
            }

            plan.Target = room;
            plan.Add(new Tell(TellKind.FreezerHiss, _ => hiss, 1f));
            plan.Add(new Effect("flood " + room + " from its fridges", k =>
            {
                k.World.FogRoom(room, k.Config.fogVisibility);
                // Paid by how much it rattled you over the next 25 s, as she reads you.
                float before = k.Director != null ? k.Director.Panic : 0f;
                System.Func<float> paid = () => Mathf.Clamp01(0.5f + ((k.Director != null ? k.Director.Panic : before) - before) * 2f);
                k.Brain?.PlaceWatch.Watch(this, room, k.Now, t => t >= 25f ? paid() : (float?)null, _ => paid());
            }));
        }
    }

    // Camera bolt-on (0.3.0): she stands a small camera in a shelf slot, aimed where you usually
    // are (CameraPlacement), and every sighting reaches her wherever she is. It beeps while it
    // sees you. Hands-on: she fits it at the shelf. At most two of hers at once.
    public sealed class CameraBoltOnTactic : Tactic
    {
        public override string Id => "camera_bolt_on";
        public override bool LeavesChore => true;
        public override bool IsHandsOn => true;
        public override string Title => "Camera bolt-on";
        public override ActionId[] Actions => new[] { ActionId.Corner, ActionId.Disrupt };
        public override int Tier => 2;
        public override int IntroducedShift => 8;
        public override float TensionCost => 0.15f;
        public override float Cooldown => 300f;
        public override float PanicPrior => 0.1f;
        public override TellKind Tell => TellKind.Drilling;
        public override float TellLead => 1.5f;
        public override string Chore => "unplug it (hold E), or keep out of its view";
        public override string Attacks => "sight";

        // The camera this plan fitted, and its place, for the placement memory's reward.
        public CctvCamera LastCamera { get; private set; }
        public string LastKey { get; private set; }

        List<CameraPlacement.Option> cached;
        float cachedAt = -999f;

        List<CameraPlacement.Option> Options(KarenContext c)
        {
            // Rays are dear: one look a second is plenty for where a camera should go.
            if (cached == null || Mathf.Abs(c.Now - cachedAt) > 1f) { cached = CameraPlacement.Options(c); cachedAt = c.Now; }
            return cached;
        }

        public override bool Available(KarenContext c, out string why)
        {
            if (c.World == null) { why = "no building"; return false; }
            if (KarenWorld.HerCameras >= KarenWorld.MaxCameras) { why = "two of her cameras are already up"; return false; }
            bool any = Options(c).Count > 0;
            why = any ? null : "no shelf slot that would see where you go";
            return any;
        }

        public override bool EffectRunning(KarenContext c) => KarenWorld.HerCameras > 0;

        // You deal with it at the camera: the one of hers nearest where she thinks you are.
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = default;
            float best = float.MaxValue;
            Vector3 from = c.Belief != null && c.Map != null ? TacticHelpers.Believed(c) : Vector3.zero;
            foreach (CctvCamera cam in CctvCamera.All)
            {
                if (cam == null || !cam.BoltedOn || cam.Dead) continue;
                float d = Vector3.Distance(cam.transform.position, from);
                if (d < best) { best = d; at = cam.transform.position; }
            }
            return best < float.MaxValue;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            List<CameraPlacement.Option> options = Options(c);
            var candidates = new List<PlaceCandidate>();
            foreach (CameraPlacement.Option o in options) candidates.Add(new PlaceCandidate(o.Key, o.Heat));
            int pick = c.Ledger != null ? c.Ledger.PickPlace(c, this, candidates) : (options.Count > 0 ? 0 : -1);
            if (pick < 0) return;
            CameraPlacement.Option chosen = options[pick];
            ShelfSlot slot = chosen.Slot;
            Vector3 front = slot.Outward;
            front.y = 0f;
            Vector3 stand = TacticHelpers.OnFloor(slot.Position + (front.sqrMagnitude > 1e-4f ? front.normalized : Vector3.forward) * 0.7f);

            plan.Target = c.Where(slot.Position) + $" ({chosen.Heat:P0} of your usual spots)";
            plan.Go(stand, KarenBody.Pace.Walk, "walk to the shelf at " + c.Where(slot.Position), 1f);
            plan.Add(new Tell(TellKind.Drilling, k => k.Body.Position, TellLead));
            plan.Add(new HandsOn("fit a camera at " + c.Where(slot.Position), _ => slot.Position, k =>
            {
                if (slot.IsBlocked) { k.Think("CHECK", "the slot is taken"); return; }
                CctvCamera cam = k.World.MountCamera(slot, chosen.Yaw, chosen.Pitch);
                LastCamera = cam;
                LastKey = chosen.Key;
                if (cam == null) return;
                // Paid by how long it saw you before it went dark: half a minute is full marks.
                float seen = 0f;
                k.Brain?.PlaceWatch.Watch(this, chosen.Key, k.Now, t =>
                {
                    if (cam != null) seen = cam.SecondsSeen;
                    return cam == null || cam.Dead ? PlaceRewards.ByTime(seen, 30f) : (float?)null;
                }, _ => PlaceRewards.ByTime(cam != null ? cam.SecondsSeen : seen, 30f));
            }));
            plan.Go(TacticHelpers.Retreat(c), KarenBody.Pace.Walk, "leave", 2.5f);
        }
    }

    // ================================================================ §8.2 work

    // Shelf sweep (0.3.0): she stands right in front of a full bay on your way, waits until she
    // hears nobody near, and knocks 5–15 of its products onto the floor. The task list names the
    // aisle. Hands-on: she has to be at the bay.
    public sealed class ShelfSweepTactic : Tactic
    {
        public override string Id => "shelf_sweep";
        public override bool LeavesChore => true;
        public override bool IsHandsOn => true;
        public override string Title => "Shelf sweep";
        public override ActionId[] Actions => new[] { ActionId.Disrupt, ActionId.Flush };
        public override int Tier => 1;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0.1f;
        public override float Cooldown => 45f;
        public override float PanicPrior => 0.1f;
        public override TellKind Tell => TellKind.ShelfRattle;
        public override string Chore => "restock the bay she emptied, and mind the stock on the floor: it's loud underfoot";
        public override string Attacks => "work";

        public const float MinWalk = 10f, MaxWalk = 30f;   // from where she thinks you are
        public const int MinItems = 5, MaxItems = 15;

        // The bay she swept last, while it's still not full again.
        public ShelfUnit Swept { get; private set; }
        public string SweptKey { get; private set; }

        public override bool Available(KarenContext c, out string why)
        {
            List<Bay> bays = Bays(c, out _, out string check);
            if (check != null) c.Think("CHECK", check);
            why = bays.Count == 0 ? "no full bay 10–30 m from the employee" : null;
            return bays.Count > 0;
        }

        public override bool EffectRunning(KarenContext c) => Swept != null && !Swept.IsFull;

        // You fix it at the bay.
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = default;
            if (!EffectRunning(c)) return false;
            at = TacticHelpers.StandIn(Swept);
            return true;
        }

        public override float ExposureRisk(KarenContext c)
        {
            List<Bay> bays = Bays(c, out _, out _);
            if (bays.Count == 0) return 0f;
            float nearest = float.MaxValue;
            foreach (Bay b in bays) nearest = Mathf.Min(nearest, TacticHelpers.WalkFromBelief(c, TacticHelpers.StandIn(b.Unit)));
            return Mathf.Clamp01(1f - nearest / 25f);
        }

        public static string Key(KarenContext c, Bay b) => $"{b.Section} · {b.Unit.name}";

        // Full bays on the sales floor 10–30 m from where she thinks you are; her guess is best
        // around 18 m and where you spend your time.
        public static List<Bay> Bays(KarenContext c, out List<PlaceCandidate> candidates, out string check)
        {
            check = null;
            var bays = new List<Bay>();
            candidates = new List<PlaceCandidate>();
            if (c.Map == null) return bays;
            foreach (Bay b in c.Map.Bays)
            {
                if (b.Unit == null || !b.Unit.IsFull) continue;
                Vector3 stand = TacticHelpers.StandIn(b.Unit);
                if (StoreMap.AreaAt(stand) != "Sales floor") continue;
                float walk = TacticHelpers.WalkFromBelief(c, stand);
                if (walk < MinWalk || walk > MaxWalk) continue;
                if (TacticHelpers.DenyVetoed(c, TaskManager.TaskKind.Stock, stand, out string why)) { check = why; continue; }
                Region region = c.Map.RegionOf(stand);
                float onYourWay = 1f - Mathf.Abs(walk - 18f) / 12f;
                float habit = c.Ledger != null ? c.Ledger.Habit(region) : 0f;
                bays.Add(b);
                candidates.Add(new PlaceCandidate(Key(c, b), Mathf.Clamp01(0.7f * onYourWay + 0.3f * habit)));
            }
            return bays;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            List<Bay> bays = Bays(c, out List<PlaceCandidate> candidates, out _);
            int pick = c.Ledger != null ? c.Ledger.PickPlace(c, this, candidates) : (bays.Count > 0 ? 0 : -1);
            if (pick < 0) return;
            ShelfUnit target = bays[pick].Unit;
            string key = candidates[pick].Key;
            string aisle = bays[pick].Section;
            Vector3 standIn = TacticHelpers.StandIn(target);
            plan.Target = c.Where(standIn);
            plan.Go(standIn, KarenBody.Pace.Walk, "walk to " + plan.Target, 1.2f);

            // Don't get caught doing it: wait until no employee noise is close by.
            plan.Add(new WaitUntil(k => !k.Brain.HeardPlayerWithin(10f, 3f), 10f, "wait for quiet"));
            plan.Add(new Tell(TellKind.ShelfRattle, _ => target.transform.position, 1f));

            int count = 0;
            var slots = new List<ShelfSlot>();
            plan.Add(new HandsOn("start on " + plan.Target, _ => standIn, k =>
            {
                count = k.Rng.Range(MinItems, MaxItems + 1);
                slots.Clear();
                foreach (ShelfSlot slot in target.Slots) if (slot.isFilled) slots.Add(slot);
                // A seeded shuffle, so the same shift drops the same products.
                for (int i = slots.Count - 1; i > 0; i--)
                {
                    int j = k.Rng.Range(0, i + 1);
                    (slots[i], slots[j]) = (slots[j], slots[i]);
                }
                count = Mathf.Min(count, slots.Count);
            }, FairnessGuard.HandsOnReach));

            int ejected = 0;
            float timer = 0f;
            plan.Add(new Process("knock " + plan.Target + "'s stock off", (k, dt) =>
            {
                // Hands-on the whole way: if she's moved off, she stops.
                if (FairnessGuard.Flat(k.Body.Position, standIn) > FairnessGuard.HandsOnReach) return true;
                timer -= dt;
                if (timer > 0f) return false;
                timer = 0.25f;
                for (int i = 0; i < 3 && ejected < count; i++, ejected++)
                    if (slots[ejected] != null && slots[ejected].isFilled) slots[ejected].Eject();
                return ejected >= count;
            }));
            plan.Add(new Effect("mark sabotage", k =>
            {
                k.Brain.MarkSabotaged(target);
                Swept = target;
                SweptKey = key;
                k.Tasks?.Hint(TaskManager.TaskKind.Stock, aisle);
                // Paid by how long the bay stayed short: restocked at once, 0; two minutes, 1.
                k.Brain?.PlaceWatch.Watch(this, key, k.Now,
                    t => target == null || target.IsFull ? PlaceRewards.ByTime(t, 120f) : (float?)null, _ => 1f);
            }));
            plan.Go(TacticHelpers.Retreat(c), KarenBody.Pace.Walk, "retreat", 2.5f);
        }
    }

    // Spill (0.3.0): the same dirt shoppers leave, dropped at her feet, wherever she chooses on
    // the way she thinks you'll take. Hands-on: she has to stand on the spot.
    public sealed class SpillTactic : Tactic
    {
        public override string Id => "spill";
        public override bool LeavesChore => true;
        public override bool IsHandsOn => true;
        public override string Title => "Spill";
        public override ActionId[] Actions => new[] { ActionId.Disrupt, ActionId.Corner };
        public override int Tier => 1;
        public override int IntroducedShift => 4;
        public override float TensionCost => 0.08f;
        public override float Cooldown => 45f;
        public override float PanicPrior => 0.08f;
        public override TellKind Tell => TellKind.BucketClank;
        public override string Chore => "mop it (3 s standing still), or walk through and leave a trail";
        public override string Attacks => "work";

        public const float MinWalk = 6f, MaxWalk = 30f;

        // The spill she left last, while it's still on the floor.
        public Dirt Left { get; private set; }
        public string LeftKey { get; private set; }

        public override bool Available(KarenContext c, out string why)
        {
            if (c.World == null || c.World.DirtPrefab == null) { why = "no spill to make"; return false; }
            bool any = Spots(c, out _).Count > 0;
            why = any ? null : "nowhere on your way to put one";
            return any;
        }

        public override bool EffectRunning(KarenContext c) => Left != null;

        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = Left != null ? Left.transform.position : default;
            return Left != null;
        }

        // Necks and chokepoints you're likely to cross next, 6–30 m off: best around 14 m, and
        // where you've walked before.
        // `reach` also asks the NavMesh that she can walk to each (the plan does; the gate, every
        // decision, doesn't need to).
        // Spots the test player couldn't get near enough to mop (0.4.0, C0.2's fence): 0.3.0's
        // ablation and trick check left Office/G's spill unmopped, and a chore nobody can do is a
        // shift that never ends. The cause is in the scene (not in git); this keeps her off it.
        public static readonly string[] NoSpill = { "Office/G" };

        public static List<Region> Spots(KarenContext c, out List<PlaceCandidate> candidates, bool reach = false)
        {
            var spots = new List<Region>();
            candidates = new List<PlaceCandidate>();
            if (c.Map == null) return spots;
            foreach (Region r in c.Map.Regions)
            {
                if (!r.IsChokepoint || r.IsDoor || !TacticHelpers.Hers(c.Map, r)) continue;
                if (System.Array.IndexOf(NoSpill, r.Name) >= 0) continue;
                float walk = TacticHelpers.WalkFromBelief(c, r.Centroid);
                if (walk < MinWalk || walk > MaxWalk) continue;
                if (reach && !TacticHelpers.CanReach(c, TacticHelpers.OnFloor(r.Centroid), 1f)) continue;
                float onYourWay = 1f - Mathf.Abs(walk - 14f) / 16f;
                float habit = c.Ledger != null ? c.Ledger.Habit(r) : 0f;
                spots.Add(r);
                candidates.Add(new PlaceCandidate(r.Name, Mathf.Clamp01(0.7f * onYourWay + 0.3f * habit)));
            }
            return spots;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            List<Region> spots = Spots(c, out List<PlaceCandidate> candidates, reach: true);
            int pick = c.Ledger != null ? c.Ledger.PickPlace(c, this, candidates) : (spots.Count > 0 ? 0 : -1);
            if (pick < 0) return;
            Region spot = spots[pick];
            string key = candidates[pick].Key;
            Vector3 at = TacticHelpers.OnFloor(spot.Centroid);
            plan.Target = spot.Name;
            if (TacticHelpers.DenyVetoed(c, TaskManager.TaskKind.Mop, at, out string check)) { c.Think("CHECK", check); return; }

            plan.Go(at, KarenBody.Pace.Walk, "go to " + plan.Target, 1f);
            plan.Add(new Tell(TellKind.BucketClank, k => k.Body.Position, 1f));
            plan.Add(new HandsOn("kick over a bucket at " + plan.Target, _ => at, k =>
            {
                Dirt dirt = k.World.Spill(TacticHelpers.WhereYouCanMop(k.Body.Position));
                Left = dirt;
                LeftKey = key;
                if (dirt == null) return;
                // Paid by whether you walked through it (wet footprints from it) or went round.
                Vector3 spot = dirt.transform.position;
                float since = Time.time;
                System.Func<bool> walked = () =>
                {
                    foreach (Trace tr in TraceRegistry.All)
                        if (tr.Kind == TraceKind.WetFootprint && tr.Created >= since && FairnessGuard.Flat(tr.Position, spot) < 3f) return true;
                    return false;
                };
                k.Brain?.PlaceWatch.Watch(this, key, k.Now,
                    t => walked() ? 1f : dirt == null ? 0.3f + 0.4f * PlaceRewards.ByTime(t, 90f) : (float?)null,
                    _ => walked() ? 1f : 0.7f);
            }));
            plan.Go(TacticHelpers.Retreat(c), KarenBody.Pace.Walk, "retreat", 2.5f);
        }
    }

    public sealed class BinTamperTactic : Tactic
    {
        public override string Id => "bin_tamper";
        public override bool LeavesChore => true;
        public override bool IsHandsOn => true;
        public override string Title => "Bin tamper";
        public override ActionId[] Actions => new[] { ActionId.Disrupt };
        public override int Tier => 1;
        public override int IntroducedShift => 4;
        public override float TensionCost => 0.06f;
        public override float Cooldown => 60f;
        public override float PanicPrior => 0.06f;
        public override TellKind Tell => TellKind.LidClatter;
        public override string Chore => "bag the bin she refilled and take it out back again";
        public override string Attacks => "work";

        Trashcan bin;

        // The bin she refilled, while it still needs emptying.
        public Trashcan Refilled { get; private set; }

        public override bool EffectRunning(KarenContext c) => Refilled != null && Refilled.UsageCount > 0;

        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = Refilled != null ? Refilled.transform.position : default;
            return EffectRunning(c);
        }

        public override bool Available(KarenContext c, out string why)
        {
            bin = null;
            foreach (Trashcan t in Trashcan.All)
            {
                if (t == null || t.UsageCount != 0) continue;
                // Only the bins in her part of the building (not the street's, nor the yard's).
                if (!StoreMap.KarenWorksIn(StoreMap.AreaAt(t.transform.position))) continue;
                if (TacticHelpers.WalkFromBelief(c, t.transform.position) > 10f) { bin = t; break; }
            }
            why = bin == null ? "no emptied bin out of sight" : null;
            return bin != null;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            if (bin == null) return;
            Trashcan target = bin;
            plan.Target = c.Where(TacticHelpers.OnFloor(target.transform.position));
            if (TacticHelpers.DenyVetoed(c, TaskManager.TaskKind.Trash, target.transform.position, out string check)) { c.Think("CHECK", check); return; }
            plan.Go(TacticHelpers.OnFloor(target.transform.position), KarenBody.Pace.Walk, "go to the bin", 1.2f);
            plan.Add(new Tell(TellKind.LidClatter, _ => target.transform.position, 0.9f));
            plan.Add(new HandsOn("refill " + plan.Target, _ => target.transform.position, k =>
            {
                for (int i = 0; i < 3; i++) target.RegisterUse();
                Refilled = target;
            }));
        }
    }

    // Tool theft (0.3.0): she walks to the WC, takes the WC mop off its rack, carries it off and
    // leaves it somewhere you don't go. The rack won't recall it, and the task list says "Return
    // the WC mop" until it's back. She never touches the staff room's mop. Hands-on at both ends.
    public sealed class ToolTheftTactic : Tactic
    {
        public override string Id => "tool_theft";
        public override bool LeavesChore => true;
        public override bool IsHandsOn => true;
        public override string Title => "Tool theft";
        public override ActionId[] Actions => new[] { ActionId.Disrupt };
        public override int Tier => 2;
        public override int IntroducedShift => 5;
        public override float TensionCost => 0.1f;
        public override float Cooldown => 180f;
        public override float PanicPrior => 0.08f;
        public override TellKind Tell => TellKind.MopRattle;
        public override string Chore => "find the WC mop and carry it back to its rack";
        public override string Attacks => "work";

        public const string Rack = "WC";                 // the only rack she takes from
        public const float MinCarry = 15f, MaxCarry = 70f;

        // Where she left it, for the placement memory's reward.
        public string LastKey { get; private set; }

        public static ToolSnapPoint WcRack()
        {
            foreach (ToolSnapPoint s in Object.FindObjectsByType<ToolSnapPoint>())
                if (s != null && s.place == Rack && s.tool != null && s.tool.type == ItemType.Mop) return s;
            return null;
        }

        public override bool Available(KarenContext c, out string why)
        {
            ToolSnapPoint rack = WcRack();
            if (rack == null) { why = "no WC mop rack"; return false; }
            if (rack.Taken) { why = "she already has it hidden"; return false; }
            if (!rack.IsToolHome) { why = "the WC mop isn't on its rack"; return false; }
            if (c.Belief != null && TacticHelpers.WalkFromBelief(c, rack.transform.position) < 12f) { why = "the employee is near the rack"; return false; }
            if (!InReach(rack)) { why = "she can't stand within reach of the rack"; return false; }
            bool any = Drops(c, rack.transform.position, out _).Count > 0;
            why = any ? null : "nowhere to leave it";
            return any;
        }

        public override bool EffectRunning(KarenContext c) => ToolSnapPoint.TakenCount > 0;

        // Where she stands to take it: the nearest spot on her floor to the rack, which hangs on
        // a wall. Walking to "the floor under the rack" left her 2.2 m off on some approaches, just
        // out of rule 9's reach, and the take failed (the trick check, 8 November).
        public const float StandArrive = 0.35f;
        public static Vector3 StandAt(ToolSnapPoint rack) => TacticHelpers.OnHerFloor(rack.transform.position, 2.5f);

        public static bool InReach(ToolSnapPoint rack) =>
            FairnessGuard.Flat(StandAt(rack), rack.transform.position) + StandArrive <= FairnessGuard.HandsOnReach - 0.1f;

        // You fix it at the mop, wherever she left it.
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = default;
            foreach (ToolSnapPoint rack in ToolSnapPoint.TakenRacks)
                if (rack != null && rack.tool != null) { at = rack.tool.transform.position; return true; }
            return false;
        }

        // Parts of the sales floor and the stockroom 15–70 m from the rack, guessed best where you
        // spend least time and far from where she thinks you are.
        public static List<Region> Drops(KarenContext c, Vector3 rack, out List<PlaceCandidate> candidates)
        {
            var regions = new List<Region>();
            candidates = new List<PlaceCandidate>();
            if (c.Map == null) return regions;
            foreach (Region r in c.Map.Regions)
            {
                if (r.IsDoor || r.Cells.Count < 4 || !TacticHelpers.Hers(c.Map, r)) continue;
                if (r.Area != "Sales floor" && r.Area != "Stockroom") continue;
                float carry = Vector3.Distance(r.Centroid, rack);
                if (carry < MinCarry || carry > MaxCarry) continue;
                float unvisited = 1f - (c.Ledger != null ? c.Ledger.Habit(r) : 0f);
                float away = c.Belief != null ? Mathf.Clamp01(TacticHelpers.WalkFromBelief(c, r.Centroid) / 40f) : 0.5f;
                regions.Add(r);
                candidates.Add(new PlaceCandidate(r.Name, Mathf.Clamp01(0.6f * unvisited + 0.4f * away)));
            }
            return regions;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            ToolSnapPoint home = WcRack();
            if (home == null) return;
            Item mop = home.tool;
            List<Region> drops = Drops(c, home.transform.position, out List<PlaceCandidate> candidates);
            int pick = c.Ledger != null ? c.Ledger.PickPlace(c, this, candidates) : (drops.Count > 0 ? 0 : -1);
            if (pick < 0) return;
            Region drop = drops[pick];
            string key = candidates[pick].Key;
            Vector3 dropAt = TacticHelpers.OnFloor(drop.Centroid);
            Vector3 rackAt = home.transform.position;
            plan.Target = "the WC mop → " + drop.Name;

            plan.Go(StandAt(home), KarenBody.Pace.Walk, "go to the WC mop rack", StandArrive);
            plan.Add(new Tell(TellKind.MopRattle, _ => rackAt, 0.9f));
            plan.Add(new HandsOn("take the WC mop", _ => rackAt, k =>
            {
                if (!home.IsToolHome) { k.Think("CHECK", "the WC mop is gone already"); return; }
                k.Body.Carry(mop);
                home.MarkTaken(true);
                LastKey = key;
                // Paid by how long it took you to bring it back: at once, 0; two and a half minutes, 1.
                k.Brain?.PlaceWatch.Watch(this, key, k.Now, t =>
                {
                    if (home != null && home.Taken) return null;
                    k.Brain?.Answered("mop_returned", t, "the WC mop back on its rack");
                    return PlaceRewards.ByTime(t, 150f);
                }, _ => 1f);
            }));
            plan.Go(dropAt, KarenBody.Pace.Walk, "carry it to " + drop.Name, 1.2f);
            plan.Add(new HandsOn("leave the WC mop in " + drop.Name, _ => dropAt, k =>
            {
                if (k.Body.Carried != mop) return;
                k.Body.PutDown(mop);
                TraceRegistry.Add(TraceKind.MopAway, mop.transform.position, float.PositiveInfinity, default, mop, mop.transform);
            }));
        }
    }

    // The overtime (0.3.0): in the shift's last two minutes she walks to the time clock in the
    // staff room and stops it. The shift's countdown stops with it — customers keep coming — and
    // the task list gains "Restart the time clock" until you restart it there. Once a shift.
    public sealed class OvertimeTactic : Tactic
    {
        public override string Id => "overtime";
        public override bool LeavesChore => true;
        public override bool IsHandsOn => true;
        public override string Title => "The overtime";
        public override ActionId[] Actions => new[] { ActionId.Disrupt };
        public override int Tier => 4;
        public override int IntroducedShift => 10;
        public override float TensionCost => 0.3f;
        // Once a shift (UsesThisShift). Not a huge cooldown: LastUsed starts at −9999 s, so
        // 99999 s kept it on cooldown for the first 25 hours, and it never ran in play.
        public override float Cooldown => 0f;
        public override float PanicPrior => 0.3f;
        public override TellKind Tell => TellKind.PunchBuzz;
        public override string Chore => "restart the time clock in the staff room";
        public override string Attacks => "you";

        public const float LastSeconds = 120f;   // the shift's last two minutes (build plan D2)

        public override bool Available(KarenContext c, out string why)
        {
            why = null;
            if (UsesThisShift > 0) { why = "once a shift"; return false; }
            if (c.Shift == null || !c.Shift.IsShiftActive) { why = "no shift running"; return false; }
            if (c.Shift.IsFrozen) { why = "the clock is already stopped"; return false; }
            if (c.Shift.TimeRemaining > LastSeconds || c.Shift.TimeRemaining <= 0f) { why = "not the shift's last two minutes"; return false; }
            if (!Clock(c, out _)) { why = "no time clock"; return false; }
            return true;
        }

        public override bool EffectRunning(KarenContext c) => c.Shift != null && c.Shift.IsFrozen;

        // You fix it at the time clock.
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = default;
            return EffectRunning(c) && Clock(c, out at);
        }

        static bool Clock(KarenContext c, out Vector3 at)
        {
            at = default;
            Landmark? clock = c.Map != null ? c.Map.FindLandmark(LandmarkKind.TimeClock) : null;
            if (!clock.HasValue) return false;
            at = clock.Value.Position;
            return true;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            if (!Clock(c, out Vector3 clock)) return;
            plan.Target = "the time clock";
            plan.Go(TacticHelpers.OnFloor(clock), KarenBody.Pace.Walk, "walk to the time clock", 1.2f);
            plan.Add(new Tell(TellKind.PunchBuzz, _ => clock, 0.9f));
            plan.Add(new HandsOn("stop the time clock", _ => clock, k =>
            {
                if (k.Shift == null || !k.Shift.Freeze()) { k.Think("CHECK", "the clock was past stopping"); return; }
                OneShotAudio.PlayAt(ProceduralAudio.ClockStop(), clock, 0.9f, SoundKind.Karen);
                k.Brain?.ClockStopped(clock);
            }));
            plan.Add(new Speak(k => $"{TacticHelpers.PlayerName(k)}, the time clock is out of order. Please continue working.", false));
            plan.Go(TacticHelpers.Retreat(c), KarenBody.Pace.Walk, "leave the staff room", 2.5f);
        }
    }

    // ================================================================ §8.3 sound

    // Phantom chime (0.3.0): the front doors near where she thinks you are slide open and shut,
    // chiming, for 20 s, with nobody there.
    public sealed class PhantomChimeTactic : Tactic
    {
        public override string Id => "phantom_chime";
        public override bool LeavesChore => true;
        public override string Title => "Phantom chime";
        public override ActionId[] Actions => new[] { ActionId.Flush };
        public override int Tier => 1;
        public override int IntroducedShift => 2;
        public override float TensionCost => 0.03f;
        public override float Cooldown => 40f;
        public override float PanicPrior => 0.05f;
        public override TellKind Tell => TellKind.DoorMotor;
        public override float TellLead => 0.9f;
        public override string Chore => "check the front doors, or don't";
        public override string Attacks => "sound";

        public const float MaxWalk = 60f;   // doors further than this from you won't be heard as a threat

        // The doors this plan cycled, for the placement memory's reward.
        public AutoDoubleDoor LastDoor { get; private set; }

        public override bool Available(KarenContext c, out string why)
        {
            if (c.World == null) { why = "no building"; return false; }
            if (c.World.PhantomRunning) { why = "the doors are already cycling"; return false; }
            bool any = Doors(c).Count > 0;
            why = any ? null : "no front doors near you";
            return any;
        }

        public override bool EffectRunning(KarenContext c) => c.World != null && c.World.PhantomRunning;

        static readonly List<AutoDoubleDoor> doors = new List<AutoDoubleDoor>();

        // The pairs of doors near where she thinks you are, nearer ones guessed better.
        public static List<PlaceCandidate> Doors(KarenContext c)
        {
            doors.Clear();
            var result = new List<PlaceCandidate>();
            Vector3 believed = TacticHelpers.Believed(c);
            foreach (AutoDoubleDoor d in Object.FindObjectsByType<AutoDoubleDoor>())
            {
                float near = Vector3.Distance(d.transform.position, believed);
                if (near > MaxWalk) continue;
                doors.Add(d);
                result.Add(new PlaceCandidate(DoorKey(c, d), Mathf.Clamp01(1f - near / MaxWalk)));
            }
            return result;
        }

        static string DoorKey(KarenContext c, AutoDoubleDoor d) =>
            c.Map != null ? c.Map.NameAt(d.transform.position) + " doors" : d.name;

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            List<PlaceCandidate> candidates = Doors(c);
            int pick = c.Ledger != null ? c.Ledger.PickPlace(c, this, candidates) : (candidates.Count > 0 ? 0 : -1);
            if (pick < 0) return;
            AutoDoubleDoor door = doors[pick];
            LastDoor = door;
            plan.Target = candidates[pick].Key;
            plan.Add(new Tell(TellKind.DoorMotor, _ => door.transform.position, TellLead));
            string key = candidates[pick].Key;
            plan.Add(new Effect("cycle " + plan.Target + " for 20 s with nobody there", k =>
            {
                k.World.PhantomChime(door, KarenWorld.PhantomSeconds);
                // Paid by whether you went to look: does she think you're by the doors 30 s on?
                System.Func<float> paid = () => door != null && k.Belief != null &&
                    Vector3.Distance(TacticHelpers.Believed(k), door.transform.position) < 12f ? 1f : 0f;
                k.Brain?.PlaceWatch.Watch(this, key, k.Now, t => t >= 30f ? paid() : (float?)null, _ => paid());
            }));
        }
    }
}
