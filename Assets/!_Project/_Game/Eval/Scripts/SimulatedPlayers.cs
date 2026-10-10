using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Eval
{
    // Karen.md §13: three scripted player profiles. The Ledger should converge to visibly
    // different tactic distributions for each; if it doesn't, the bandit isn't learning and
    // the reward signal is broken. They play through exactly the same actions an external
    // agent would, with the same senses: Karen only when she's in view, her footsteps only
    // when they're close.
    public enum PlayerProfile
    {
        Efficient,   // nearest job first, walks, mostly ignores her
        Skittish,    // over-reacts: flees on sight, hides, keeps looking behind
        Reckless     // sprints everywhere, throws things, ignores noise entirely
    }

    public sealed class SimulatedPlayer
    {
        readonly KehaiEnv env;
        readonly PlayerProfile profile;
        readonly System.Random random;
        readonly Dictionary<string, float> avoidUntil = new Dictionary<string, float>();
        float calmUntil;

        public SimulatedPlayer(KehaiEnv env, PlayerProfile profile, int seed)
        {
            this.env = env;
            this.profile = profile;
            random = new System.Random(seed);
        }

        public string Name => profile.ToString().ToLowerInvariant();

        // Plays until the episode is done.
        public IEnumerator PlayShift()
        {
            env.Driver.lookJitter = profile == PlayerProfile.Skittish ? 0.9f : profile == PlayerProfile.Reckless ? 0.2f : 0f;

            while (!env.CheckDone())
            {
                var obs = env.World.Observe(env);
                EnvAction action = Choose(obs);

                // Skittish players abandon whatever they're doing the moment she appears.
                IEnumerator run = env.Act(action);
                IEnumerator watch = profile == PlayerProfile.Skittish ? Watch() : null;
                while (run.MoveNext())
                {
                    watch?.MoveNext();
                    yield return run.Current;
                }

                // A job that just failed (no path, wrong tool, already done) is left alone for a
                // while rather than retried every frame.
                if (env.LastResult != null && !(bool)env.LastResult["ok"])
                    avoidUntil[Key(action.verb, action.target)] = Time.time + 15f;

                if (profile == PlayerProfile.Skittish && Threatened(env.World.Observe(env), out Vector3 _))
                    yield return Flee();

                yield return null;
            }
        }

        static string Key(string verb, string target) => verb + ":" + target;

        bool Avoided(string verb, string target) =>
            avoidUntil.TryGetValue(Key(verb, target), out float until) && Time.time < until;

        EnvAction Unless(EnvAction a, EnvAction otherwise) => a != null && !Avoided(a.verb, a.target) ? a : otherwise;

        // Runs alongside an action and cancels it when she's seen.
        IEnumerator Watch()
        {
            while (true)
            {
                if (Time.time > calmUntil && Threatened(env.World.Observe(env), out _)) env.Cancel();
                yield return null;
            }
        }

        static bool Threatened(Dictionary<string, object> obs, out Vector3 _)
        {
            _ = default;
            var karen = (Dictionary<string, object>)obs["karen"];
            if ((bool)karen["visible"] && karen.TryGetValue("distance_m", out object d) && (float)d < 16f) return true;
            return (bool)karen["heard"];
        }

        // Run to the far end of the store, then crouch in the dark and wait it out.
        IEnumerator Flee()
        {
            KarenBrain brain = KarenBrain.Instance;
            Vector3 her = brain != null ? brain.Body.Position : env.Driver.transform.position;
            Vector3 me = env.Driver.transform.position;
            var map = env.World.Map;

            var hide = map.Regions
                .Where(r => !r.IsDoor && r.Area == "Sales floor" && r.Cells.Count >= 4)
                .OrderByDescending(r => Vector3.Distance(r.Centroid, her) - Vector3.Distance(r.Centroid, me) * 0.3f + (float)random.NextDouble() * 6f)
                .FirstOrDefault();
            if (hide == null) yield break;

            yield return env.Act(new EnvAction { verb = "move_to", target = hide.Name, sprint = true, timeout = 25f });
            env.Driver.Crouch = true;
            yield return env.Act(new EnvAction { verb = "wait", seconds = 6f + (float)random.NextDouble() * 6f });
            env.Driver.Crouch = false;
            calmUntil = Time.time + 8f;
        }

        EnvAction Choose(Dictionary<string, object> obs)
        {
            bool sprint = profile == PlayerProfile.Reckless;
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

            var spills = List(obs, "spills").Where(s => !Avoided("mop", (string)s["id"])).OrderBy(s => (float)s["walk_m"]).ToList();
            var shelves = List(obs, "shelves_to_restock").Where(s => !Avoided("restock", (string)s["id"])).OrderBy(s => (float)s["walk_m"]).ToList();
            var bins = List(obs, "bins").Where(x => (int)x["fill"] > 0 && !Avoided("bag_trash", (string)x["id"])).OrderBy(x => (float)x["walk_m"]).ToList();

            // Nearest job first, fetching the tool it needs if it isn't in the bag.
            var jobs = new List<(float d, string kind, string id)>();
            if (spills.Count > 0) jobs.Add(((float)spills[0]["walk_m"], "mop", (string)spills[0]["id"]));
            if (shelves.Count > 0) jobs.Add(((float)shelves[0]["walk_m"], "restock", (string)shelves[0]["id"]));
            if (bins.Count > 0 && (int)bins[0]["fill"] >= (profile == PlayerProfile.Efficient ? 1 : 2))
                jobs.Add(((float)bins[0]["walk_m"], "bag_trash", (string)bins[0]["id"]));

            if (jobs.Count > 0)
            {
                var job = jobs.OrderBy(j => j.d < 0 ? 999f : j.d).First();
                string tool = job.kind == "mop" ? "mop" : job.kind == "restock" ? "crate" : null;
                bool hurry = sprint || energy > 0.5f && job.d > 25f;
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

        // Nothing to do: wait by the tills for the next customer.
        EnvAction Idle() => random.NextDouble() < 0.5 && !Avoided("move_to", "checkout_2")
            ? new EnvAction { verb = "move_to", target = "checkout_2", sprint = false }
            : new EnvAction { verb = "wait", seconds = 2f };

        static List<Dictionary<string, object>> List(Dictionary<string, object> obs, string key) =>
            ((List<object>)obs[key]).Cast<Dictionary<string, object>>().ToList();

        static bool Far(Dictionary<string, object> x) => x.TryGetValue("walk_m", out object d) && (float)d > 25f;

        static bool Holding(Dictionary<string, object> obs, string name) =>
            ((Dictionary<string, object>)obs["you"])["holding"] as string == name ||
            Carries(obs, name);

        static bool Carries(Dictionary<string, object> obs, string name) =>
            ((List<string>)((Dictionary<string, object>)obs["you"])["inventory"]).Contains(name);
    }
}
