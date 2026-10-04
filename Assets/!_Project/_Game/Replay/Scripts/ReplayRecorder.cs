using System.Collections.Generic;
using System.IO;
using Kehai.Aiko;
using Kehai.Store;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kehai.Replay
{
    // Records the shift for the 3D replay, next to the shift record:
    //   <persistent data>/shift_records/<stem>.krec
    // Everything that moves or changes in the store, as snapshots (see KrecFormat.cs): the
    // player's view 60 times a second, the player, Aiko, customers, her understudies, doors,
    // shelf units, her props, spills, bins, bags, tools and every item off its shelf 30 times a
    // second, the lights and the shelf slots as they change, what was heard and said, her
    // thought log, and her belief map twice a second. Only listens; changes nothing.
    //
    // Playtest builds also record the stretches between shifts, before the first clock-in and
    // after each clock-out, as <shift records>/interlude_<next shift>_<time>.krec: where the
    // tester went and what they looked at while working out what to do.
    [DefaultExecutionOrder(900)]   // after the store has moved this frame
    public sealed class ReplayRecorder : MonoBehaviour
    {
        public static ReplayRecorder Instance { get; private set; }

        // The last recording finished this session (the review screen offers to watch it).
        public static string LastFinished { get; private set; }

        // Set by the playtest: record the time between shifts too.
        public static bool RecordInterludes;
        const float ShortestInterlude = 3f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            LastFinished = null;
            RecordInterludes = false;
        }

        public sealed class Tracked
        {
            public int Id;
            public KrecKind Kind;
            public Transform Transform;
            public Object Owner;
            public System.Func<int> State;
            public System.Func<bool> Visible;
        }

        public bool Recording => writer != null;
        public string CurrentPath => writer != null ? writer.Path : null;
        // The file being written (as it will be named when finished) and the time in it.
        public string CurrentFile => writer != null ? finalPath : null;
        public float RecordingTime => writer != null ? Now : -1f;
        public float LastTickTime { get; private set; } = -1f;
        public IReadOnlyList<Tracked> Entities => tracked;

        // After each tick is written, with its time (for the round-trip test).
        public event System.Action<float> Ticked;

        ShiftRecorder shift;
        ShiftRecording recordingOf;
        KrecWriter writer;
        string finalPath;
        bool stopped;
        bool interlude;               // recording the time between shifts, not a shift
        float interludeStartedAt;
        string stem, started;         // what the file is called and when it began
        int shiftNumber;

        readonly List<Tracked> tracked = new List<Tracked>();
        readonly Dictionary<Object, Tracked> byOwner = new Dictionary<Object, Tracked>();
        int nextId = 1;
        float nextTick, nextCamera, nextBelief, nextDiscovery, nextSlots;

        AikoBrain brain;
        PaSystem pa;
        Camera view;
        Eyelids lids;
        CarrySlot carry;
        PlayerTools tools;

        List<ShelfSlot> slots;                  // as they were at the start: slot numbers stay put
        readonly HashSet<Item> onShelves = new HashSet<Item>();
        bool[] slotFilled;
        string[] slotProduct;
        IReadOnlyList<Light> lights;
        bool[] lightOn;
        int circuits = -1;
        readonly List<(int, bool)> changedLights = new List<(int, bool)>();

        int[] cellToBin;
        float[] binMass;
        byte[] binBytes;

        void Awake()
        {
            Instance = this;
            shift = GetComponent<ShiftRecorder>();
        }

        void OnEnable()
        {
            NoiseBus.Emitted += OnNoise;
            AikoNarrator.Said += OnStory;
            ShiftRecorder.Finishing += OnFinishing;
        }

        void OnDisable()
        {
            NoiseBus.Emitted -= OnNoise;
            AikoNarrator.Said -= OnStory;
            ShiftRecorder.Finishing -= OnFinishing;
            Unhook();
            if (writer != null) Close(Now, false, keep: !interlude || Now >= ShortestInterlude);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        float Now => interlude ? Time.time - interludeStartedAt : shift.ShiftTime;

        void LateUpdate()
        {
            if (shift == null) return;
            if (shift.Current != recordingOf)
            {
                if (writer != null) Close(LastTickTime, false, keep: !interlude || LastTickTime >= ShortestInterlude);
                recordingOf = shift.Current;
                stopped = false;
                if (recordingOf != null) Begin();
            }
            else if (recordingOf == null && writer == null && RecordInterludes && StoreMap.Current != null &&
                     ReplayPlayer.Instance == null && GameObject.FindGameObjectWithTag("Player") != null)
                BeginInterlude();
            if (writer == null) return;
            Hook();

            float t = Now;
            if (t >= nextDiscovery)
            {
                nextDiscovery = t + Krec.DiscoverySeconds;
                Discover();
            }
            if (t >= nextTick)
            {
                nextTick = Mathf.Max(nextTick + 1f / Krec.TickRate, t);
                Tick(t);
            }
            if (t >= nextCamera)
            {
                nextCamera = Mathf.Max(nextCamera + 1f / Krec.CameraRate, t);
                RecordCamera(t);
            }
            if (t >= nextSlots)
            {
                nextSlots = t + Krec.SlotSeconds;
                RecordSlots(t);
            }
            if (t >= nextBelief)
            {
                nextBelief = Mathf.Max(nextBelief + 1f / Krec.BeliefRate, t);
                RecordBelief(t);
            }
        }

        // ---- start and finish ----------------------------------------------------------------

        void Begin()
        {
            if (stopped || string.IsNullOrEmpty(shift.Stem)) return;
            interlude = false;
            stem = shift.Stem;
            shiftNumber = recordingOf.ShiftNumber;
            started = recordingOf.StartedAt;
            StartWriter();
        }

        // The time between shifts, until the next clock-in (or the store unloads).
        void BeginInterlude()
        {
            ShiftManager manager = FindAnyObjectByType<ShiftManager>();
            shiftNumber = (manager != null ? manager.ShiftNumber : 0) + 1;
            interlude = true;
            interludeStartedAt = Time.time;
            stem = Path.Combine(ShiftRecorder.Folder, $"interlude_{shiftNumber:00}_{System.DateTime.Now:yyyyMMdd_HHmmss}");
            started = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Directory.CreateDirectory(ShiftRecorder.Folder);
            StartWriter();
        }

        void StartWriter()
        {
            finalPath = stem + ".krec";
            try
            {
                writer = new KrecWriter(finalPath + ".part");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Couldn't start the replay recording: " + e.Message);
                writer = null;
                return;
            }
            tracked.Clear();
            byOwner.Clear();
            nextId = 1;
            nextTick = nextCamera = nextBelief = nextDiscovery = nextSlots = 0f;
            circuits = -1;
            view = null;
            lids = null;
            carry = null;
            tools = null;

            Hook();
            writer.Header(BuildHeader());
            writer.Tick(Now);
            Discover();
            LastTickTime = Now;
        }

        // Finishes the file being written now (the playtest is packing). A stretch between shifts
        // starts again by itself; a shift doesn't.
        public string Cut()
        {
            if (writer == null) return null;
            bool keep = !interlude || Now >= ShortestInterlude;
            return Close(Now, false, keep);
        }

        // Ends the recording now and returns the finished file (the round-trip test uses it).
        public string EndRecording()
        {
            if (writer == null) return null;
            stopped = true;
            return Close(Now, false, keep: true);
        }

        void OnFinishing(ShiftRecording r)
        {
            if (writer == null || r != recordingOf) return;
            // A shift too short to be saved leaves no replay either.
            Close(r.Length, r.ClockedOut, keep: r.Frames.Count >= 5);
        }

        string Close(float length, bool clockedOut, bool keep)
        {
            KrecWriter w = writer;
            writer = null;
            try
            {
                w.End(length, clockedOut);
                if (!keep)
                {
                    File.Delete(w.Path);
                    return null;
                }
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(w.Path, finalPath);
                LastFinished = finalPath;
                long bytes = new FileInfo(finalPath).Length;
                float perTen = length > 1f ? bytes / length * 600f / (1024f * 1024f) : 0f;
                var kinds = new SortedDictionary<string, int>();
                foreach (Tracked e in tracked) kinds[e.Kind.ToString()] = kinds.TryGetValue(e.Kind.ToString(), out int n) ? n + 1 : 1;
                Debug.Log($"Replay recorded: {finalPath} ({bytes / 1024f:0} KB for {length:0} s, ≈{perTen:0.0} MB per 10 min; " +
                          $"tracking {string.Join(", ", System.Linq.Enumerable.Select(kinds, k => $"{k.Value} {k.Key}"))})");
                return finalPath;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Couldn't finish the replay recording: " + e.Message);
                return null;
            }
        }

        KrecHeader BuildHeader()
        {
            var h = new KrecHeader
            {
                Stem = Path.GetFileName(stem),
                Shift = shiftNumber,
                Started = started,
                Scene = SceneManager.GetActiveScene().path,
                Seed = brain != null && brain.Rng != null ? brain.Rng.Seed : 0,
                Rung = brain != null ? brain.config.rung.ToString() : ""
            };

            BuildBeliefGrid(h);

            IReadOnlyList<Bay> bays = StoreMap.Current.Bays;
            foreach (MazeMutation.Move m in MazeMutation.LastMoves)
            {
                int bay = -1;
                for (int i = 0; i < bays.Count; i++) if (bays[i].Unit == m.Bay) { bay = i; break; }
                h.MazeMoves.Add((bay, m.From, m.To));
            }

            slots = new List<ShelfSlot>(ShelfSlot.All);
            slotFilled = new bool[slots.Count];
            slotProduct = new string[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                ShelfSlot s = slots[i];
                slotFilled[i] = s != null && s.isFilled;
                slotProduct[i] = s != null ? s.productId : "";
                Vector3 at = s == null ? Vector3.zero : s.snapPoint != null ? s.snapPoint.position : s.transform.position;
                h.Slots.Add((at, slotFilled[i], slotProduct[i]));
            }

            lights = LightProbe.CeilingLights;
            lightOn = new bool[lights.Count];
            for (int i = 0; i < lights.Count; i++)
            {
                lightOn[i] = lights[i] != null && lights[i].enabled;
                h.Lights.Add((lights[i] != null ? lights[i].transform.position : Vector3.zero, lightOn[i]));
            }
            return h;
        }

        // Her map's 1.5 m cells, two by two into 3 m bins.
        void BuildBeliefGrid(KrecHeader h)
        {
            StoreMap map = StoreMap.Current;
            int cols = 0, rows = 0;
            for (int c = 0; c < map.CellCount; c++)
            {
                map.CellGrid(c, out int col, out int row);
                cols = Mathf.Max(cols, col / 2 + 1);
                rows = Mathf.Max(rows, row / 2 + 1);
            }
            cellToBin = new int[map.CellCount];
            h.BeliefMask = new byte[cols * rows];
            for (int c = 0; c < map.CellCount; c++)
            {
                map.CellGrid(c, out int col, out int row);
                cellToBin[c] = (row / 2) * cols + col / 2;
                h.BeliefMask[cellToBin[c]] = 1;
            }
            h.BeliefOrigin = new Vector2(map.Origin.x, map.Origin.z);
            h.BeliefCell = StoreMap.CellSize * 2f;
            h.BeliefCols = cols;
            h.BeliefRows = rows;
            binMass = new float[cols * rows];
            binBytes = new byte[cols * rows];
        }

        // ---- what's in the store -----------------------------------------------------------

        Tracked Track(Object owner, KrecKind kind, Transform t, string key, string label, System.Func<int> state = null, System.Func<bool> visible = null)
        {
            if (owner == null || t == null) return null;
            if (byOwner.TryGetValue(owner, out Tracked existing)) return existing;
            var e = new Tracked { Id = nextId++, Kind = kind, Transform = t, Owner = owner, State = state, Visible = visible };
            tracked.Add(e);
            byOwner[owner] = e;
            writer.Spawn(e.Id, kind, key, label, t.position, t.rotation, state != null ? state() : 0, visible != null ? visible() : t.gameObject.activeInHierarchy);
            return e;
        }

        void Discover()
        {
            // Gone since last time.
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                Tracked e = tracked[i];
                if (e.Owner != null && e.Transform != null) continue;
                writer.Despawn(e.Id);
                tracked.RemoveAt(i);
                byOwner.Remove(e.Owner);   // a destroyed object still works as its own key
            }

            PlayerPresence me = PlayerPresence.Current;
            if (me != null)
            {
                Track(me, KrecKind.Player, me.transform, "Player", "you", () =>
                {
                    int s = (int)me.Motion;
                    if (carry != null && carry.IsCarrying) s |= KrecState.PlayerCarrying;
                    if (tools != null && tools.IsHolding) s |= KrecState.PlayerHoldingTool;
                    return s;
                });
                if (view == null) view = me.GetComponentInChildren<Camera>() ?? Camera.main;
                if (carry == null) carry = me.GetComponentInParent<CarrySlot>() ?? Object.FindAnyObjectByType<CarrySlot>();
                if (tools == null) tools = me.GetComponentInParent<PlayerTools>() ?? Object.FindAnyObjectByType<PlayerTools>();
                if (lids == null) lids = Object.FindAnyObjectByType<Eyelids>();
            }

            if (brain != null && brain.Body != null)
            {
                AikoBody body = brain.Body;
                AikoBrain b = brain;
                Track(body, KrecKind.Aiko, body.transform, "Aiko", GameNames.Antagonist, () =>
                {
                    int s = (int)body.CurrentMood;
                    if (body.Sight != null && body.Sight.Awareness >= body.Sight.seeAt) s |= KrecState.AikoSees;
                    if (b.IsChasing) s |= KrecState.AikoChasing;
                    return s;
                }, () => true);
            }

            foreach (CustomerNPC c in CustomerNPC.All)
                if (c != null)
                {
                    CustomerNPC customer = c;
                    Track(c, KrecKind.Customer, c.transform, "Customer", c.name, () => (int)ShiftRecording.MarkOf(customer));
                }
            foreach (Understudy u in Object.FindObjectsByType<Understudy>()) Track(u, KrecKind.Understudy, u.transform, "Understudy", u.name);

            foreach (HingeDoor d in HingeDoor.All)
                if (d != null)
                {
                    HingeDoor door = d;
                    Track(d, KrecKind.Door, d.transform, "Door", d.name, () => door.Locked ? 1 : 0);
                }
            foreach (AutoDoubleDoor d in Object.FindObjectsByType<AutoDoubleDoor>())
            {
                if (d.LeftPanel != null) Track(d.LeftPanel, KrecKind.AutoDoorPanel, d.LeftPanel, "AutoDoor/left", d.name);
                if (d.RightPanel != null) Track(d.RightPanel, KrecKind.AutoDoorPanel, d.RightPanel, "AutoDoor/right", d.name);
            }
            foreach (Bay bay in StoreMap.Current.Bays)
                if (bay.Unit != null) Track(bay.Unit, KrecKind.ShelfUnit, bay.Unit.transform, "Shelf", bay.Section ?? bay.Unit.name);

            foreach (CrateWall w in Object.FindObjectsByType<CrateWall>())
            {
                int columns = Mathf.Max(1, w.transform.childCount / 2);   // two crates high
                Track(w, KrecKind.CrateWall, w.transform, "CrateWall", w.name, () => columns);
            }
            foreach (FogCloud f in Object.FindObjectsByType<FogCloud>())
            {
                int radius = Mathf.RoundToInt(f.Radius * 10f);
                Track(f, KrecKind.Fog, f.transform, "Fog", f.name, () => radius);
            }
            foreach (CctvCamera c in CctvCamera.All)
                if (c != null)
                {
                    CctvCamera cam = c;
                    Track(c, KrecKind.Cctv, c.transform, c.BoltedOn ? "Cctv/bolted" : "Cctv", c.name, () => (cam.BoltedOn ? 1 : 0) | (cam.Dead ? 2 : 0));
                }
            foreach (CoffeeCup c in Object.FindObjectsByType<CoffeeCup>())
            {
                bool last = c.IsLast;
                Track(c, KrecKind.Coffee, c.transform, "Coffee", c.name, () => last ? 1 : 0);
            }
            foreach (Footprint f in Object.FindObjectsByType<Footprint>()) Track(f, KrecKind.Footprint, f.transform, "Footprint", f.name);
            foreach (Dirt d in Dirt.All) if (d != null) Track(d, KrecKind.Spill, d.transform, "Spill", d.name);
            foreach (Trashcan bin in Trashcan.All)
                if (bin != null)
                {
                    Trashcan can = bin;
                    Track(bin, KrecKind.Bin, bin.transform, "Bin", bin.name, () => can.UsageCount);
                }
            foreach (TrashBag bag in Object.FindObjectsByType<TrashBag>())
            {
                TrashBag b = bag;
                Track(bag, KrecKind.Bag, bag.transform, "Bag", bag.name, () => b.IsDisposed ? 1 : 0);
            }
            DiscoverItems();
        }

        // An item is recorded from the moment it leaves its shelf: in a hand, dropped, thrown,
        // knocked off. While it sits on a shelf the slot events say where it is. (The items
        // placed in the scene sit in their slots without isOnShelf set, so a slot holding an
        // item counts as its shelf too.)
        void DiscoverItems()
        {
            onShelves.Clear();
            if (slots != null)
                foreach (ShelfSlot slot in slots)
                    if (slot != null && slot.isFilled && slot.storedItem != null) onShelves.Add(slot.storedItem);
            foreach (Item item in Item.All)
            {
                if (item == null || byOwner.ContainsKey(item) || item.isOnShelf || onShelves.Contains(item) || !item.gameObject.activeInHierarchy) continue;
                TrackItem(item);
            }
        }

        // Tools are items too (the mop, the torch): they rest on their racks, off any shelf.
        Tracked TrackItem(Item item)
        {
            Item it = item;
            Flashlight torch = item.GetComponent<Flashlight>();
            string key = string.IsNullOrEmpty(item.productId) ? item.type.ToString() : item.productId;
            return Track(item, KrecKind.Item, item.transform, key, item.DisplayName, () =>
            {
                int s;
                if (it.isOnShelf || onShelves.Contains(it)) s = KrecState.ItemOnShelf;
                else if (!it.isCarried) s = KrecState.ItemLoose;
                else if ((carry != null && carry.Contains(it)) || (tools != null && tools.HeldTool == it.gameObject)) s = KrecState.ItemInHand;
                else s = KrecState.ItemHeld;
                if (torch != null && torch.IsOn) s |= KrecState.ItemLit;
                return s;
            });
        }

        // ---- sampling ----------------------------------------------------------------------

        void Tick(float t)
        {
            writer.Tick(t);
            DiscoverItems();
            foreach (Tracked e in tracked)
            {
                if (e.Transform == null || e.Owner == null) continue;
                bool visible = e.Visible != null ? e.Visible() : e.Transform.gameObject.activeInHierarchy;
                writer.Pose(e.Id, e.Transform.position, e.Transform.rotation, e.State != null ? e.State() : 0, visible);
            }
            RecordLights(t);
            RecordCircuits(t);
            LastTickTime = t;
            Ticked?.Invoke(t);
        }

        void RecordCamera(float t)
        {
            if (view == null) return;
            int held = 0;
            if (tools != null && tools.HeldTool != null)
            {
                Item tool = tools.HeldTool.GetComponent<Item>();
                Tracked e = tool != null ? TrackItem(tool) : null;
                if (e != null) held = e.Id;
            }
            else if (carry != null && carry.currentItem != null)
            {
                Tracked e = TrackItem(carry.currentItem);
                if (e != null) held = e.Id;
            }
            writer.Camera(t, view.transform.position, view.transform.rotation, view.fieldOfView, lids != null ? lids.Closed01 : 0f, held);
        }

        void RecordSlots(float t)
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Count && i < slotFilled.Length; i++)
            {
                ShelfSlot s = slots[i];
                if (s == null) continue;
                if (s.isFilled == slotFilled[i] && s.productId == slotProduct[i]) continue;
                slotFilled[i] = s.isFilled;
                slotProduct[i] = s.productId;
                writer.Slot(t, i, s.isFilled, s.productId);
            }
        }

        void RecordLights(float t)
        {
            if (lights == null) return;
            changedLights.Clear();
            for (int i = 0; i < lights.Count && i < lightOn.Length; i++)
            {
                bool on = lights[i] != null && lights[i].enabled;
                if (on == lightOn[i]) continue;
                lightOn[i] = on;
                changedLights.Add((i, on));
            }
            if (changedLights.Count > 0) writer.Lights(t, changedLights);
        }

        void RecordCircuits(float t)
        {
            BreakerPanel panel = BreakerPanel.Instance;
            int bits = PowerSystem.PowerOn ? 1 : 0;
            if (panel != null)
            {
                for (int i = 0; i < 3; i++) if (panel.IsOn((LightCircuit)i)) bits |= 2 << i;
                if (panel.PaJammed) bits |= 16;
            }
            if (bits == circuits) return;
            circuits = bits;
            writer.Circuits(t, bits);
        }

        void RecordBelief(float t)
        {
            if (brain == null || brain.Belief == null || cellToBin == null) return;
            BeliefGrid belief = brain.Belief;
            System.Array.Clear(binMass, 0, binMass.Length);
            int cells = Mathf.Min(belief.CellCount, cellToBin.Length);
            for (int c = 0; c < cells; c++) binMass[cellToBin[c]] += belief[c];
            float max = 0f;
            for (int i = 0; i < binMass.Length; i++) max = Mathf.Max(max, binMass[i]);
            for (int i = 0; i < binMass.Length; i++)
                binBytes[i] = max > 0f ? (byte)Mathf.RoundToInt(255f * Mathf.Sqrt(binMass[i] / max)) : (byte)0;
            Vector3 peak = belief.PeakPosition;
            writer.Belief(t, new Vector2(peak.x, peak.z), belief.Confidence, binBytes);
        }

        // ---- what's heard and said ----------------------------------------------------------

        void Hook()
        {
            if (brain == null && AikoBrain.Instance != null)
            {
                brain = AikoBrain.Instance;
                brain.Log.Written += OnThought;
                brain.Told += OnTell;
            }
            if (pa == null && AikoWorld.Instance != null && AikoWorld.Instance.Pa != null)
            {
                pa = AikoWorld.Instance.Pa;
                pa.ChimeStarted += OnPaChime;
                pa.SpeechStarted += OnPaSpeech;
            }
        }

        void Unhook()
        {
            if (brain != null)
            {
                brain.Log.Written -= OnThought;
                brain.Told -= OnTell;
                brain = null;
            }
            if (pa != null)
            {
                pa.ChimeStarted -= OnPaChime;
                pa.SpeechStarted -= OnPaSpeech;
                pa = null;
            }
        }

        void OnNoise(NoiseEvent n) { if (writer != null) writer.Noise(Now, (int)n.Kind, (int)n.Author, n.Position, n.Loudness); }
        void OnTell(TellKind kind, Vector3 at, float lead) { if (writer != null) writer.Tell(Now, (int)kind, at, lead); }
        void OnPaChime(PaAnnouncement a) { if (writer != null) writer.Text(Now, KrecEvent.PaChime, a.Text); }
        void OnPaSpeech(PaAnnouncement a) { if (writer != null) writer.Text(Now, KrecEvent.PaSpeech, a.Text); }
        void OnThought(ThoughtRecord r) { if (writer != null) writer.Thought(Now, r.Kind, r.Text); }
        void OnStory(StoryLine line) { if (writer != null) writer.Story(Now, (int)line.Kind, line.Text); }
    }
}
