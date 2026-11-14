using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Eval
{
    // Karen.md §13: scripted player profiles. They play through exactly the same actions an
    // external agent would, with the same senses (EnvWorld.Observe): Karen only when she's in
    // view, her footsteps only close by, her warnings as far as they carry, and her blink dash
    // as a person would notice it. Nothing here reads Karen herself.
    //
    // What they sense frightens them (BotFear), each in its own way, and the fear shows
    // through the body: looking round, sprinting, freezing, giving up on a job, taking the
    // long way, dropping things, hiding — the very signals her Panic Index reads. Fear drains,
    // and they go back to work. Her touch kills, so all of them back away when she's close,
    // by a way that doesn't pass her.
    //
    // Since 0.2.0 they answer her Hunt's tricks as a person would: seeing her standing on the
    // way ahead (her Cut), they turn back; seeing something thrown at them, they sidestep it
    // a moment later, most of the time. A throw from behind, they never see.
    //
    // Static bots (-ablation-static-bots) are the old ones, kept as a control: they don't
    // react to her tricks, and only the skittish one flees, when it sees or hears her.
    public enum PlayerProfile
    {
        Efficient,   // nearest job first; minds her when she's close or the HUD lies
        Skittish,    // over-reacts to everything: looks round, freezes, drops things, hides
        Reckless,    // sprints everywhere, ignores noise; only being chased gets to it
        Cautious     // held out: checks behind, shuns where she was, hides early. Not in the
                     // ablation's default profiles and never used in RL training, so it can
                     // test whether what Karen learns carries over to someone new.
    }

    public sealed class SimulatedPlayer
    {
        readonly KehaiEnv env;
        readonly PlayerProfile profile;
        readonly System.Random random;
        readonly bool reactive;
        readonly BotTraits traits;
        readonly BotFear fear = new BotFear();
        readonly Dictionary<string, float> avoidUntil = new Dictionary<string, float>();
        readonly Dictionary<string, float> shunned = new Dictionary<string, float>();   // region → until
        float calmUntil, lastTick;
        int lastHeardId;
        bool lightsWereOn = true, startled;
        string lastSeenRegion, lastThreatRegion;
        Vector3? lastThreatAt;
        float lastThreatTime = -99f;
        Vector3? turnBackFrom;                  // she's ahead on the way: turn back once the action stops
        string dodgeSide;                       // a throw to step out of, once the action stops
        int watching;                           // the throw it has seen coming, and since when
        float watchingSince;
        readonly HashSet<int> decided = new HashSet<int>();

        public int Hides { get; private set; }
        public int BackOffs { get; private set; }
        public int TurnBacks { get; private set; }
        public int ThrowsSeen { get; private set; }
        public int Dodges { get; private set; }

        // 0.4.0 (build plan §7): who it is, what it remembers across the career, and the trained
        // mind that chooses for it (none: the heuristic below, as it always was).
        readonly Persona persona;
        public BotMemory Memory { get; }
        readonly IEmployeeMind mind;
        EmployeeWhat? lastWhat;
        float[] mindInputs;
        bool[] mindWhat, mindWhich;
        float fearSeconds, sprintSeconds;
        int jobsAtDecision, lostAtDecision;
        bool usedHabit, lookedOnce, curiousDone;
        public int Choices { get; private set; }
        public bool CareerOver { get; set; } = true;   // the league clears it on all but a career's last shift

        public SimulatedPlayer(KehaiEnv env, PlayerProfile profile, int seed, bool reactive = true)
            : this(env, Persona.Of(profile), seed, reactive, null, null) { }

        // A persona's player. A named point (Persona.Of) plays exactly as its profile does; `memory`
        // carries over a career (null: a fresh one); `mind` chooses instead of the heuristic.
        public SimulatedPlayer(KehaiEnv env, Persona persona, int seed, bool reactive, BotMemory memory, IEmployeeMind mind)
        {
            this.env = env;
            this.persona = persona ?? Persona.Of(PlayerProfile.Efficient);
            profile = this.persona.NearestProfile();
            this.reactive = reactive;
            random = new System.Random(seed);
            traits = this.persona.ToTraits(seed);
            Memory = memory ?? new BotMemory();
            this.mind = mind;
        }

        public string Name => persona.Scripted.HasValue ? profile.ToString().ToLowerInvariant() : persona.Name;
        public Persona Persona => persona;

        // Plays until the episode is done.
        public IEnumerator PlayShift()
        {
            lastTick = Time.time;
            ShiftRecorder.BotLabel = Name + ":" + persona;
            Memory.BeginShift(env.Shift != null ? env.Shift.ShiftNumber : 0);
            jobsAtDecision = JobsDone;
            lostAtDecision = env.Metrics.CustomersLost;
            Look();

            while (!env.CheckDone())
            {
                var obs = env.World.Observe(env);
                Sense(obs);

                if (reactive && TooClose(obs, out Vector3 threat))
                {
                    yield return BackOff(threat);
                    continue;
                }

                // With a trained mind, hiding is one of its choices, not a reflex (§7.1).
                if (!MindActive && reactive && fear.Fear >= traits.HideAt && Time.time > calmUntil)
                {
                    if (mind is IEmployeeDemonstrator teacher && teacher.Recording)
                        teacher.Demonstrate(EmployeeObservation.Build(obs, fear.Fear, Memory, persona, lastWhat, Time.time),
                                            EmployeeOptions.MaskWhat(obs), EmployeeOptions.MaskHow(), EmployeeOptions.MaskWhich(Memory, lastThreatAt.HasValue),
                                            new EmployeeChoice { What = EmployeeWhat.Hide, How = EmployeeHow.Hurry, Which = EmployeeWhich.Safest });
                    yield return Flee();
                    continue;
                }

                EnvAction action = null;
                EmployeeChoice choice = default;
                if (mind != null && MindChooses(obs, out choice))
                {
                    if (choice.What == EmployeeWhat.Hide) { Log(choice, Here); yield return Flee(); continue; }
                    if (choice.What == EmployeeWhat.BackOff)
                    {
                        Log(choice, Here);
                        yield return BackOff(lastThreatAt ?? Here + Flat(env.Driver.transform.forward) * 4f);
                        continue;
                    }
                    action = Realise(choice, obs);
                }
                if (action == null)
                {
                    action = Choose(obs);
                    choice = Tag(action, obs, "heuristic");
                    if (mind is IEmployeeDemonstrator teacher && teacher.Recording && mindInputs != null)
                        teacher.Demonstrate(mindInputs, mindWhat, EmployeeOptions.MaskHow(), mindWhich, choice);
                }
                choice.Verb = action.verb;
                choice.Target = action.target;
                lastWhat = choice.What;
                IEnumerator run = env.Act(action);
                IEnumerator monitor = reactive ? Monitor(action) : profile == PlayerProfile.Skittish ? Watch() : null;
                Vector3 leadsTo = Here;
                while (run.MoveNext())
                {
                    monitor?.MoveNext();
                    if (env.Driver.Moving) leadsTo = env.Driver.Goal;
                    yield return run.Current;
                }
                bool done = env.LastResult != null && (bool)env.LastResult["ok"];
                Log(choice, leadsTo, done);
                if (done && IsJob(choice.What)) Memory.DidJob(choice.What.ToString());

                if (dodgeSide != null)
                {
                    yield return Sidestep();
                    continue;
                }
                if (turnBackFrom.HasValue)
                {
                    // Not that way: she's on it. Leave the job a while, and go back the way it came.
                    Vector3 her = turnBackFrom.Value;
                    turnBackFrom = null;
                    TurnBacks++;
                    avoidUntil[Key(action.verb, action.target)] = Time.time + 10f;
                    yield return BackOff(her);
                    continue;
                }

                // A job that just failed (no path, wrong tool, already done) is left alone for a
                // while rather than retried every frame.
                if (env.LastResult != null && !(bool)env.LastResult["ok"])
                    avoidUntil[Key(action.verb, action.target)] = Time.time + 15f;

                if (reactive && startled)
                {
                    startled = false;
                    // Freezing within her reach is how people die: backing off comes first.
                    var after = env.World.Observe(env);
                    if (!TooClose(after, out _)) yield return AfterStartle(after);
                }
                else if (!reactive && profile == PlayerProfile.Skittish && Threatened(env.World.Observe(env)))
                    yield return Flee();

                yield return null;
            }

            FinishShift();
            env.Metrics.BotMeanFear = fear.MeanFear;
            env.Metrics.BotPeakFear = fear.Peak;
            env.Metrics.BotStartles = fear.Startles;
            env.Metrics.BotHides = Hides;
            env.Metrics.BotBackOffs = BackOffs;
            env.Metrics.BotTurnBacks = TurnBacks;
            env.Metrics.BotThrowsSeen = ThrowsSeen;
            env.Metrics.BotDodges = Dodges;
            env.Metrics.BotReactive = reactive;
        }

        static string Key(string verb, string target) => verb + ":" + target;

        bool Avoided(string verb, string target) =>
            avoidUntil.TryGetValue(Key(verb, target), out float until) && Time.time < until;

        EnvAction Unless(EnvAction a, EnvAction otherwise) => a != null && !Avoided(a.verb, a.target) ? a : otherwise;

        // ---- fear ----------------------------------------------------------------------------

        // Reads the observation for anything frightening that's new since last time.
        void Sense(Dictionary<string, object> obs)
        {
            float now = Time.time, dt = Mathf.Max(0f, now - lastTick);
            lastTick = now;
            fear.Decay(dt, traits);
            if (!reactive) return;

            var karen = (Dictionary<string, object>)obs["karen"];
            if ((bool)karen["visible"])
            {
                float distance = (float)karen["distance_m"];
                float near = Mathf.Clamp01(1f - distance / 30f);
                bool red = (string)karen["eye_colour"] == "hunt";
                Feel(red ? Stimulus.Chase : Stimulus.SawHer, (0.3f + near) * dt * 2f);
                lastSeenRegion = (string)karen["region"];
                Shun(lastSeenRegion);
                lastThreatRegion = lastSeenRegion;
                Threat(Point(karen["at"]));
            }

            if (obs.TryGetValue("heard", out object h) && h is List<object> heard)
                foreach (var e in heard.Cast<Dictionary<string, object>>())
                {
                    int id = (int)e["id"];
                    if (id <= lastHeardId) continue;
                    lastHeardId = id;
                    Stimulus s = FromCategory((string)e["category"]);
                    float distance = (float)e["distance_m"];
                    Feel(s, 1f - 0.5f * Mathf.Clamp01(distance / EnvWorld.TellFar));
                    string category = (string)e["category"];
                    if (category != "pa" && category != "hud")
                    {
                        Shun((string)e["region"]);
                        lastThreatRegion = (string)e["region"];
                        Threat(Point(e["at"]));
                    }
                }

            fearSeconds += fear.Fear * dt;
            if (env.Driver != null && env.Driver.Sprint && env.Driver.Moving) sprintSeconds += dt;
            if ((bool)karen["visible"]) Memory.SensedHer((string)karen["region"], now);

            var power = (Dictionary<string, object>)obs["power"];
            bool lightsOn = (bool)power["lights_on"];
            if (!lightsOn && lightsWereOn) Feel(Stimulus.Blackout, 1f);
            lightsWereOn = lightsOn;

            Look();
        }

        void Feel(Stimulus s, float scale)
        {
            if (fear.Feel(s, traits, scale)) startled = true;
        }

        public static Stimulus FromCategory(string category)
        {
            switch (category)
            {
                case "light": return Stimulus.TellLight;
                case "pa": return Stimulus.TellPa;
                case "hud": return Stimulus.TellHud;
                case "social": return Stimulus.TellSocial;
                case "her": return Stimulus.TellHer;
                case "steps": return Stimulus.HerSteps;
                case "close_in": return Stimulus.CloseIn;
                case "vanish": return Stimulus.Vanish;
                case "appeared": return Stimulus.Appeared;
                default: return Stimulus.TellSound;
            }
        }

        // Looking round more the more frightened: the Panic Index's yaw jitter.
        void Look()
        {
            env.Driver.lookJitter = reactive
                ? traits.LookBase + traits.LookGain * fear.Fear
                : profile == PlayerProfile.Skittish ? 0.9f : profile == PlayerProfile.Reckless ? 0.2f : 0f;
        }

        void Shun(string region)
        {
            if (!string.IsNullOrEmpty(region)) shunned[region] = Time.time + traits.AvoidSeconds;
        }

        bool Shunned(string region) => region != null && shunned.TryGetValue(region, out float until) && Time.time < until;

        void Threat(Vector3 at)
        {
            lastThreatAt = at;
            lastThreatTime = Time.time;
        }

        // Where the observation puts something ([x, z]), at the player's own height.
        Vector3 Point(object at)
        {
            var xz = (float[])at;
            return new Vector3(xz[0], env.Driver.transform.position.y, xz[1]);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // Runs alongside an action: keeps sensing, sprints while frightened, and drops the job
        // on a big enough startle — or to go and hide.
        IEnumerator Monitor(EnvAction action)
        {
            float next = 0f;
            while (true)
            {
                if (WatchThrows()) env.Cancel();
                else if (Time.time >= next)
                {
                    next = Time.time + 0.25f;
                    var obs = env.World.Observe(env);
                    Sense(obs);
                    env.Driver.Sprint = action.sprint || fear.Fear >= traits.SprintAt;
                    bool hide = fear.Fear >= traits.HideAt && Time.time > calmUntil;
                    if ((startled && fear.Fear >= traits.AbandonAt) || hide || TooClose(obs, out _)) env.Cancel();
                    else if (Ahead(obs, out Vector3 her))
                    {
                        turnBackFrom = her;
                        env.Cancel();
                    }
                }
                yield return null;
            }
        }

        // ---- her Hunt's tricks (0.2.0) --------------------------------------------------------

        // She's in view on the way it's going, nearer than where it's going and than it lets her
        // be: she got there first (Cut), or she's simply in the way.
        bool Ahead(Dictionary<string, object> obs, out Vector3 her)
        {
            her = default;
            var karen = (Dictionary<string, object>)obs["karen"];
            if (!(bool)karen["visible"] || !env.Driver.Moving) return false;
            her = Point(karen["at"]);
            return OnTheWay(env.Driver.transform.position, env.Driver.Goal, her, traits.TurnBackAt);
        }

        // Whether `her` stands on the way from `me` to `goal`: within 35° of it, nearer than the
        // goal, and nearer than `turnBackAt`.
        public static bool OnTheWay(Vector3 me, Vector3 goal, Vector3 her, float turnBackAt)
        {
            Vector3 toHer = Flat(her - me), toGoal = Flat(goal - me);
            float distance = toHer.magnitude;
            return distance <= turnBackAt && toGoal.magnitude > distance && Vector3.Angle(toHer, toGoal) < 35f;
        }

        // Every frame: a throw it can see coming. It reacts after its reaction time, and steps
        // aside with its chance of doing so; once a throw is decided, it's decided. True when it
        // has chosen to dodge.
        bool WatchThrows()
        {
            if (!reactive || dodgeSide != null) return false;
            if (!EnvWorld.SeesThrow(PlayerPresence.Current, out EnvWorld.ThrowSeen t) || decided.Contains(t.Id)) return false;
            if (t.Id != watching)
            {
                watching = t.Id;
                watchingSince = Time.time;
                ThrowsSeen++;
                Feel(Stimulus.Chase, 0.5f);
            }
            if (Time.time - watchingSince < traits.DodgeReaction) return false;
            decided.Add(t.Id);
            if (random.NextDouble() >= traits.DodgeChance) return false;
            dodgeSide = t.StepTo;
            return true;
        }

        IEnumerator Sidestep()
        {
            string side = dodgeSide;
            dodgeSide = null;
            Dodges++;
            yield return env.Act(new EnvAction { verb = "sidestep", target = side, sprint = true, timeout = 1.5f });
        }

        // An action of its own (backing off, hiding) with only the throw watch alongside.
        IEnumerator Act(EnvAction a)
        {
            IEnumerator run = env.Act(a);
            while (run.MoveNext())
            {
                if (WatchThrows()) env.Cancel();
                yield return run.Current;
            }
            if (dodgeSide != null) yield return Sidestep();
        }

        // A startle's after-effects: freeze for a moment, maybe drop what's in hand.
        IEnumerator AfterStartle(Dictionary<string, object> obs)
        {
            if (traits.DropChance > 0f && random.NextDouble() < traits.DropChance && IsHolding(obs))
                yield return env.Act(new EnvAction { verb = "drop" });
            if (traits.FreezeOnStartle)
                yield return env.Act(new EnvAction { verb = "wait", seconds = 1f + (float)random.NextDouble() * 2f });
        }

        // ---- keeping out of reach -------------------------------------------------------------

        // Too close: in view and nearer than this player lets her get, or her footsteps right
        // behind. `threat` is where she is, as a person would place her.
        bool TooClose(Dictionary<string, object> obs, out Vector3 threat)
        {
            threat = default;
            var karen = (Dictionary<string, object>)obs["karen"];
            if ((bool)karen["visible"] && (float)karen["distance_m"] < traits.KeepClear)
            {
                threat = Point(karen["at"]);
                return true;
            }
            if (traits.StepsClear <= 0f || !obs.TryGetValue("heard", out object h) || !(h is List<object> heard)) return false;
            foreach (var e in heard.Cast<Dictionary<string, object>>())
                if ((string)e["category"] == "steps" && (float)e["age_s"] < 1f && (float)e["distance_m"] < traits.StepsClear)
                {
                    threat = Point(e["at"]);
                    return true;
                }
            return false;
        }

        // Backs off at a run to somewhere near whose way leads away from her, not past her.
        // Cornered, it holds still a moment and looks again.
        IEnumerator BackOff(Vector3 threat)
        {
            BackOffs++;
            string there = env.World.Map.NameAt(threat);
            Shun(there);
            lastThreatRegion = there;
            Threat(threat);

            Vector3 me = env.Driver.transform.position;
            Vector3 away = Flat(me - threat);
            away = away.sqrMagnitude > 0.01f ? away.normalized : -Flat(env.Driver.transform.forward).normalized;
            float now = Flat(me - threat).magnitude;
            var options = env.World.Map.Regions
                .Where(r => !r.IsDoor)
                .Select(r => (r, d: Flat(r.Centroid - me)))
                .Where(x => x.d.magnitude >= 5f && x.d.magnitude <= 18f && Vector3.Dot(x.d.normalized, away) > 0.2f)
                .OrderByDescending(x => Vector3.Dot(x.d.normalized, away) + (float)random.NextDouble() * 0.3f)
                .Select(x => x.r)
                .Take(8)
                .ToList();
            Region to = options.FirstOrDefault(r => WayKeepsClear(r.Centroid, threat, Mathf.Min(now * 0.9f, Mathf.Max(2.5f, traits.KeepClear * 0.7f))))
                        ?? options.FirstOrDefault(r => WayKeepsClear(r.Centroid, threat, Mathf.Min(now * 0.9f, 2f)));
            if (to == null)
            {
                yield return env.Act(new EnvAction { verb = "wait", seconds = 0.5f });
                yield break;
            }
            Log(new EmployeeChoice { What = EmployeeWhat.BackOff, How = EmployeeHow.Hurry, By = "reflex", Verb = "move_to", Target = to.Name, Chance = 1f }, to.Centroid);
            yield return Act(new EnvAction { verb = "move_to", target = to.Name, sprint = true, timeout = 8f });
        }

        // Whether the walk from here to `to` stays at least `clearance` from the threat all the
        // way: the store's layout, which any player knows, not where she's going.
        bool WayKeepsClear(Vector3 to, Vector3 threat, float clearance)
        {
            if (!NavMesh.SamplePosition(env.Driver.transform.position, out NavMeshHit from, 2f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to, out NavMeshHit end, 2f, NavMesh.AllAreas))
                return false;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from.position, end.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                return false;
            Vector3[] c = path.corners;
            for (int i = 1; i < c.Length; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Flat(c[i] - c[i - 1]).magnitude));
                for (int k = 1; k <= steps; k++)
                    if (Flat(Vector3.Lerp(c[i - 1], c[i], k / (float)steps) - threat).magnitude < clearance) return false;
            }
            return true;
        }

        // ---- the static control ------------------------------------------------------------

        // Runs alongside an action and cancels it when she's seen (the old skittish bot).
        IEnumerator Watch()
        {
            while (true)
            {
                if (Time.time > calmUntil && Threatened(env.World.Observe(env))) env.Cancel();
                yield return null;
            }
        }

        bool Threatened(Dictionary<string, object> obs)
        {
            var karen = (Dictionary<string, object>)obs["karen"];
            if ((bool)karen["visible"])
            {
                lastThreatRegion = (string)karen["region"];
                if ((float)karen["distance_m"] < 16f) return true;
            }
            return (bool)karen["heard"];
        }

        // ---- hiding --------------------------------------------------------------------------

        // Run to the far end of the store from where the threat was, then crouch and wait it out.
        IEnumerator Flee()
        {
            Vector3 me = env.Driver.transform.position;
            StoreMap map = env.World.Map;
            Region threat = lastThreatRegion != null ? map.Regions.FirstOrDefault(r => r.Name == lastThreatRegion) : null;
            bool placed = reactive && lastThreatAt.HasValue && Time.time - lastThreatTime < 15f;
            Vector3 from = placed ? lastThreatAt.Value : threat != null ? threat.Centroid : me;

            var options = map.Regions
                .Where(r => !r.IsDoor && r.Area == "Sales floor" && r.Cells.Count >= 4 && !Shunned(r.Name))
                .OrderByDescending(r => Vector3.Distance(r.Centroid, from) - Vector3.Distance(r.Centroid, me) * 0.3f + (float)random.NextDouble() * 6f)
                .ToList();
            // The reacting players don't run past her to get there.
            Region hide = placed ? options.Take(8).FirstOrDefault(r => WayKeepsClear(r.Centroid, from, 2.5f)) ?? options.FirstOrDefault() : options.FirstOrDefault();
            if (hide == null) yield break;

            Hides++;
            Memory.Hid(hide.Name);
            if (!MindActive) Log(new EmployeeChoice { What = EmployeeWhat.Hide, How = EmployeeHow.Hurry, By = "reflex", Verb = "move_to", Target = hide.Name, Chance = 1f }, hide.Centroid);
            yield return Act(new EnvAction { verb = "move_to", target = hide.Name, sprint = true, timeout = 25f });
            env.Driver.Crouch = true;
            float wait = profile == PlayerProfile.Cautious ? 10f : 6f;
            yield return env.Act(new EnvAction { verb = "wait", seconds = wait + (float)random.NextDouble() * 6f });
            env.Driver.Crouch = false;
            calmUntil = Time.time + 8f;
        }

        // ---- choosing what to do -------------------------------------------------------------

        EnvAction Choose(Dictionary<string, object> obs)
        {
            bool sprint = profile == PlayerProfile.Reckless || (reactive && fear.Fear >= traits.SprintAt);
            var you = (Dictionary<string, object>)obs["you"];
            float energy = (float)you["energy"];
            bool storeOpen = (bool)obs["store_open"];
            var power = (Dictionary<string, object>)obs["power"];

            // Lights out: fix the breakers, lowest hum first.
            if (!(bool)power["lights_on"] && power.TryGetValue("breakers", out object b))
            {
                var next = ((List<object>)b).Cast<Dictionary<string, object>>()
                    .Where(x => !(bool)x["on"]).OrderBy(x => (int)x["hum_pitch_rank"]).FirstOrDefault();
                if (next != null && !Avoided("flip_breaker", (string)next["id"]))
                    return new EnvAction { verb = "flip_breaker", target = (string)next["id"], sprint = sprint };
            }

            if (energy < 0.25f && profile != PlayerProfile.Reckless && !Avoided("drink_coffee", null))
                return new EnvAction { verb = "drink_coffee", sprint = false };

            // A queue at the till is the most expensive thing to ignore.
            var queue = List(obs, "checkout_queue");
            var longest = queue.Where(q => !Avoided("serve", (string)q["id"])).OrderByDescending(q => (float)q["waited_s"]).FirstOrDefault();
            if (longest != null && ((float)longest["waited_s"] > 12f || profile == PlayerProfile.Efficient))
                return new EnvAction { verb = "serve", target = (string)longest["id"], sprint = sprint || Far(longest) };

            // Someone asking, or someone whose escort was cut short and is still waiting. The
            // shift can't end while anyone is.
            var asking = List(obs, "customers_asking").FirstOrDefault(a => (string)a["stage"] != "Talking" && !Avoided("help", (string)a["id"]));
            if (asking != null)
            {
                bool closing = !storeOpen;
                bool accept = closing || profile == PlayerProfile.Reckless || (profile == PlayerProfile.Efficient && random.NextDouble() < 0.5);
                return new EnvAction { verb = "help", target = (string)asking["id"], accept = accept, sprint = sprint };
            }

            // Carrying a bag? Take it out.
            if (Holding(obs, "trash bag"))
                return Unless(new EnvAction { verb = "dispose", sprint = sprint || energy > 0.6f }, new EnvAction { verb = "drop" });

            var spills = List(obs, "spills").Where(s => !Avoided("mop", (string)s["id"])).OrderBy(s => Cost(s)).ToList();
            var shelves = List(obs, "shelves_to_restock").Where(s => !Avoided("restock", (string)s["id"])).OrderBy(s => Cost(s)).ToList();
            var bins = List(obs, "bins").Where(x => (int)x["fill"] > 0 && !Avoided("bag_trash", (string)x["id"])).OrderBy(x => Cost(x)).ToList();

            // Nearest job first — counting the long way round any region she was just seen or
            // heard in — fetching the tool it needs if it isn't in the bag.
            var jobs = new List<(float d, string kind, string id)>();
            if (spills.Count > 0) jobs.Add((Cost(spills[0]), "mop", (string)spills[0]["id"]));
            if (shelves.Count > 0) jobs.Add((Cost(shelves[0]), "restock", (string)shelves[0]["id"]));
            if (bins.Count > 0 && (int)bins[0]["fill"] >= (profile == PlayerProfile.Efficient ? 1 : 2))
                jobs.Add((Cost(bins[0]), "bag_trash", (string)bins[0]["id"]));

            // Her tricks' chores (0.3.0): the fog box, the stopped clock, the WC mop she hid, a box
            // behind a door, Kenji, her cameras. Jobs like the rest, nearest first; the clock comes
            // first once the doors close, since nobody clocks out while it's stopped.
            if (obs.TryGetValue("karen_chores", out object choresObj))
                foreach (var chore in ((List<object>)choresObj).Cast<Dictionary<string, object>>())
                {
                    string kind = (string)chore["kind"], id = (string)chore["id"];
                    string verb = ChoreVerb(kind);
                    if (verb == null || Avoided(verb, id)) continue;
                    if (kind == "camera" && (profile == PlayerProfile.Reckless || (float)chore["walk_m"] > 25f)) continue;
                    if (kind == "clock" && !storeOpen) return new EnvAction { verb = verb, target = id, sprint = sprint };
                    jobs.Add((Cost(chore), verb, id));
                }

            if (jobs.Count > 0)
            {
                var job = jobs.OrderBy(j => j.d < 0 ? 999f : j.d).First();
                string tool = job.kind == "mop" ? "mop" : job.kind == "restock" ? "crate" : null;
                bool hurry = sprint || traits.Hurries && energy > 0.5f && job.d > 25f;
                if (tool != null && !Carries(obs, tool))
                    return Unless(new EnvAction { verb = "pick_up", target = tool, sprint = hurry }, Idle());
                return new EnvAction { verb = job.kind, target = job.id, sprint = hurry };
            }

            // Nothing left that can be seen and the doors are shut: try the time clock. The HUD
            // is only a hint — Karen can make it lie either way — and a refusal is backed off.
            if (!storeOpen)
                return Unless(new EnvAction { verb = "clock_out", sprint = sprint }, Idle());

            return Idle();
        }

        static string ChoreVerb(string kind)
        {
            switch (kind)
            {
                case "fog": return "reset_fog";
                case "clock": return "restart_clock";
                case "lost_tool": return "find_tool";
                case "blocked_door": return "unblock_door";
                case "kenji": return "send_kenji";
                case "camera": return "unplug_camera";
                default: return null;
            }
        }

        // A job's walk, plus the detour a frightened player makes round where she was.
        float Cost(Dictionary<string, object> x)
        {
            float walk = (float)x["walk_m"];
            if (walk < 0f) return walk;
            return reactive && x.TryGetValue("region", out object r) && Shunned(r as string) ? walk + traits.AvoidPenalty : walk;
        }

        // Nothing to do: wait by the tills for the next customer.
        EnvAction Idle() => random.NextDouble() < 0.5 && !Avoided("move_to", "checkout_2")
            ? new EnvAction { verb = "move_to", target = "checkout_2", sprint = false }
            : new EnvAction { verb = "wait", seconds = 2f };

        static List<Dictionary<string, object>> List(Dictionary<string, object> obs, string key) =>
            ((List<object>)obs[key]).Cast<Dictionary<string, object>>().ToList();

        static bool Far(Dictionary<string, object> x) => x.TryGetValue("walk_m", out object d) && (float)d > 25f;

        static bool IsHolding(Dictionary<string, object> obs) =>
            ((Dictionary<string, object>)obs["you"])["holding"] is string;

        static bool Holding(Dictionary<string, object> obs, string name) =>
            ((Dictionary<string, object>)obs["you"])["holding"] as string == name ||
            Carries(obs, name);

        static bool Carries(Dictionary<string, object> obs, string name) =>
            ((List<string>)((Dictionary<string, object>)obs["you"])["inventory"]).Contains(name);

        // ---- 0.4.0: the options, the log, the trained mind (build plan §7.3) ----------------------

        bool MindActive => mind != null && mind.Active;

        Vector3 Here => env.Driver != null ? env.Driver.transform.position : Vector3.zero;

        int JobsDone
        {
            get
            {
                EpisodeMetrics m = env.Metrics;
                return m.SpillsMopped + m.ShelvesRestocked + m.BinsBagged + m.BagsDisposed + m.CustomersServed + m.DirectionsGiven;
            }
        }

        static bool IsJob(EmployeeWhat w) =>
            w == EmployeeWhat.ServeTill || w == EmployeeWhat.HelpShopper || w == EmployeeWhat.Restock || w == EmployeeWhat.Mop ||
            w == EmployeeWhat.Bins || w == EmployeeWhat.BagOut || w == EmployeeWhat.AnswerChore;

        // The heuristic's action, named as one of the options. Draws nothing.
        EmployeeChoice Tag(EnvAction a, Dictionary<string, object> obs, string by) => new EmployeeChoice
        {
            What = EmployeeOptions.WhatOf(a, Holding(obs, "trash bag")),
            How = EmployeeOptions.HowOf(a),
            Which = EmployeeWhich.Nearest,
            Chance = 1f,
            By = by
        };

        // Every choice, with where it leads (the shift record's "choice" events, for legs.py).
        void Log(EmployeeChoice c, Vector3 leadsTo, bool done = true)
        {
            Choices++;
            ShiftRecorder.Annotate("choice", EmployeeOptions.Describe(c) + "|" + (done ? "ok" : "cut"), leadsTo);
        }

        // Asks the mind; pays it for what happened since its last choice.
        bool MindChooses(Dictionary<string, object> obs, out EmployeeChoice choice)
        {
            PayMind(false, false, false);
            float[] inputs = mindInputs = EmployeeObservation.Build(obs, fear.Fear, Memory, persona, lastWhat, Time.time);
            bool[] what = mindWhat = EmployeeOptions.MaskWhat(obs);
            bool[] which = mindWhich = EmployeeOptions.MaskWhich(Memory, lastThreatAt.HasValue);
            // The heuristic's own pick is always among the options; a demonstration must be legal.
            what[(int)EmployeeWhat.Wait] = true;
            if (!mind.TryChoose(inputs, what, EmployeeOptions.MaskHow(), which, persona, random, out choice)) return false;
            if (choice.Which == EmployeeWhich.Usual) usedHabit = true;
            return true;
        }

        void PayMind(bool clockedOut, bool caught, bool timedOut)
        {
            if (mind == null) return;
            int jobs = JobsDone, lost = env.Metrics.CustomersLost;
            var outcome = new EmployeeOutcome
            {
                Jobs = jobs - jobsAtDecision, CustomersLost = Mathf.Max(0, lost - lostAtDecision),
                FearSeconds = fearSeconds, SprintSeconds = sprintSeconds, UsedHabit = usedHabit, LookedOnce = lookedOnce,
                ClockedOut = clockedOut, Caught = caught, TimedOut = timedOut
            };
            mind.AddReward(EmployeeReward.Pay(persona.Weights, outcome));
            jobsAtDecision = jobs;
            lostAtDecision = lost;
            fearSeconds = sprintSeconds = 0f;
            usedHabit = lookedOnce = false;
        }

        void FinishShift()
        {
            EpisodeMetrics m = env.Metrics;
            bool caught = !m.ClockedOut && !m.TimedOut;
            Memory.EndShift(caught, caught ? env.World.Map.NameAt(Here) : null);
            if (mind == null) return;
            PayMind(m.ClockedOut, caught, m.TimedOut);
            mind.EndShift(CareerOver);
        }

        // A mind's choice made into an action, with the same helpers the heuristic uses. Null: it
        // found nothing to do that way, and the heuristic picks.
        EnvAction Realise(EmployeeChoice c, Dictionary<string, object> obs)
        {
            bool sprint = c.How == EmployeeHow.Hurry, crouch = c.How == EmployeeHow.Sneak;
            EnvAction A(string verb, string target = null) => new EnvAction { verb = verb, target = target, sprint = sprint, crouch = crouch };
            Dictionary<string, object> Pick(IEnumerable<Dictionary<string, object>> xs) => Which(xs, c.Which);

            switch (c.What)
            {
                case EmployeeWhat.ServeTill:
                    var q = Pick(List(obs, "checkout_queue").Where(x => !Avoided("serve", (string)x["id"])));
                    return q != null ? A("serve", (string)q["id"]) : null;
                case EmployeeWhat.HelpShopper:
                    var ask = Pick(List(obs, "customers_asking").Where(a => (string)a["stage"] != "Talking" && !Avoided("help", (string)a["id"])));
                    if (ask == null) return null;
                    EnvAction help = A("help", (string)ask["id"]);
                    help.accept = true;
                    return help;
                case EmployeeWhat.Restock:
                case EmployeeWhat.Mop:
                    bool mop = c.What == EmployeeWhat.Mop;
                    string verb = mop ? "mop" : "restock";
                    var job = Pick(List(obs, mop ? "spills" : "shelves_to_restock").Where(x => !Avoided(verb, (string)x["id"])));
                    if (job == null) return null;
                    string tool = mop ? "mop" : "crate";
                    return Carries(obs, tool) ? A(verb, (string)job["id"]) : A("pick_up", tool);
                case EmployeeWhat.Bins:
                    var bin = Pick(List(obs, "bins").Where(x => (int)x["fill"] > 0 && !Avoided("bag_trash", (string)x["id"])));
                    return bin != null ? A("bag_trash", (string)bin["id"]) : null;
                case EmployeeWhat.BagOut:
                    return Holding(obs, "trash bag") ? A("dispose") : null;
                case EmployeeWhat.AnswerChore:
                    var power = (Dictionary<string, object>)obs["power"];
                    if (!(bool)power["lights_on"] && power.TryGetValue("breakers", out object b))
                    {
                        var off = ((List<object>)b).Cast<Dictionary<string, object>>().Where(x => !(bool)x["on"]).OrderBy(x => (int)x["hum_pitch_rank"]).FirstOrDefault();
                        if (off != null) return A("flip_breaker", (string)off["id"]);
                    }
                    if (!obs.TryGetValue("karen_chores", out object ch)) return null;
                    var chore = Pick(((List<object>)ch).Cast<Dictionary<string, object>>().Where(x => ChoreVerb((string)x["kind"]) != null));
                    if (chore == null) return null;
                    if (!curiousDone) { lookedOnce = true; curiousDone = true; }
                    return A(ChoreVerb((string)chore["kind"]), (string)chore["id"]);
                case EmployeeWhat.Coffee: return A("drink_coffee");
                case EmployeeWhat.ClockOut: return A("clock_out");
                case EmployeeWhat.GoRound:
                    // The long way: somewhere 5–18 m off that keeps clear of where she was.
                    Vector3 threat = lastThreatAt ?? Here;
                    Region r = env.World.Map.Regions.Where(x => !x.IsDoor && Vector3.Distance(x.Centroid, Here) is float d && d >= 5f && d <= 18f)
                        .OrderByDescending(x => Vector3.Distance(x.Centroid, threat)).FirstOrDefault();
                    return r != null ? A("move_to", r.Name) : null;
                case EmployeeWhat.StopAndLook:
                    env.Driver.lookJitter = Mathf.Max(env.Driver.lookJitter, 1f);
                    return new EnvAction { verb = "wait", seconds = 1.5f };
                default:
                    return new EnvAction { verb = "wait", seconds = 2f };
            }
        }

        // Nearest (the shortest walk), usual (a region it's used to), or safest (furthest from
        // where she last was).
        Dictionary<string, object> Which(IEnumerable<Dictionary<string, object>> xs, EmployeeWhich which)
        {
            var list = xs.ToList();
            if (list.Count == 0) return null;
            switch (which)
            {
                case EmployeeWhich.Safest when lastThreatAt.HasValue:
                    Vector3 her = lastThreatAt.Value;
                    return list.OrderByDescending(x => x.TryGetValue("region", out object rg) && rg is string name && env.World.Map.Regions.FirstOrDefault(r => r.Name == name) is Region reg
                        ? Vector3.Distance(reg.Centroid, her) : 0f).First();
                case EmployeeWhich.Usual:
                    string usual = Memory.UsualHide();
                    var same = list.FirstOrDefault(x => x.TryGetValue("region", out object rg) && rg as string == usual);
                    if (same != null) return same;
                    goto default;
                default:
                    return list.OrderBy(x => { float d = Cost(x); return d < 0 ? 999f : d; }).First();
            }
        }
    }
}
