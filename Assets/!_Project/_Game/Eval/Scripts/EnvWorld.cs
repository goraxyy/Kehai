using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Eval
{
    // What an agent can see and name (IDEAS.md, the agent test environment).
    //
    // Text first, pixels later: a serialisable snapshot that isolates *planning* failure
    // from *perception* failure. Every object an action can target gets a stable id for the
    // episode — spill_3, bay_41, cust_7, bin_2, mop — and every place is named by the store
    // map, so an agent reasons over "Aisle 2/B", not coordinates.
    //
    // Fair by construction: the agent sees the HUD's task list (which Karen can falsify),
    // Karen only when she is in its view cone with a clear line of sight, and her footsteps
    // only when they're close enough to hear. It hears her warnings (tells) as a player would
    // — near ones within TellFar, the PA and blackouts anywhere — and notices what a person
    // would of her blink dash: that she was suddenly nearer, that she was gone, that she
    // stepped out. It is told nothing a player couldn't know.
    public sealed class EnvWorld
    {
        readonly Dictionary<Object, string> ids = new Dictionary<Object, string>();
        readonly Dictionary<string, Object> objects = new Dictionary<string, Object>();
        int nextSpill = 1, nextCustomer = 1, nextBag = 1, nextChore = 1;
        float[] fromPlayer;
        long noiseCursor;
        readonly List<NoiseEvent> noises = new List<NoiseEvent>();
        float heardKarenAt = -99f, stepsNotedAt = -99f;

        // Everything of hers the employee has noticed lately: tells, footsteps, dash cues.
        public struct Heard
        {
            public int Id;
            public float Time;
            public string Kind, Category;
            public Vector3 At;
        }
        readonly List<Heard> heard = new List<Heard>();
        int nextHeard = 1;
        const float HeardFor = 5f;

        // Her last sighting, for the dash cues.
        bool sawHer;
        float lastSeenAt = -99f, lastSeenDistance;
        Vector3 lastSeenPosition;

        public StoreMap Map => StoreMap.Current;

        public void Reset()
        {
            ids.Clear();
            objects.Clear();
            nextSpill = nextCustomer = nextBag = 1;
            noiseCursor = NoiseBus.Latest;
            heard.Clear();
            sawHer = false;
            lastSeenAt = heardKarenAt = stepsNotedAt = -99f;
        }

        // ---- what a person notices of her ----------------------------------------------------

        // A warning, as KarenBrain.Told reports it (KehaiEnv subscribes). Heard if it carries
        // that far: the PA, the HUD and a blackout reach everywhere; the rest, TellFar.
        public void OnTold(TellKind kind, Vector3 at, float lead)
        {
            PlayerPresence me = PlayerPresence.Current;
            if (me == null) return;
            if (!Storewide(kind) && Vector3.Distance(at, me.Position) > TellFar) return;
            Note("tell:" + kind.ToString().ToLowerInvariant(), TellCategory(kind), at);
        }

        public const float TellFar = 60f;   // as KarenWorld plays a tell

        public static bool Storewide(TellKind kind) =>
            kind == TellKind.PaChime || kind == TellKind.HoldMusic || kind == TellKind.BallastWhine;

        // What kind of warning it is, as a person would class it.
        public static string TellCategory(TellKind kind)
        {
            switch (kind)
            {
                case TellKind.BallastWhine:
                    return "light";
                case TellKind.PaChime:
                case TellKind.HoldMusic:
                    return "pa";
                case TellKind.PunchBuzz:
                    return "hud";
                case TellKind.CustomerFreeze:
                    return "social";
                case TellKind.Screech:
                case TellKind.EyeFlicker:
                case TellKind.RevealSting:
                    return "her";
                default:
                    return "sound";
            }
        }

        void Note(string kind, string category, Vector3 at)
        {
            heard.Add(new Heard { Id = nextHeard++, Time = Time.time, Kind = kind, Category = category, At = at });
            heard.RemoveAll(h => Time.time - h.Time > HeardFor);
        }

        // Whether the employee can see her: in the view cone, nothing solid in between.
        public static bool CanSee(PlayerPresence me, KarenBrain brain, out float distance)
        {
            distance = 0f;
            if (me == null || brain == null || brain.Body == null) return false;
            Vector3 her = brain.Body.Position + Vector3.up * 1.4f;
            Vector3 d = her - me.Head;
            distance = d.magnitude;
            if (distance > Mathf.Min(35f, FogState.SightLimit(me.Head, her)) || Vector3.Angle(me.Facing, d) > 55f) return false;   // fog blinds you too
            return !Physics.Linecast(me.Head, her, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                   || hit.collider.transform.IsChildOf(brain.Body.transform);
        }

        // A throw of hers, as a person sees it coming: what it is, how soon it's here, where it
        // is, and which way to step to be off its line (left or right of where you face).
        public struct ThrowSeen
        {
            public int Id;
            public string Item;
            public float Eta;
            public Vector3 At;
            public string StepTo;
        }

        // A throw you can see coming (0.2.0): in view, nothing in the way, and on a line that
        // passes within reach of you. Thrown from behind you, you don't see it.
        public static bool SeesThrow(PlayerPresence me, out ThrowSeen seen)
        {
            seen = default;
            if (me == null) return false;
            foreach (Projectile p in Projectile.Flying)
            {
                if (p == null) continue;
                Vector3 at = p.transform.position, d = at - me.Head;
                if (d.magnitude > Mathf.Min(35f, FogState.SightLimit(me.Head, at)) || Vector3.Angle(me.Facing, d) > 55f) continue;
                if (!Incoming(me.Position, at, p.Velocity, out float eta)) continue;
                if (Physics.Linecast(me.Head, at, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && !me.Owns(hit.collider)) continue;
                seen = new ThrowSeen
                {
                    Id = p.Number, Item = p.Item.ToString().ToLowerInvariant(), Eta = eta, At = at,
                    StepTo = StepAside(me.Position, me.transform.forward, at, p.Velocity),
                };
                return true;
            }
            return false;
        }

        // Off the line of something flying at you: the side of you it passes further from,
        // as left or right of `facing`. Dead on, right.
        public static string StepAside(Vector3 me, Vector3 facing, Vector3 at, Vector3 velocity)
        {
            Vector3 v = velocity, rel = me - at;
            v.y = rel.y = facing.y = 0f;
            Vector3 off = v.sqrMagnitude > 1e-4f ? rel - v * (Vector3.Dot(rel, v) / v.sqrMagnitude) : Vector3.zero;
            Vector3 right = Vector3.Cross(Vector3.up, facing);
            if (off.sqrMagnitude < 0.01f) return "right";
            return Vector3.Dot(off, right) >= 0f ? "right" : "left";
        }

        // Whether something at `at`, flying with `velocity`, passes within reach of `me` on the
        // floor plan, and in how many seconds it's nearest.
        public static bool Incoming(Vector3 me, Vector3 at, Vector3 velocity, out float eta)
        {
            eta = 0f;
            Vector3 v = velocity, rel = me - at;
            v.y = rel.y = 0f;
            float speed2 = v.sqrMagnitude;
            if (speed2 < 0.25f) return false;
            eta = Vector3.Dot(rel, v) / speed2;
            if (eta <= 0f) return false;                           // going away
            return (rel - v * eta).magnitude < ThrowReach;
        }

        public const float ThrowReach = 1.5f;   // a line closer than this to you looks like it's coming at you

        // Is that spot in view, and clear? (Where she was a moment ago, to tell a vanish from
        // her simply walking behind a shelf or you turning away.)
        static bool SpotInView(PlayerPresence me, Vector3 spot)
        {
            Vector3 p = spot + Vector3.up * 1.4f;
            Vector3 d = p - me.Head;
            if (d.magnitude > Mathf.Min(35f, FogState.SightLimit(me.Head, p)) || Vector3.Angle(me.Facing, d) > 50f) return false;
            return !Physics.Linecast(me.Head, p, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) || (hit.point - p).magnitude < 0.8f;
        }

        string Id(Object o, string prefix, ref int counter)
        {
            if (o == null) return null;
            if (ids.TryGetValue(o, out string id)) return id;
            id = prefix + "_" + counter++;
            ids[o] = id;
            objects[id] = o;
            return id;
        }

        public string SpillId(Dirt d) => Id(d, "spill", ref nextSpill);
        public string ChoreId(Object o, string kind) => Id(o, kind, ref nextChore);
        public string CustomerId(CustomerNPC c) => Id(c, "cust", ref nextCustomer);
        public string BagId(TrashBag b) => Id(b, "bag", ref nextBag);

        public static string BayId(int index) => "bay_" + index;
        public static string BinId(int index) => "bin_" + (index + 1);

        public static string LandmarkId(Landmark l) =>
            l.Name.ToLowerInvariant().Replace(' ', '_').Replace(":", "").Replace("-", "");

        // ---- resolving targets ----------------------------------------------------------

        public T Resolve<T>(string id) where T : Object
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (objects.TryGetValue(id, out Object o)) return o as T;

            if (id.StartsWith("bay_") && int.TryParse(id.Substring(4), out int bay) && bay >= 0 && bay < Map.Bays.Count)
                return Map.Bays[bay].Unit as T;
            if (id.StartsWith("bin_") && int.TryParse(id.Substring(4), out int bin))
            {
                var bins = Bins();
                return bin - 1 >= 0 && bin - 1 < bins.Count ? bins[bin - 1] as T : null;
            }

            switch (id)
            {
                case "mop": return Tool(ItemType.Mop) as T;
                case "crate": return Tool(ItemType.Stock) as T;
                case "flashlight": return Object.FindAnyObjectByType<Flashlight>()?.GetComponent<Item>() as T;
            }

            foreach (Landmark l in Map.Landmarks)
                if (LandmarkId(l) == id) return l.Source as T;
            return null;
        }

        public bool TryPosition(string id, out Vector3 position)
        {
            position = default;
            if (string.IsNullOrEmpty(id)) return false;

            Object o = Resolve<Object>(id);
            if (o is Component c) { position = o is ShelfUnit u ? TacticHelpers.StandIn(u) : c.transform.position; return true; }
            if (o is GameObject g) { position = g.transform.position; return true; }

            foreach (Landmark l in Map.Landmarks)
                if (LandmarkId(l) == id) { position = l.Position; return true; }

            foreach (Region r in Map.Regions)
                if (string.Equals(r.Name, id, System.StringComparison.OrdinalIgnoreCase)) { position = r.Centroid; return true; }
            return false;
        }

        public static List<Trashcan> Bins() => Trashcan.All.OrderBy(t => t.transform.position.x).ThenBy(t => t.transform.position.z).ToList();

        public static Item Tool(ItemType type)
        {
            // The staff room's mop first: the WC's is hers to take (0.3.0, Tool theft).
            foreach (ToolSnapPoint s in Object.FindObjectsByType<ToolSnapPoint>())
                if (s.tool != null && s.tool.type == type && IsTool(s.tool) && s.place != ToolTheftTactic.Rack && !s.Taken) return s.tool;
            foreach (ToolSnapPoint s in Object.FindObjectsByType<ToolSnapPoint>())
                if (s.tool != null && s.tool.type == type && IsTool(s.tool)) return s.tool;
            foreach (Item i in Object.FindObjectsByType<Item>())
                if (i.type == type && IsTool(i)) return i;
            return null;
        }

        // The back rooms' product boxes are Stock items too (7f90835); "crate" is the stock
        // crate, which restocks a whole bay. Without this the crate could resolve to any box,
        // often one with no path to it, and a box only fills its own product's slots.
        static bool IsTool(Item i) => i.type != ItemType.Stock || i.GetComponent<StockCrate>() != null;

        // ---- observing ---------------------------------------------------------------------

        float Walk(Vector3 p)
        {
            int cell = Map.CellAt(p, 4f);
            if (cell < 0 || fromPlayer == null) return -1f;
            float d = fromPlayer[cell];
            return float.IsInfinity(d) ? -1f : Mathf.Round(d * 10f) / 10f;
        }

        // ---- her chores (0.3.0) -----------------------------------------------------------------

        // What her tricks left you to fix, and where, as the task list and your eyes would tell you:
        // the fog box, the stopped time clock, the WC mop she hid, a box behind a door, Kenji, and
        // any camera of hers still running.
        public List<object> Chores()
        {
            var chores = new List<object>();
            void Add(string kind, string id, Vector3 at) => chores.Add(new Dictionary<string, object>
            {
                ["kind"] = kind, ["id"] = id, ["region"] = Map.NameAt(at), ["walk_m"] = Walk(at),
            });

            if (FogState.Any)
            {
                Vector3? box = FogState.ResetBox != null ? FogState.ResetBox.position : Map.FindLandmark(LandmarkKind.BreakerBox)?.Position;
                if (box.HasValue) Add("fog", "fog_box", box.Value);
            }
            ShiftManager shift = Object.FindAnyObjectByType<ShiftManager>();
            Landmark? clock = Map.FindLandmark(LandmarkKind.TimeClock);
            if (shift != null && shift.IsFrozen && clock.HasValue) Add("clock", "time_clock", clock.Value.Position);
            // 0.4.1: closing time's announcement at the PA microphone, and the cameras she broke.
            if (shift != null && shift.AwaitingAnnouncement)
                Add("closing", "pa_microphone", ClosingMic.Instance != null ? ClosingMic.Instance.transform.position : clock?.Position ?? Vector3.zero);
            if (StoreCameras.Instance != null && StoreCameras.Instance.Broken && StoreCameras.Instance.Box != null)
                Add("cameras", "camera_box", StoreCameras.Instance.Box.transform.position);
            foreach (ToolSnapPoint rack in ToolSnapPoint.TakenRacks)
                if (rack != null && rack.tool != null) Add("lost_tool", ChoreId(rack.tool, "tool"), rack.tool.transform.position);
            foreach (HingeDoor door in HingeDoor.All)
                if (door != null && door.Blocker != null && door.Blocker.IsBlocking && door.Blocker.Box != null)
                    Add("blocked_door", ChoreId(door, "door"), door.Blocker.Box.transform.position);
            if (Understudy.Current != null && !Understudy.Current.Leaving)
            {
                objects["kenji"] = Understudy.Current;
                Add("kenji", "kenji", Understudy.Current.transform.position);
            }
            foreach (CctvCamera cam in CctvCamera.All)
                if (cam != null && cam.BoltedOn && !cam.Dead) Add("camera", ChoreId(cam, "camera"), cam.transform.position);
            return chores;
        }

        public Dictionary<string, object> Observe(KehaiEnv env)
        {
            PlayerPresence me = PlayerPresence.Current;
            Vector3 here = me != null ? me.Position : Vector3.zero;
            int myCell = Map.CellAt(here, 4f);
            fromPlayer = myCell >= 0 ? Map.Distances(myCell, float.MaxValue, fromPlayer) : null;

            var o = new Dictionary<string, object>();
            ShiftManager shift = env.Shift;
            o["t"] = Round(env.ShiftSeconds);
            o["shift"] = shift != null ? shift.ShiftNumber : 0;
            o["on_shift"] = shift != null && shift.IsShiftActive;
            o["time_remaining"] = shift != null ? Round(shift.TimeRemaining) : 0f;
            o["store_open"] = shift != null && shift.CustomersAllowed;

            BurnoutSystem burnout = env.Burnout;
            CarrySlot carry = env.Driver != null ? env.Driver.Carry : null;
            o["you"] = new Dictionary<string, object>
            {
                ["region"] = Map.NameAt(here),
                ["at"] = new[] { Round(here.x), Round(here.z) },
                ["energy"] = burnout != null ? Round(burnout.Energy01) : 1f,
                ["can_sprint"] = burnout == null || burnout.CanSprint,
                ["holding"] = carry != null && carry.IsCarrying ? ItemName(carry.currentItem) : null,
                ["inventory"] = carry != null ? carry.items.Select(i => i != null ? ItemName(i) : null).ToList() : new List<string>(),
                ["active_slot"] = carry != null ? carry.activeSlot : 0,
                ["held_by_karen"] = Consequences.PlayerHeld
            };

            // The task list, as the player sees it.
            var tasks = new List<object>();
            if (env.Tasks != null && env.Tasks.HasTasks)
                foreach (TaskManager.ShiftTask t in env.Tasks.Tasks)
                    tasks.Add(new Dictionary<string, object> { ["kind"] = t.Kind.ToString(), ["label"] = t.Label, ["done"] = t.IsComplete, ["detail"] = t.Detail });
            o["hud_tasks"] = tasks;

            o["spills"] = Dirt.All.Where(d => d != null).Select(d => (object)new Dictionary<string, object>
            {
                ["id"] = SpillId(d), ["region"] = Map.NameAt(d.transform.position), ["walk_m"] = Walk(d.transform.position)
            }).ToList();

            var bays = new List<object>();
            for (int i = 0; i < Map.Bays.Count; i++)
            {
                Bay b = Map.Bays[i];
                if (b.Unit == null || b.Unit.IsFull) continue;
                int empty = b.Unit.EmptyCount;
                Vector3 stand = TacticHelpers.StandIn(b.Unit);
                bays.Add(new Dictionary<string, object> { ["id"] = BayId(i), ["section"] = b.Section, ["empty_slots"] = empty, ["region"] = Map.NameAt(stand), ["walk_m"] = Walk(stand) });
            }
            o["shelves_to_restock"] = bays;

            var bins = Bins();
            o["bins"] = bins.Select((t, i) => (object)new Dictionary<string, object>
            {
                ["id"] = BinId(i), ["fill"] = t.UsageCount, ["capacity"] = t.capacity, ["region"] = Map.NameAt(t.transform.position), ["walk_m"] = Walk(t.transform.position)
            }).ToList();

            o["bags"] = Object.FindObjectsByType<TrashBag>().Where(b => !b.IsDisposed).Select(b => (object)new Dictionary<string, object>
            {
                ["id"] = BagId(b), ["carried"] = b.GetComponent<Item>() != null && b.GetComponent<Item>().isCarried, ["region"] = Map.NameAt(b.transform.position)
            }).ToList();

            var queue = new List<object>();
            var asking = new List<object>();
            foreach (CustomerNPC c in CustomerNPC.All)
            {
                if (c == null) continue;
                if (c.IsWaitingToBeServed)
                    queue.Add(new Dictionary<string, object> { ["id"] = CustomerId(c), ["waited_s"] = Round(env.Metrics.WaitedFor(c)), ["region"] = Map.NameAt(c.transform.position), ["walk_m"] = Walk(c.transform.position) });
                var req = c.GetComponent<CustomerRequest>();
                if (req != null && req.CurrentStage != CustomerRequest.Stage.None)
                    asking.Add(new Dictionary<string, object>
                    {
                        ["id"] = CustomerId(c), ["stage"] = req.CurrentStage.ToString(), ["wants"] = req.Wanted,
                        ["region"] = Map.NameAt(c.transform.position), ["walk_m"] = Walk(c.transform.position),
                        ["destination"] = req.CurrentStage >= CustomerRequest.Stage.Escorting ? Map.NameAt(req.Destination) : null
                    });
            }
            o["checkout_queue"] = queue;
            o["customers_asking"] = asking;

            var tools = new Dictionary<string, object>();
            foreach (var (id, type) in new[] { ("mop", ItemType.Mop), ("crate", ItemType.Stock) })
            {
                Item item = Tool(type);
                if (item == null) continue;
                tools[id] = new Dictionary<string, object> { ["held"] = carry != null && carry.Contains(item), ["region"] = Map.NameAt(item.transform.position), ["walk_m"] = Walk(item.transform.position) };
            }
            Flashlight torch = Object.FindAnyObjectByType<Flashlight>();
            if (torch != null)
                tools["flashlight"] = new Dictionary<string, object> { ["held"] = carry != null && carry.Contains(torch.GetComponent<Item>()), ["on"] = torch.IsOn, ["region"] = Map.NameAt(torch.transform.position) };
            o["tools"] = tools;

            var power = new Dictionary<string, object> { ["lights_on"] = PowerSystem.PowerOn };
            BreakerPanel panel = BreakerPanel.Instance;
            if (panel != null)
            {
                // A person at the panel hears each breaker's hum; the agent gets its pitch rank.
                power["breakers"] = Enumerable.Range(0, 3).Select(i => (object)new Dictionary<string, object>
                {
                    ["id"] = "breaker_" + i, ["on"] = panel.SwitchOn(i), ["hum_pitch_rank"] = panel.PitchOf(i)
                }).ToList();
            }
            // The PA's power (0.3.0: the PA box), under its old name for the agents' readers.
            power["pa_jammed"] = KarenWorld.Instance != null && !KarenWorld.Instance.Pa.Powered;
            o["power"] = power;

            o["karen_chores"] = Chores();

            o["karen"] = KarenAsSeen(me);
            var thrown = new List<object>();
            if (SeesThrow(me, out ThrowSeen coming))
                thrown.Add(new Dictionary<string, object>
                {
                    ["item"] = coming.Item, ["eta_s"] = Round(coming.Eta), ["step_to"] = coming.StepTo,
                    ["at"] = new[] { Round(coming.At.x), Round(coming.At.z) }
                });
            o["thrown_at_you"] = thrown;
            heard.RemoveAll(h => Time.time - h.Time > HeardFor);
            o["heard"] = heard.Select(h => (object)new Dictionary<string, object>
            {
                ["id"] = h.Id, ["kind"] = h.Kind, ["category"] = h.Category, ["age_s"] = Round(Time.time - h.Time),
                ["distance_m"] = Round(Vector3.Distance(h.At, here)), ["region"] = Map.NameAt(h.At),
                ["at"] = new[] { Round(h.At.x), Round(h.At.z) }
            }).ToList();
            o["pa_subtitle"] = env.LastSubtitle;
            o["events"] = env.DrainEvents();
            o["last_action"] = env.LastResult;
            return o;
        }

        Dictionary<string, object> KarenAsSeen(PlayerPresence me)
        {
            var result = new Dictionary<string, object> { ["visible"] = false, ["heard"] = false };
            KarenBrain brain = KarenBrain.Instance;
            if (brain == null || brain.Body == null || me == null) return result;

            NoiseBus.ReadSince(ref noiseCursor, noises);
            foreach (NoiseEvent n in noises)
                // A bot hears her steps only when a player could: with the developer's switch on
                // (KarenFx), which is never in a batch run (0.2.0).
                if (n.Kind == NoiseKind.KarenStep && KarenFx.StepsAudible && Vector3.Distance(n.Position, me.Position) < 12f)
                {
                    heardKarenAt = Time.time;
                    if (Time.time - stepsNotedAt > 1f) { stepsNotedAt = Time.time; Note("her_steps", "steps", n.Position); }
                }
            result["heard"] = Time.time - heardKarenAt < 2f;

            bool clear = CanSee(me, brain, out float distance);
            if (clear)
            {
                // The dash, as a person sees it: a moment ago she was further off (close-in), or
                // she wasn't in sight at all and is now close (she stepped out).
                if (sawHer && Time.time - lastSeenAt < 0.6f && lastSeenDistance - distance >= 3f) Note("dash:close_in", "close_in", brain.Body.Position);
                else if (!sawHer && Time.time - lastSeenAt > 2f && distance < 8f) Note("appeared", "appeared", brain.Body.Position);

                sawHer = true;
                lastSeenAt = Time.time;
                lastSeenDistance = distance;
                lastSeenPosition = brain.Body.Position;
                result["visible"] = true;
                result["region"] = Map.NameAt(brain.Body.Position);
                result["at"] = new[] { Round(brain.Body.Position.x), Round(brain.Body.Position.z) };
                result["distance_m"] = Round(distance);
                result["eye_colour"] = brain.Body.CurrentMood.ToString().ToLowerInvariant();
            }
            else if (sawHer)
            {
                // Out of sight. If the spot she stood on a moment ago is still in plain view,
                // she didn't walk off or get blocked: she vanished.
                if (Time.time - lastSeenAt < 0.6f && SpotInView(me, lastSeenPosition)) Note("dash:vanish", "vanish", lastSeenPosition);
                sawHer = false;
            }
            if (lastSeenAt > 0f)
            {
                result["last_seen_s"] = Round(Time.time - lastSeenAt);
                result["last_seen_region"] = Map.NameAt(lastSeenPosition);
            }
            return result;
        }

        static string ItemName(Item i)
        {
            if (i == null) return null;
            if (i.type == ItemType.Mop) return "mop";
            if (i.type == ItemType.Stock && IsTool(i)) return "crate";
            if (i.type == ItemType.TrashBag) return "trash bag";
            if (i.type == ItemType.Flashlight) return "flashlight";
            return i.DisplayName;
        }

        static float Round(float v) => Mathf.Round(v * 10f) / 10f;

        // ---- the same thing as prose, for language models --------------------------------------

        public static string AsText(Dictionary<string, object> o)
        {
            var sb = new StringBuilder();
            var you = (Dictionary<string, object>)o["you"];
            sb.AppendLine($"Shift {o["shift"]}, t={o["t"]}s, {o["time_remaining"]}s until the doors close ({((bool)o["store_open"] ? "open" : "closed — clock out when done")}).");
            sb.AppendLine($"You are in {you["region"]}. Energy {you["energy"]}{((bool)you["can_sprint"] ? "" : " (too tired to sprint)")}. Holding: {you["holding"] ?? "nothing"}.");

            sb.AppendLine("HUD tasks: " + string.Join("; ", ((List<object>)o["hud_tasks"]).Cast<Dictionary<string, object>>()
                .Select(t => $"{t["label"]}{(string.IsNullOrEmpty((string)t["detail"]) ? "" : " (" + t["detail"] + ")")} [{((bool)t["done"] ? "done" : "TODO")}]")));

            List(sb, "Spills", o["spills"], x => $"{x["id"]} in {x["region"]} ({x["walk_m"]} m)");
            List(sb, "Shelves to restock", o["shelves_to_restock"], x => $"{x["id"]} {x["section"]} ({x["empty_slots"]} empty) in {x["region"]} ({x["walk_m"]} m)");
            List(sb, "Bins", o["bins"], x => $"{x["id"]} {x["fill"]}/{x["capacity"]} in {x["region"]}");
            List(sb, "Trash bags", o["bags"], x => $"{x["id"]} {((bool)x["carried"] ? "(carried)" : "in " + x["region"])}");
            List(sb, "Checkout queue", o["checkout_queue"], x => $"{x["id"]} waiting {x["waited_s"]}s ({x["walk_m"]} m)");
            List(sb, "Customers asking", o["customers_asking"], x => $"{x["id"]} wants {x["wants"]} ({x["stage"]}) in {x["region"]}");

            var karen = (Dictionary<string, object>)o["karen"];
            if ((bool)karen["visible"]) sb.AppendLine($"{GameNames.Antagonist} is visible in {karen["region"]}, {karen["distance_m"]} m away, eye {karen["eye_colour"]}.");
            else if ((bool)karen["heard"]) sb.AppendLine("You can hear " + GameNames.Antagonist + "'s footsteps nearby.");
            if (karen.TryGetValue("last_seen_s", out object ago) && !(bool)karen["visible"])
                sb.AppendLine($"You last saw {GameNames.Antagonist} {ago} s ago in {karen["last_seen_region"]}.");
            if (o.TryGetValue("heard", out object h) && h is List<object> hs && hs.Count > 0)
                sb.AppendLine("Lately you noticed: " + string.Join("; ", hs.Cast<Dictionary<string, object>>()
                    .Select(x => $"{x["kind"]} ({x["distance_m"]} m away, in {x["region"]}, {x["age_s"]} s ago)")) + ".");
            if (o.TryGetValue("thrown_at_you", out object th) && th is List<object> ts && ts.Count > 0)
                sb.AppendLine("Coming at you: " + string.Join("; ", ts.Cast<Dictionary<string, object>>()
                    .Select(x => $"a thrown {x["item"]}, {x["eta_s"]} s away — sidestep {x["step_to"]} to get off its line")) + ".");
            var power = (Dictionary<string, object>)o["power"];
            if (!(bool)power["lights_on"]) sb.AppendLine("The lights are out. Breakers: " + string.Join(", ",
                ((List<object>)power["breakers"]).Cast<Dictionary<string, object>>().Select(b => $"{b["id"]} {((bool)b["on"] ? "on" : "off")} (hum pitch {b["hum_pitch_rank"]})")));
            if (o["pa_subtitle"] is string pa && pa.Length > 0) sb.AppendLine("PA: " + pa);
            if (o["last_action"] is Dictionary<string, object> last) sb.AppendLine($"Last action: {last["verb"]} → {((bool)last["ok"] ? "ok" : "FAILED")}: {last["message"]}");
            return sb.ToString();
        }

        static void List(StringBuilder sb, string title, object list, System.Func<Dictionary<string, object>, string> line)
        {
            var items = ((List<object>)list).Cast<Dictionary<string, object>>().ToList();
            if (items.Count == 0) return;
            sb.AppendLine($"{title}: " + string.Join("; ", items.Take(8).Select(line)) + (items.Count > 8 ? $"; …{items.Count - 8} more" : ""));
        }
    }
}
