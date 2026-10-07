using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kehai.Aiko;
using Kehai.Store;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kehai.Eval
{
    // Configuration for one episode — everything that must be fixed for it to be repeatable.
    public sealed class EnvConfig
    {
        public int seed = 1;
        public string rung = "F";
        public float shiftSeconds = 180f;
        public int fps = 20;                 // fixed simulation step: 1/fps seconds per frame
        public bool render = true;
        public bool aiko = true;
        public bool freshLedger = true;
        public int startShift = 1;           // career position of the first shift
        public float overtimeCap = 240f;     // give up this long after the doors close
        public string agent = "external";
        public bool syntheticBlinks = true;  // rung F without a face: a recorded-style blink trace
        public string ledgerPath = "";       // empty = kehai_eval/eval_ledger.json, never the player's own
        public bool verbose;                 // log every action and its result

        public static EnvConfig From(Dictionary<string, object> o)
        {
            var c = new EnvConfig();
            if (o == null) return c;
            c.seed = (int)o.GetNumber("seed", c.seed);
            c.rung = o.GetString("rung", c.rung);
            c.shiftSeconds = (float)o.GetNumber("shift_seconds", c.shiftSeconds);
            c.fps = Mathf.Clamp((int)o.GetNumber("fps", c.fps), 5, 120);
            c.render = o.GetBool("render", c.render);
            c.aiko = o.GetBool("aiko", c.aiko);
            c.freshLedger = o.GetBool("fresh_ledger", c.freshLedger);
            c.startShift = Mathf.Max(1, (int)o.GetNumber("start_shift", c.startShift));
            c.overtimeCap = (float)o.GetNumber("overtime_cap", c.overtimeCap);
            c.agent = o.GetString("agent", c.agent);
            c.syntheticBlinks = o.GetBool("synthetic_blinks", c.syntheticBlinks);
            c.ledgerPath = o.GetString("ledger_path", c.ledgerPath);
            c.verbose = o.GetBool("verbose", c.verbose);
            return c;
        }
    }

    // Kehai as an agent-eval environment (IDEAS.md §1).
    //
    //   reset(config) — reload the store, seed everything, fix the time step, clock in
    //   step(action)  — run one macro-action to completion through the real body and hands
    //   observe()     — the structured snapshot (EnvWorld), plus a prose rendering
    //
    // Determinism: a seed fixes UnityEngine.Random (customers, spills) and Aiko's own RNG,
    // Time.captureDeltaTime fixes every frame's dt, the reload is synchronous and the planner
    // has no wall-clock budget. The first shift after launch replays exactly; later ones in
    // the same process drift a little (engine-side threading), so compare paired seeds. Headless: with render off the cameras stop drawing, and a
    // -batchmode -nographics build runs it with no window at all.
    public sealed class KehaiEnv : MonoBehaviour
    {
        public static KehaiEnv Instance { get; private set; }

        public EnvConfig Config { get; private set; } = new EnvConfig();
        public AgentDriver Driver { get; private set; }
        public ShiftManager Shift { get; private set; }
        public TaskManager Tasks { get; private set; }
        public BurnoutSystem Burnout { get; private set; }
        public EnvWorld World { get; } = new EnvWorld();
        public EpisodeMetrics Metrics { get; private set; } = new EpisodeMetrics();

        public bool Ready { get; private set; }
        public bool Busy { get; private set; }
        public bool Done { get; private set; }
        public Dictionary<string, object> LastResult { get; private set; }
        public string LastSubtitle { get; private set; } = string.Empty;
        public float ShiftSeconds => Time.time - shiftStartedAt;

        readonly List<string> events = new List<string>();
        float shiftStartedAt;
        float wallStartedAt;
        bool cancel;
        PaAnnouncement lastSeenAnnouncement;

        public static string EvalDirectory => System.IO.Path.Combine(Application.persistentDataPath, "kehai_eval");

        public static KehaiEnv Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("~KehaiEnv");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<KehaiEnv>();
            return Instance;
        }

        void Update()
        {
            if (!Ready) return;
            Metrics.Tick(Tasks, Burnout);

            AikoScreen screen = AikoScreen.Instance;
            PaSystem pa = AikoWorld.Instance != null ? AikoWorld.Instance.Pa : null;
            if (pa != null && pa.Busy) LastSubtitle = CurrentPaText(pa);
        }

        static string CurrentPaText(PaSystem pa) => pa != null ? pa.CurrentText : string.Empty;

        public List<string> DrainEvents()
        {
            var copy = new List<string>(events);
            events.Clear();
            return copy;
        }

        public void Event(string e)
        {
            events.Add($"[{ShiftSeconds:0.0}] {e}");
            if (events.Count > 40) events.RemoveAt(0);
        }

        // ---- episodes -----------------------------------------------------------------------

        public IEnumerator ResetEpisode(EnvConfig config)
        {
            Ready = false;
            Done = false;
            Config = config ?? new EnvConfig();
            Metrics.Unsubscribe();

            // Everything the next Aiko and the next shift will be built from.
            AikoBootstrap.Disabled = !Config.aiko;
            // An eval Aiko keeps her Ledger in a file of her own: resetting an episode must
            // never wipe what she has learned about the person who actually plays the game.
            var aiko = new AikoConfig
            {
                seed = Config.seed,
                writeJsonl = false,
                planBudgetMs = 0f,      // no wall-clock cut-off: decisions depend on the seed alone
                ledgerPath = string.IsNullOrEmpty(Config.ledgerPath)
                    ? System.IO.Path.Combine(EvalDirectory, "eval_ledger.json")
                    : Config.ledgerPath
            };
            if (AikoBootstrap.TryParseRung(Config.rung, out AikoRung rung)) aiko.rung = rung;
            AikoBootstrap.Override = aiko;
            if (Config.freshLedger) new AikoLedger(aiko) { Persistent = true }.Wipe();

            Time.captureDeltaTime = 1f / Config.fps;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;

            // A synchronous load always completes on the next frame; an asynchronous one takes
            // as many frames as the disk does, and at a fixed step every frame is simulated
            // time — which made the same seed start the shift at different moments.
            Random.InitState(Config.seed);
            Scene active = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            // A scene that isn't in the build list (a recovered or experimental copy of the
            // store) can still be reloaded in the editor, by path.
            if (active.buildIndex < 0)
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(active.path, new LoadSceneParameters(LoadSceneMode.Single));
            else
#endif
                SceneManager.LoadScene(Mathf.Max(0, active.buildIndex), LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;

            Random.InitState(Config.seed);
            Bind();

            if (Shift != null)
            {
                Shift.shiftDurationSeconds = Config.shiftSeconds;
                Shift.SetShiftNumber(Config.startShift - 1);
            }

            yield return ClockIn();
            Ready = true;

            if (Config.verbose)
            {
                AikoBrain brain = AikoBrain.Instance;
                Debug.Log($"[env] reset seed {Config.seed}: {GameNames.Antagonist} {(brain != null ? $"online, rung {brain.config.rung}, seed {brain.config.seed}" : "absent")}; " +
                          $"shift {(Shift != null ? Shift.ShiftNumber : 0)} active={Shift != null && Shift.IsShiftActive}; player at {World.Map.NameAt(Driver.transform.position)}; " +
                          $"{NavMeshWalls.LastCount} walls carved, NavMesh agent radius {UnityEngine.AI.NavMesh.GetSettingsByIndex(0).agentRadius:0.00}");
            }
        }

        void Bind()
        {
            Shift = FindAnyObjectByType<ShiftManager>();
            Tasks = FindAnyObjectByType<TaskManager>();
            Burnout = FindAnyObjectByType<BurnoutSystem>();
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Driver = player.GetOrAdd<AgentDriver>();
            Driver.verbose = Config.verbose;
            Driver.Engage();
            World.Reset();

            if (!Config.render)
                foreach (Camera cam in FindObjectsByType<Camera>()) cam.enabled = false;

            var blink = FindAnyObjectByType<Kehai.Blink.BlinkTracker>();
            Kehai.Blink.BlinkClock.Simulated = Config.syntheticBlinks;
            if (blink != null && Config.syntheticBlinks) blink.UseSynthetic(Config.seed);
        }

        // Walk to the time clock and punch in — the first thing every shift.
        public IEnumerator ClockIn()
        {
            if (Shift == null || Shift.IsShiftActive) yield break;
            if (AikoBrain.Instance != null && AikoBrain.Instance.Review != null) AikoBrain.Instance.Review.Visible = false;
            yield return Act(new EnvAction { verb = "clock_in" });
            shiftStartedAt = Time.time;
            wallStartedAt = Time.realtimeSinceStartup;
            Metrics = new EpisodeMetrics();
            Metrics.Begin(Config.seed, Shift.ShiftNumber, Config.agent, Config.rung);
            LastResult = null;
            Done = false;
        }

        // The episode ends when the employee clocks out, or runs this far past closing.
        public bool CheckDone()
        {
            if (Done) return true;
            if (Shift == null) return false;

            bool clockedOut = !Shift.IsShiftActive && ShiftSeconds > 1f;
            bool timedOut = Shift.IsShiftActive && ShiftSeconds > Config.shiftSeconds + Config.overtimeCap
                            + (AikoBrain.Instance != null ? AikoBrain.Instance.Stats.Overtimes * 120f : 0f);
            if (!clockedOut && !timedOut) return false;

            Done = true;
            Metrics.End(clockedOut, timedOut, Burnout, Time.realtimeSinceStartup - wallStartedAt);
            if (timedOut && Shift.IsShiftActive) Shift.EndShift();
            return true;
        }

        public void Cancel() => cancel = true;

        // ---- actions ----------------------------------------------------------------------------

        public IEnumerator Act(EnvAction a)
        {
            Busy = true;
            cancel = false;
            string verb = (a.verb ?? string.Empty).Trim().ToLowerInvariant();
            string kind = KindOf(verb, a.target);
            bool ok;
            string message;

            var result = new ActionResult();
            IEnumerator routine = Dispatch(verb, a, result);
            if (routine == null)
            {
                result.Fail($"unknown verb '{a.verb}'");
                yield return null;
            }
            else
            {
                // Run nested steps by hand rather than handing them to Unity, so the timeout
                // and an interruption (a simulated player fleeing) are checked every frame,
                // not only between an action's big steps.
                float started = Time.time;
                int frames = 0;
                var stack = new Stack<IEnumerator>();
                stack.Push(routine);
                while (stack.Count > 0)
                {
                    if (Time.time - started > a.timeout) { result.Fail("timed out"); break; }
                    if (cancel) { result.Fail("interrupted"); break; }
                    IEnumerator top = stack.Peek();
                    bool more;
                    try { more = top.MoveNext(); }
                    catch (System.Exception e)
                    {
                        // A target destroyed mid-action, or a bug: the action fails, the
                        // episode goes on, and the log says why.
                        Debug.LogWarning($"[env] {a} threw: {e.GetType().Name}: {e.Message}");
                        result.Fail("error: " + e.Message);
                        break;
                    }
                    if (!more) { stack.Pop(); continue; }
                    if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    if (result.Finished && !result.Ok) break;
                    frames++;
                    yield return top.Current;
                }
                if (!result.Finished) result.Fail("no result");

                // Every action costs at least a frame, even one refused on the spot. An agent
                // that retries a failing action in a loop then burns simulated time — and
                // eventually the shift — instead of hanging the frame it's running in.
                if (frames == 0) yield return null;
            }

            Driver.ReleaseHands();
            Driver.Stop();
            Driver.Sprint = false;
            Driver.ClearLook();

            ok = result.Ok;
            message = result.Message;
            if (Config.verbose || (!ok && verb == "clock_in"))
                Debug.Log($"[env t={ShiftSeconds:0.0}s] {a} → {(ok ? "ok" : "FAILED")}: {message}");
            Metrics.Record(verb, kind, ok);
            LastResult = new Dictionary<string, object> { ["verb"] = verb, ["target"] = a.target, ["ok"] = ok, ["message"] = message };
            Busy = false;
        }

        static string KindOf(string verb, string target)
        {
            switch (verb)
            {
                case "mop": return "Mop";
                case "restock": case "place_on": return "Stock";
                case "bag_trash": case "dispose": return "Trash";
                case "serve": return "Serve";
                case "help": return "Directions";
                case "drink_coffee": return "Coffee";
                case "flip_breaker": return "Breakers";
                default: return string.Empty;
            }
        }

        sealed class ActionResult
        {
            public bool Ok;
            public string Message = "ok";
            public bool Finished;
            public void Pass(string m = "ok") { Ok = true; Message = m; Finished = true; }
            public void Fail(string m) { Ok = false; Message = m; Finished = true; }
        }

        IEnumerator Dispatch(string verb, EnvAction a, ActionResult r)
        {
            switch (verb)
            {
                case "move_to": return MoveTo(a, r);
                case "pick_up": return PickUp(a, r);
                case "restock": return Restock(a, r);
                case "mop": return Mop(a, r);
                case "serve": return Serve(a, r);
                case "help": return Help(a, r);
                case "bag_trash": return BagTrash(a, r);
                case "dispose": return Dispose(a, r);
                case "drink_coffee": return UseLandmark(LandmarkKind.CoffeeMachine, a, r);
                case "clock_in": return Clock(true, a, r);
                case "clock_out": return Clock(false, a, r);
                case "flip_breaker": return FlipBreaker(a, r);
                case "equip": return Equip(a, r);
                case "toggle_flashlight": return ToggleTorch(a, r);
                case "return_tool": return ReturnTool(a, r);
                case "drop": return DropHeld(a, r);
                case "wait": return Wait(a, r);
                default: return null;
            }
        }

        // ---- building blocks -------------------------------------------------------------------

        IEnumerator Approach(Vector3 target, float within, EnvAction a, ActionResult r)
        {
            Driver.Sprint = a.sprint;
            Driver.Crouch = a.crouch;
            if (Driver.DistanceTo(target) <= within) yield break;
            if (!Driver.GoTo(target, within)) { r.Fail("no path to " + World.Map.NameAt(target)); yield break; }
            while (Driver.Moving) yield return null;
            if (Driver.DistanceTo(target) > within + 1.2f) r.Fail("couldn't get close enough to " + World.Map.NameAt(target));
        }

        IEnumerator Face(Vector3 point)
        {
            Driver.LookAt(point);
            for (int i = 0; i < 12; i++) yield return null;
        }

        IEnumerator Tap(IInteractable target, Vector3 at)
        {
            yield return Face(at);
            Driver.Tap(target);
            yield return null;
            yield return null;
            Driver.ReleaseHands();
        }

        bool EnsureHolding(Item item, ActionResult r)
        {
            if (item == null) { r.Fail("no such item"); return false; }
            CarrySlot carry = Driver.Carry;
            for (int i = 0; i < carry.items.Length; i++)
                if (carry.items[i] == item) { carry.SetActiveSlot(i); return true; }
            r.Fail($"not carrying the {item.type.ToString().ToLowerInvariant()} — pick it up first");
            return false;
        }

        // ---- verbs -----------------------------------------------------------------------------

        IEnumerator MoveTo(EnvAction a, ActionResult r)
        {
            if (!World.TryPosition(a.target, out Vector3 p)) { r.Fail($"unknown target '{a.target}'"); yield break; }
            yield return Approach(p, 1.5f, a, r);
            if (!r.Finished) r.Pass("at " + World.Map.NameAt(Driver.transform.position));
        }

        IEnumerator PickUp(EnvAction a, ActionResult r)
        {
            Item item = World.Resolve<Item>(a.target);
            if (item == null) { r.Fail($"unknown item '{a.target}'"); yield break; }
            if (Driver.Carry.Contains(item)) { r.Pass("already carrying it"); yield break; }
            if (Driver.Carry.IsFull()) { r.Fail("inventory full"); yield break; }
            var pickup = item.GetComponent<PickupInteractable>();
            if (pickup == null) { r.Fail("can't pick that up"); yield break; }

            yield return Approach(item.transform.position, 1.8f, a, r);
            if (r.Finished) yield break;
            yield return Tap(pickup, item.transform.position);
            if (Driver.Carry.Contains(item)) r.Pass("picked up " + a.target); else r.Fail("pickup didn't take");
        }

        IEnumerator Restock(EnvAction a, ActionResult r)
        {
            ShelfUnit bay = World.Resolve<ShelfUnit>(a.target);
            if (bay == null) { r.Fail($"unknown bay '{a.target}'"); yield break; }
            if (bay.IsFull) { r.Pass("already full"); yield break; }
            if (!EnsureHolding(EnvWorld.Tool(ItemType.Stock), r)) yield break;

            Vector3 stand = TacticHelpers.StandIn(bay);
            yield return Approach(stand, 1.2f, a, r);
            if (r.Finished) yield break;

            ShelfSlot slot = bay.GetComponentsInChildren<ShelfSlot>()
                .OrderBy(s => (s.transform.position - Driver.transform.position).sqrMagnitude).FirstOrDefault();
            if (slot == null) { r.Fail("bay has no slots"); yield break; }
            yield return Tap(slot, slot.transform.position);
            if (bay.IsFull) r.Pass("restocked"); else r.Fail("restock didn't take");
        }

        IEnumerator Mop(EnvAction a, ActionResult r)
        {
            Dirt spill = World.Resolve<Dirt>(a.target);
            if (spill == null) { r.Fail($"no spill '{a.target}' (already cleaned?)"); yield break; }
            if (!EnsureHolding(EnvWorld.Tool(ItemType.Mop), r)) yield break;

            yield return Approach(spill.transform.position, 1.3f, a, r);
            if (r.Finished) yield break;
            if (spill == null) { r.Pass("someone else cleaned it"); yield break; }
            yield return Face(spill.transform.position);

            float until = Time.time + spill.secondsToClean + 2f;
            Driver.Hold(spill, true);
            while (spill != null && Time.time < until) yield return null;
            Driver.Hold(null, false);
            if (spill == null) r.Pass("mopped"); else r.Fail("stopped mopping before it was clean");
        }

        IEnumerator Serve(EnvAction a, ActionResult r)
        {
            CustomerNPC c = World.Resolve<CustomerNPC>(a.target);
            if (c == null) { r.Fail($"no customer '{a.target}'"); yield break; }
            if (!c.IsWaitingToBeServed) { r.Fail("they aren't waiting at the till"); yield break; }
            yield return Approach(c.transform.position, 2.4f, a, r);
            if (r.Finished) yield break;
            yield return Tap(c, c.transform.position);
            if (c == null || !c.IsWaitingToBeServed) r.Pass("served"); else r.Fail("serve didn't take");
        }

        IEnumerator Help(EnvAction a, ActionResult r)
        {
            CustomerNPC c = World.Resolve<CustomerNPC>(a.target);
            var req = c != null ? c.GetComponent<CustomerRequest>() : null;
            if (req == null || req.CurrentStage == CustomerRequest.Stage.None) { r.Fail("they aren't asking for anything"); yield break; }

            yield return Approach(c.transform.position, 3f, a, r);
            if (r.Finished) yield break;

            // Someone already following (or who lost you and wandered back) just needs leading;
            // anyone else is asked first.
            bool following = req.CurrentStage == CustomerRequest.Stage.Escorting || req.CurrentStage == CustomerRequest.Stage.Returning;
            if (!following)
            {
                yield return Tap(c, c.transform.position);
                yield return null;
                req.externalChoice = a.accept ? 0 : 1;
                yield return null;
                yield return null;
                if (!a.accept) { r.Pass("declined"); yield break; }
            }

            // Walk them there, slowly enough that they keep up. Someone following at arm's
            // length is easy for a person to step past and hard for a capsule, so the two
            // bodies don't collide for the length of the escort.
            Vector3 destination = req.Destination;
            var playerBody = Driver.GetComponent<CharacterController>();
            var theirs = c.GetComponentsInChildren<Collider>();
            foreach (Collider col in theirs) if (col != null && playerBody != null) Physics.IgnoreCollision(playerBody, col, true);
            // They stop a couple of metres short of whoever they follow but only count as
            // there within 1.6 m of the spot, so lead them past it, as a person would.
            Vector3 lead = LeadPoint(destination, c.transform.position);
            float relead = Time.time + 6f;
            float until = Time.time + 90f;
            while (req != null && req.CurrentStage != CustomerRequest.Stage.None && Time.time < until)
            {
                float gap = Vector3.Distance(c.transform.position, Driver.transform.position);
                if (gap > 5f) Driver.Stop();
                else if (!Driver.Moving)
                {
                    if (Driver.DistanceTo(lead) > 0.8f) Driver.GoTo(lead, 0.5f);
                    else if (Time.time > relead)
                    {
                        lead = LeadPoint(destination, c.transform.position);
                        relead = Time.time + 6f;
                    }
                }
                yield return null;
            }
            foreach (Collider col in theirs) if (col != null && playerBody != null) Physics.IgnoreCollision(playerBody, col, false);
            if (req == null || req.CurrentStage == CustomerRequest.Stage.None) r.Pass("escorted"); else r.Fail("they didn't make it");
        }

        // A standing spot just beyond `destination`, seen from someone at `from`.
        static Vector3 LeadPoint(Vector3 destination, Vector3 from)
        {
            Vector3 dir = destination - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            foreach (Vector3 d in new[] { dir, (dir + side).normalized, (dir - side).normalized, side, -side })
            {
                Vector3 p = destination + d * 1.7f;
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit hit, 0.5f, UnityEngine.AI.NavMesh.AllAreas) &&
                    !UnityEngine.AI.NavMesh.Raycast(destination, hit.position, out _, UnityEngine.AI.NavMesh.AllAreas))
                    return hit.position;
            }
            return destination;
        }

        IEnumerator BagTrash(EnvAction a, ActionResult r)
        {
            Trashcan bin = World.Resolve<Trashcan>(a.target);
            if (bin == null) { r.Fail($"no bin '{a.target}'"); yield break; }
            if (bin.UsageCount == 0) { r.Pass("already empty"); yield break; }
            if (Driver.Carry.IsFull()) { r.Fail("inventory full"); yield break; }
            yield return Approach(bin.transform.position, 1.7f, a, r);
            if (r.Finished) yield break;
            yield return Tap(bin, bin.transform.position);
            if (bin.UsageCount == 0) r.Pass("bagged"); else r.Fail("bagging didn't take");
        }

        IEnumerator Dispose(EnvAction a, ActionResult r)
        {
            Item bag = Driver.Carry.items.FirstOrDefault(i => i != null && i.type == ItemType.TrashBag);
            if (bag == null) { r.Fail("not carrying a trash bag"); yield break; }
            EnsureHolding(bag, r);

            Landmark? skip = World.Map.FindLandmark(LandmarkKind.TrashSkip);
            if (!skip.HasValue) { r.Fail("no skip in this store"); yield break; }
            yield return Approach(skip.Value.Position, 1.6f, a, r);
            if (r.Finished) yield break;
            yield return Face(skip.Value.Position);

            int before = Metrics.BagsDisposed;
            Driver.Drop(3.5f);
            float until = Time.time + 3f;
            while (Metrics.BagsDisposed == before && Time.time < until) yield return null;
            if (Metrics.BagsDisposed > before) r.Pass("in the skip"); else r.Fail("missed the skip");
        }

        IEnumerator UseLandmark(LandmarkKind kind, EnvAction a, ActionResult r)
        {
            Landmark? l = World.Map.NearestLandmark(kind, Driver.transform.position);
            if (!l.HasValue || !(l.Value.Source is IInteractable target)) { r.Fail($"no {kind}"); yield break; }
            yield return Approach(l.Value.Position, 1.7f, a, r);
            if (r.Finished) yield break;
            yield return Tap(target, l.Value.Position);
            r.Pass(kind.ToString());
        }

        IEnumerator Clock(bool clockIn, EnvAction a, ActionResult r)
        {
            if (Shift == null) { r.Fail("no shift manager"); yield break; }
            if (clockIn == Shift.IsShiftActive) { r.Pass(clockIn ? "already on shift" : "already off shift"); yield break; }

            Landmark? clock = World.Map.FindLandmark(LandmarkKind.TimeClock);
            if (!clock.HasValue || !(clock.Value.Source is IInteractable puncher)) { r.Fail("no time clock"); yield break; }
            yield return Approach(clock.Value.Position, 1.6f, a, r);
            if (r.Finished) yield break;
            yield return Tap(puncher, clock.Value.Position);
            yield return null;

            if (Shift.IsShiftActive == clockIn) r.Pass(clockIn ? "clocked in" : "clocked out");
            else r.Fail(clockIn ? "couldn't clock in" : "clock-out refused");
        }

        IEnumerator FlipBreaker(EnvAction a, ActionResult r)
        {
            string id = a.target ?? string.Empty;
            BreakerSwitch target = FindObjectsByType<BreakerSwitch>()
                .FirstOrDefault(b => b.name.EndsWith(((LightCircuit)int.Parse(id.Replace("breaker_", string.Empty))).ToString()));
            if (target == null) { r.Fail($"no breaker '{a.target}'"); yield break; }
            yield return Approach(target.transform.position, 1.5f, a, r);
            if (r.Finished) yield break;
            yield return Tap(target, target.transform.position);
            r.Pass("thrown");
        }

        IEnumerator Equip(EnvAction a, ActionResult r)
        {
            int slot = Mathf.Clamp(a.slot, 0, Driver.Carry.items.Length - 1);
            Driver.Carry.SetActiveSlot(slot);
            yield return null;
            r.Pass("slot " + slot);
        }

        IEnumerator ToggleTorch(EnvAction a, ActionResult r)
        {
            Flashlight torch = FindAnyObjectByType<Flashlight>();
            if (torch == null || !EnsureHolding(torch.GetComponent<Item>(), r)) yield break;
            torch.Toggle();
            yield return null;
            r.Pass(torch.IsOn ? "torch on" : "torch off");
        }

        IEnumerator ReturnTool(EnvAction a, ActionResult r)
        {
            Item tool = World.Resolve<Item>(a.target);
            ToolSnapPoint home = FindObjectsByType<ToolSnapPoint>().FirstOrDefault(s => s.tool == tool);
            if (home == null) { r.Fail("that has no home"); yield break; }
            yield return Approach(home.transform.position, 1.7f, a, r);
            if (r.Finished) yield break;
            yield return Tap(home, home.transform.position);
            r.Pass("returned");
        }

        IEnumerator DropHeld(EnvAction a, ActionResult r)
        {
            if (!Driver.Carry.IsCarrying) { r.Fail("hands empty"); yield break; }
            Driver.Drop(0f);
            yield return null;
            r.Pass("dropped");
        }

        IEnumerator Wait(EnvAction a, ActionResult r)
        {
            float until = Time.time + Mathf.Clamp(a.seconds, 0.1f, 60f);
            while (Time.time < until) yield return null;
            r.Pass("waited");
        }
    }

    public sealed class EnvAction
    {
        public string verb;
        public string target;
        public bool sprint;
        public bool crouch;
        public bool accept = true;
        public int slot;
        public float seconds = 2f;
        public float timeout = 90f;

        public static EnvAction From(Dictionary<string, object> o) => new EnvAction
        {
            verb = o.GetString("verb"),
            target = o.GetString("target"),
            sprint = o.GetBool("sprint"),
            crouch = o.GetBool("crouch"),
            accept = o.GetBool("accept", true),
            slot = (int)o.GetNumber("slot"),
            seconds = (float)o.GetNumber("seconds", 2),
            timeout = (float)o.GetNumber("timeout", 90)
        };

        public override string ToString() => $"{verb}({target}{(sprint ? ", sprint" : "")})";
    }
}
