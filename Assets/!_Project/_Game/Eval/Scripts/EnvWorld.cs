using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Eval
{
    // What an agent can see and name (IDEAS.md §1 "Observation").
    //
    // Text first, pixels later: a serialisable snapshot that isolates *planning* failure
    // from *perception* failure. Every object an action can target gets a stable id for the
    // episode — spill_3, bay_41, cust_7, bin_2, mop — and every place is named by the store
    // map, so an agent reasons over "Aisle 2/B", not coordinates.
    //
    // Fair by construction: the agent sees the HUD's task list (which Karen can falsify),
    // Karen only when she is in its view cone with a clear line of sight, and her footsteps
    // only when they're close enough to hear. It is told nothing a player couldn't know.
    public sealed class EnvWorld
    {
        readonly Dictionary<Object, string> ids = new Dictionary<Object, string>();
        readonly Dictionary<string, Object> objects = new Dictionary<string, Object>();
        int nextSpill = 1, nextCustomer = 1, nextBag = 1;
        float[] fromPlayer;
        long noiseCursor;
        readonly List<NoiseEvent> noises = new List<NoiseEvent>();
        float heardKarenAt = -99f;

        public StoreMap Map => StoreMap.Current;

        public void Reset()
        {
            ids.Clear();
            objects.Clear();
            nextSpill = nextCustomer = nextBag = 1;
            noiseCursor = NoiseBus.Latest;
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
            foreach (ToolSnapPoint s in Object.FindObjectsByType<ToolSnapPoint>())
                if (s.tool != null && s.tool.type == type) return s.tool;
            foreach (Item i in Object.FindObjectsByType<Item>())
                if (i.type == type) return i;
            return null;
        }

        // ---- observing ---------------------------------------------------------------------

        float Walk(Vector3 p)
        {
            int cell = Map.CellAt(p, 4f);
            if (cell < 0 || fromPlayer == null) return -1f;
            float d = fromPlayer[cell];
            return float.IsInfinity(d) ? -1f : Mathf.Round(d * 10f) / 10f;
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
                ["frozen_by_lecture"] = Consequences.LectureRunning
            };

            // The HUD, exactly as the player sees it — lies included.
            var tasks = new List<object>();
            if (env.Tasks != null && env.Tasks.HasTasks)
                foreach (TaskManager.ShiftTask t in HudFeed.Shown(env.Tasks.Tasks))
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
                power["pa_jammed"] = panel.PaJammed;
            }
            o["power"] = power;

            o["karen"] = KarenAsSeen(me);
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
                if (n.Kind == NoiseKind.KarenStep && Vector3.Distance(n.Position, me.Position) < 12f) heardKarenAt = Time.time;
            result["heard"] = Time.time - heardKarenAt < 2f;

            Vector3 her = brain.Body.Position + Vector3.up * 1.4f;
            Vector3 d = her - me.Head;
            bool inView = d.magnitude < 35f && Vector3.Angle(me.Facing, d) < 55f;
            bool clear = inView && (!Physics.Linecast(me.Head, her, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                                    || hit.collider.transform.IsChildOf(brain.Body.transform));
            if (clear)
            {
                result["visible"] = true;
                result["region"] = Map.NameAt(brain.Body.Position);
                result["distance_m"] = Round(d.magnitude);
                result["eye_colour"] = brain.Body.CurrentMood.ToString().ToLowerInvariant();
            }
            return result;
        }

        static string ItemName(Item i)
        {
            if (i == null) return null;
            if (i.type == ItemType.Mop) return "mop";
            if (i.type == ItemType.Stock) return "crate";
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
