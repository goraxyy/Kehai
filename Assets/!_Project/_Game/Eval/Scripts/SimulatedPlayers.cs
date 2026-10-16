using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

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
    // and they go back to work.
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

        public int Hides { get; private set; }

        public SimulatedPlayer(KehaiEnv env, PlayerProfile profile, int seed, bool reactive = true)
        {
            this.env = env;
            this.profile = profile;
            this.reactive = reactive;
            random = new System.Random(seed);
            traits = BotTraits.For(profile, seed);
        }

        public string Name => profile.ToString().ToLowerInvariant();

        // Plays until the episode is done.
        public IEnumerator PlayShift()
        {
            lastTick = Time.time;
            Look();

            while (!env.CheckDone())
            {
                var obs = env.World.Observe(env);
                Sense(obs);

                if (reactive && fear.Fear >= traits.HideAt && Time.time > calmUntil)
                {
                    yield return Flee();
                    continue;
                }

                EnvAction action = Choose(obs);
                IEnumerator run = env.Act(action);
                IEnumerator monitor = reactive ? Monitor(action) : profile == PlayerProfile.Skittish ? Watch() : null;
                while (run.MoveNext())
                {
                    monitor?.MoveNext();
                    yield return run.Current;
                }

                // A job that just failed (no path, wrong tool, already done) is left alone for a
                // while rather than retried every frame.
                if (env.LastResult != null && !(bool)env.LastResult["ok"])
                    avoidUntil[Key(action.verb, action.target)] = Time.time + 15f;

                if (reactive && startled)
                {
                    startled = false;
                    yield return AfterStartle(env.World.Observe(env));
                }
                else if (!reactive && profile == PlayerProfile.Skittish && Threatened(env.World.Observe(env)))
                    yield return Flee();

                yield return null;
            }

            env.Metrics.BotMeanFear = fear.MeanFear;
            env.Metrics.BotPeakFear = fear.Peak;
            env.Metrics.BotStartles = fear.Startles;
            env.Metrics.BotHides = Hides;
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
                    }
                }

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

        // Runs alongside an action: keeps sensing, sprints while frightened, and drops the job
        // on a big enough startle — or to go and hide.
        IEnumerator Monitor(EnvAction action)
        {
            float next = 0f;
            while (true)
            {
                if (Time.time >= next)
                {
                    next = Time.time + 0.25f;
                    Sense(env.World.Observe(env));
                    env.Driver.Sprint = action.sprint || fear.Fear >= traits.SprintAt;
                    bool hide = fear.Fear >= traits.HideAt && Time.time > calmUntil;
                    if ((startled && fear.Fear >= traits.AbandonAt) || hide) env.Cancel();
                }
                yield return null;
            }
        }

        // A startle's after-effects: freeze for a moment, maybe drop what's in hand.
        IEnumerator AfterStartle(Dictionary<string, object> obs)
        {
            if (traits.DropChance > 0f && random.NextDouble() < traits.DropChance && IsHolding(obs))
                yield return env.Act(new EnvAction { verb = "drop" });
            if (traits.FreezeOnStartle)
                yield return env.Act(new EnvAction { verb = "wait", seconds = 1f + (float)random.NextDouble() * 2f });
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
            Vector3 from = threat != null ? threat.Centroid : me;

            var hide = map.Regions
                .Where(r => !r.IsDoor && r.Area == "Sales floor" && r.Cells.Count >= 4 && !Shunned(r.Name))
                .OrderByDescending(r => Vector3.Distance(r.Centroid, from) - Vector3.Distance(r.Centroid, me) * 0.3f + (float)random.NextDouble() * 6f)
                .FirstOrDefault();
            if (hide == null) yield break;

            Hides++;
            yield return env.Act(new EnvAction { verb = "move_to", target = hide.Name, sprint = true, timeout = 25f });
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
    }
}
