using System.Collections.Generic;
using System.IO;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Aiko
{
    // Records the shift: where you, Aiko and every customer were five times a second, and
    // everything that happened — sounds, her decisions, your jobs, customers asking and
    // queueing, the lights. Keeps a live picture for the F1 map, the whole shift for the F2
    // replay, and at clock-out writes it to disk with a report you can open in a browser:
    //
    //   <persistent data>/shift_records/shift_03_20260926_141500.json          (the data)
    //   <persistent data>/shift_records/shift_03_20260926_141500.html          (map, replay, analysis)
    //   <persistent data>/shift_records/shift_03_20260926_141500.markers.json  (moments worth a clip)
    public sealed class ShiftRecorder : MonoBehaviour
    {
        public const float FrameInterval = 0.2f;

        public static ShiftRecorder Instance { get; private set; }

        public ShiftRecording Current { get; private set; }       // the shift in progress
        public ShiftRecording Last { get; private set; }          // the most recent finished one
        public ShiftAnalysis LastAnalysis { get; private set; }
        public string LastReportPath { get; private set; }
        public ShiftFrame Live { get; } = new ShiftFrame();
        public bool Recording => Current != null;

        // Recent events for the live map, with the Time.time they happened.
        readonly List<ShiftEvent> recent = new List<ShiftEvent>();
        public IReadOnlyList<ShiftEvent> Recent => recent;

        ShiftManager shift;
        BurnoutSystem burnout;
        float shiftStartedAt, nextFrame, nextLive;
        bool lastPower = true;

        readonly Dictionary<CustomerNPC, int> ids = new Dictionary<CustomerNPC, int>();
        readonly Dictionary<ShelfUnit, int> bays = new Dictionary<ShelfUnit, int>();
        readonly Dictionary<int, CustomerMark> lastMark = new Dictionary<int, CustomerMark>();
        readonly HashSet<int> helped = new HashSet<int>(), served = new HashSet<int>(), waitWarned = new HashSet<int>();
        int nextId = 1;

        public static string Folder => Path.Combine(Application.persistentDataPath, "shift_records");

        // The shift's files, without their extensions: named when the shift starts, so the replay
        // recorder can write <stem>.krec as it goes.
        public string Stem { get; private set; }

        // Every event as it's recorded, and the finished shift just before it's written —
        // for the clip markers, which close what's still open and mark the review.
        public static event System.Action<ShiftEvent> Recorded;
        public static event System.Action<ShiftRecording> Finishing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Recorded = null;
            Finishing = null;
        }

        void Awake()
        {
            Instance = this;
            shift = FindAnyObjectByType<ShiftManager>();
            burnout = FindAnyObjectByType<BurnoutSystem>();
        }

        void OnEnable()
        {
            NoiseBus.Emitted += OnNoise;
            AikoNarrator.Said += OnStory;
            GameEvents.SpillCleaned += OnSpill;
            GameEvents.ShelfRestocked += OnRestocked;
            GameEvents.BinBagged += OnBagged;
            GameEvents.BagDisposed += OnDisposed;
            GameEvents.CustomerServed += OnServed;
            GameEvents.DirectionsGiven += OnDirections;
            GameEvents.CoffeeDrunk += OnCoffee;
            GameEvents.PunchAttempted += OnPunch;
            if (shift != null) shift.ShiftStateChanged += OnShiftChanged;
        }

        void OnDisable()
        {
            NoiseBus.Emitted -= OnNoise;
            AikoNarrator.Said -= OnStory;
            GameEvents.SpillCleaned -= OnSpill;
            GameEvents.ShelfRestocked -= OnRestocked;
            GameEvents.BinBagged -= OnBagged;
            GameEvents.BagDisposed -= OnDisposed;
            GameEvents.CustomerServed -= OnServed;
            GameEvents.DirectionsGiven -= OnDirections;
            GameEvents.CoffeeDrunk -= OnCoffee;
            GameEvents.PunchAttempted -= OnPunch;
            if (shift != null) shift.ShiftStateChanged -= OnShiftChanged;
            if (Current != null) Finish(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (shift != null && shift.IsShiftActive) Begin();
        }

        public float ShiftTime => Time.time - shiftStartedAt;

        // ---- the shift ------------------------------------------------------------------------

        void OnShiftChanged()
        {
            if (shift.IsShiftActive && Current == null) Begin();
            else if (!shift.IsShiftActive && Current != null) Finish(true);
        }

        void Begin()
        {
            AikoBrain brain = AikoBrain.Instance;
            Current = new ShiftRecording
            {
                ShiftNumber = shift != null ? shift.ShiftNumber : 0,
                PlayerName = brain != null && brain.Ledger != null ? brain.Ledger.PlayerName : "",
                AikoRung = brain != null ? brain.config.rung.ToString() : "",
                StartedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };
            shiftStartedAt = Time.time;
            Stem = Path.Combine(Folder, $"shift_{Current.ShiftNumber:00}_{System.DateTime.Now:yyyyMMdd_HHmmss}");
            nextFrame = 0f;
            helped.Clear(); served.Clear(); waitWarned.Clear(); lastMark.Clear();
            Add("store", "The shift started.", Vector3.zero, false, "the store");
        }

        // The playtest wraps up mid-shift (the tester is quitting): write what there is now.
        public void FinishNow()
        {
            if (Current != null) Finish(false);
        }

        void Finish(bool clockedOut)
        {
            ShiftRecording r = Current;
            Current = null;
            if (r == null) return;
            r.Length = Time.time - shiftStartedAt;
            r.ClockedOut = clockedOut;
            Finishing?.Invoke(r);
            Last = r;
            LastAnalysis = ShiftAnalysis.Of(r);
            r.Moments = ClipMoments.Build(r);

            if (r.Frames.Count < 5) return;
            try
            {
                Directory.CreateDirectory(Folder);
                string stem = Stem;
                StoreFloorPlan plan = StoreFloorPlan.Current;
                string json = r.ToJson(plan, LastAnalysis);
                File.WriteAllText(stem + ".json", json);
                File.WriteAllText(stem + ".html", ShiftReportHtml.Build(json, r.ShiftNumber));
                File.WriteAllText(stem + ".markers.json", ClipMoments.FileJson(r, Path.GetFileName(stem), r.Moments));
                LastReportPath = stem + ".html";
                Debug.Log($"Shift {r.ShiftNumber} recorded: {LastReportPath} ({r.Markers.Count} clip markers, {r.Moments.Count} moments)");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Couldn't save the shift recording: " + e.Message);
            }
        }

        // Writes the current (or last) shift out now and opens its report in the browser.
        public void OpenReport()
        {
            ShiftRecording r = Current ?? Last;
            if (r == null) return;
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, $"shift_{r.ShiftNumber:00}_{(Current != null ? "in_progress" : System.DateTime.Now.ToString("yyyyMMdd_HHmmss"))}.html");
                if (Current != null) r.Length = ShiftTime;
                ShiftAnalysis a = Current != null ? ShiftAnalysis.Of(r) : LastAnalysis;
                if (Current != null || r.Moments == null) r.Moments = ClipMoments.Build(r);
                File.WriteAllText(path, ShiftReportHtml.Build(r.ToJson(StoreFloorPlan.Current, a), r.ShiftNumber));
                LastReportPath = path;
                Application.OpenURL("file://" + path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Couldn't write the shift report: " + e.Message);
            }
        }

        // ---- sampling --------------------------------------------------------------------------

        void Update()
        {
            if (Time.time >= nextLive)
            {
                nextLive = Time.time + 0.1f;
                ShiftRecording.Capture(Live, Current != null ? ShiftTime : 0f, burnout, IdOf, BayOf);
                if (Live.Power != lastPower)
                {
                    lastPower = Live.Power;
                    Add("store", Live.Power ? "The lights are back on." : "The lights went out! Fix the breakers in the Backstreet.", Vector3.zero, false, "the store");
                }
                WatchCustomers(Live);
            }

            if (Current == null || ShiftTime < nextFrame) return;
            nextFrame = ShiftTime + FrameInterval;
            var f = new ShiftFrame();
            ShiftRecording.Capture(f, ShiftTime, burnout, IdOf, BayOf);
            Current.Frames.Add(f);
            while (recent.Count > 0 && Time.time - recent[0].WallTime > 30f) recent.RemoveAt(0);
        }

        int IdOf(CustomerNPC c)
        {
            if (!ids.TryGetValue(c, out int id)) ids[c] = id = nextId++;
            return id;
        }

        // The shelf a customer is asking for — the one their request actually picked, looked
        // up each time, since the same shopper can ask again for something else.
        int BayOf(CustomerNPC c)
        {
            var req = c.GetComponent<CustomerRequest>();
            ShelfUnit shelf = req != null ? req.DestinationShelf : null;
            if (shelf == null) return -1;
            if (bays.TryGetValue(shelf, out int bay)) return bay;
            List<Bay> all = StoreMap.Current.Bays;
            bay = -1;
            for (int i = 0; i < all.Count; i++) if (all[i].Unit == shelf) { bay = i; break; }
            bays[shelf] = bay;
            return bay;
        }

        // Customers changing what they're doing become lines of the story.
        void WatchCustomers(ShiftFrame f)
        {
            var seen = new HashSet<int>();
            foreach (PersonState c in f.Customers)
            {
                if (c.Id < 0) continue;
                seen.Add(c.Id);
                lastMark.TryGetValue(c.Id, out CustomerMark before);
                bool known = lastMark.ContainsKey(c.Id);
                lastMark[c.Id] = c.State;
                Vector3 at = new Vector3(c.At.x, 0f, c.At.y);

                if (c.State == CustomerMark.Asking && (!known || before != CustomerMark.Asking))
                {
                    CustomerNPC npc = Find(c.Id);
                    var req = npc != null ? npc.GetComponent<CustomerRequest>() : null;
                    string wanted = req != null && !string.IsNullOrEmpty(req.Wanted) ? req.Wanted : "something";
                    if (Current != null) Current.CustomerWants[c.Id] = wanted;
                    string shelf = c.Bay >= 0 ? ShelfWords(c.Bay) : "somewhere in the store";
                    Add("customer", $"A customer is looking for {wanted}. It's on {shelf}.", at, true, "asked");
                }
                else if (known && (before == CustomerMark.Asking || before == CustomerMark.LostTheGuide || before == CustomerMark.Talking) &&
                         c.State != CustomerMark.Asking && c.State != CustomerMark.Talking && c.State != CustomerMark.Following &&
                         c.State != CustomerMark.LostTheGuide && !helped.Contains(c.Id))
                {
                    Add("customer", "A customer gave up asking and went back to shopping.", at, true, "gave up asking");
                }
                else if (c.State == CustomerMark.Queueing && known && before != CustomerMark.Queueing)
                {
                    Add("customer", "A customer is waiting at the till.", at, true, "queued");
                }
                else if (c.State == CustomerMark.Queueing && c.Wait > 60f && waitWarned.Add(c.Id))
                {
                    Add("customer", "A customer has been waiting at the till for a whole minute.", at, true, "waiting long");
                }
                else if (known && before == CustomerMark.Queueing && c.State != CustomerMark.Queueing && !served.Contains(c.Id))
                {
                    Add("customer", "A customer gave up waiting at the till and left.", at, true, "gave up at the till");
                }
                else if (c.State == CustomerMark.Possessed && known && before != CustomerMark.Possessed)
                {
                    Add("customer", "A customer suddenly stopped and turned towards you… " + GameNames.Antagonist + " is watching through them.", at, true, "possessed");
                }
            }
            if (lastMark.Count > seen.Count * 2 + 20)
            {
                var gone = new List<int>();
                foreach (int id in lastMark.Keys) if (!seen.Contains(id)) gone.Add(id);
                foreach (int id in gone) lastMark.Remove(id);
            }
        }

        CustomerNPC Find(int id)
        {
            foreach (var pair in ids) if (pair.Value == id && pair.Key != null) return pair.Key;
            return null;
        }

        // "the Cereal & Breakfast shelf (Aisle 4)", from the section's own name.
        public static string ShelfWords(int bay)
        {
            StoreMap map = StoreMap.Current;
            if (bay < 0 || bay >= map.Bays.Count) return "a shelf";
            Bay b = map.Bays[bay];
            if (string.IsNullOrEmpty(b.Section)) return "a shelf near " + AikoNarrator.Place(b.Position);
            int dot = b.Section.IndexOf(" · ", System.StringComparison.Ordinal);
            return dot < 0 ? $"the {b.Section} shelf" : $"the {b.Section.Substring(dot + 3)} shelf ({b.Section.Substring(0, dot)})";
        }

        // ---- events ----------------------------------------------------------------------------

        void Add(string kind, string text, Vector3 at, bool hasPlace, string who, float radius = 0f)
        {
            var e = new ShiftEvent
            {
                T = Current != null ? ShiftTime : 0f,
                WallTime = Time.time,
                Kind = kind,
                Text = text,
                At = StoreFloorPlan.Flat(at),
                HasPlace = hasPlace,
                Who = who,
                Radius = radius
            };
            recent.Add(e);
            if (recent.Count > 400) recent.RemoveRange(0, recent.Count - 400);
            if (Current != null) Current.Events.Add(e);
            Recorded?.Invoke(e);
        }

        void OnNoise(NoiseEvent n)
        {
            if (n.Kind == NoiseKind.Tell) return;   // the warning itself is a story line
            Add("sound", AikoNarrator.Noise(n.Kind), n.Position, true, AikoNarrator.Who(n.Author), n.Carry);
        }

        void OnStory(StoryLine line) => Add(line.Kind.ToString(), line.Text, line.At, line.HasPlace, GameNames.Antagonist);

        void Job(string what, string sentence, Vector3 at) => Add("job", sentence, at, true, what);

        static Vector3 PlayerAt => PlayerPresence.Current != null ? PlayerPresence.Current.Position : Vector3.zero;

        void OnSpill(Dirt d) => Job("mopped a spill", $"You mopped a spill in {AikoNarrator.Place(d != null ? d.transform.position : PlayerAt)}.", d != null ? d.transform.position : PlayerAt);
        void OnRestocked(ShelfUnit u, int n) => Job("restocked a shelf", $"You restocked a shelf in {AikoNarrator.Place(u != null ? TacticHelpers.StandIn(u) : PlayerAt)}.", u != null ? TacticHelpers.StandIn(u) : PlayerAt);
        void OnBagged(Trashcan t) => Job("bagged a bin", "You bagged up a full bin — now take it to the skip outside.", t != null ? t.transform.position : PlayerAt);
        void OnDisposed(TrashBag b) => Job("took a bag to the skip", "You threw a rubbish bag in the skip.", PlayerAt);
        void OnCoffee(Vector3 at) => Job("drank a coffee", "You drank a coffee. Energy restored.", at);

        void OnServed(CustomerNPC c)
        {
            if (c != null) served.Add(IdOf(c));
            Job("served a customer", "You served a customer at the till.", c != null ? c.transform.position : PlayerAt);
        }

        void OnDirections(CustomerNPC c)
        {
            if (c != null) helped.Add(IdOf(c));
            Job("gave directions", "You walked a customer to the shelf they wanted.", c != null ? c.transform.position : PlayerAt);
        }

        void OnPunch(bool accepted)
        {
            if (shift == null) return;
            if (accepted) return;   // the shift change itself is recorded
            Add("store", "The time clock refused you — there's still work to do (or " + GameNames.Antagonist + " added overtime).", PlayerAt, true, "the store");
        }
    }
}
