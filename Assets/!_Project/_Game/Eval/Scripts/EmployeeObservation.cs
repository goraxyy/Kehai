using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kehai.Eval
{
    // What a simulated player's trained mind sees (0.4.0, build plan §7.3, C2.4): its own senses
    // only, from EnvWorld.Observe (the same dictionary an outside agent gets), its fear, its
    // memory and its persona. Plain C#, tested without the ML-Agents package; nothing here reads
    // Karen (BotFearTests.Players_NeverReadKaren covers this file).
    public static class EmployeeObservation
    {
        public const int Version = 1;
        const int Self = 7, Jobs = 11, Chores = 8, Her = 8, Store = 3, Memory = 6;
        public static readonly string[] ChoreKinds = { "fog", "clock", "lost_tool", "blocked_door", "kenji", "camera" };

        public static int Size => Self + Jobs + Chores + Her + Store + Memory + Persona.Count + EmployeeOptions.Whats;

        public static float[] Build(Dictionary<string, object> obs, float fear, BotMemory memory, Persona persona, EmployeeWhat? last, float now)
        {
            var v = new float[Size];
            int i = 0;
            var you = Obj(obs, "you");

            // ---- itself ----
            v[i++] = Num(you, "energy");
            v[i++] = EmployeeOptions.Holding(obs, "trash bag") ? 1f : 0f;
            v[i++] = EmployeeOptions.Holding(obs, "mop") ? 1f : 0f;
            v[i++] = EmployeeOptions.Holding(obs, "crate") ? 1f : 0f;
            v[i++] = Mathf.Clamp01(fear);
            v[i++] = Mathf.Clamp01(Num(obs, "time_remaining") / 300f);
            v[i++] = Bool(obs, "store_open") ? 1f : 0f;

            // ---- the jobs: how many of each kind, how far the nearest; the longest wait ----
            foreach (string key in new[] { "checkout_queue", "customers_asking", "shelves_to_restock", "spills", "bins" })
            {
                var list = EmployeeOptions.List(obs, key);
                if (key == "bins") list = list.Where(x => Num(x, "fill") > 0f).ToList();
                v[i++] = Squash(list.Count, 3f);
                float nearest = list.Select(x => Num(x, "walk_m", -1f)).Where(d => d >= 0f).DefaultIfEmpty(-1f).Min();
                v[i++] = nearest < 0f ? 1f : Squash(nearest, 20f);
            }
            v[i++] = Squash(EmployeeOptions.List(obs, "checkout_queue").Select(q => Num(q, "waited_s")).DefaultIfEmpty(0f).Max(), 30f);

            // ---- her chores, and the lights ----
            var chores = EmployeeOptions.List(obs, "karen_chores");
            foreach (string kind in ChoreKinds) v[i++] = chores.Any(c => Str(c, "kind") == kind) ? 1f : 0f;
            float choreWalk = chores.Select(c => Num(c, "walk_m", -1f)).Where(d => d >= 0f).DefaultIfEmpty(-1f).Min();
            v[i++] = choreWalk < 0f ? 1f : Squash(choreWalk, 20f);
            var power = Obj(obs, "power");
            v[i++] = power != null && power.TryGetValue("lights_on", out object lit) && lit is bool on && !on ? 1f : 0f;

            // ---- her, as it senses her ----
            var karen = Obj(obs, "karen");
            bool visible = karen != null && Bool(karen, "visible");
            v[i++] = visible ? 1f : 0f;
            v[i++] = visible ? Squash(Num(karen, "distance_m"), 10f) : 1f;
            Vector2 dir = Vector2.zero;
            if (visible && karen.TryGetValue("at", out object at) && at is float[] her && you != null && you.TryGetValue("at", out object me) && me is float[] mine)
                dir = new Vector2(her[0] - mine[0], her[1] - mine[1]).normalized;
            v[i++] = dir.x;
            v[i++] = dir.y;
            v[i++] = visible && Str(karen, "eye_colour") == "hunt" ? 1f : 0f;
            v[i++] = karen != null && karen.TryGetValue("last_seen_s", out object ago) ? Squash(Num(ago), 20f) : 1f;
            v[i++] = karen != null && Bool(karen, "heard") ? 1f : 0f;
            var heard = EmployeeOptions.List(obs, "heard");
            v[i++] = Squash(heard.Count(h => Num(h, "age_s") < 5f), 2f);

            // ---- the store ----
            v[i++] = power != null && power.TryGetValue("lights_on", out object l2) && l2 is bool on2 && on2 ? 1f : 0f;
            v[i++] = chores.Any(c => Str(c, "kind") == "fog") ? 1f : 0f;
            v[i++] = Squash(chores.Count, 2f);

            // ---- its memory ----
            v[i++] = memory != null ? memory.Confidence : 1f;
            v[i++] = memory != null ? Squash(memory.Catches, 2f) : 0f;
            v[i++] = memory != null && memory.UsualHide() != null ? 1f : 0f;
            v[i++] = memory != null ? memory.HideHabit() : 0f;
            v[i++] = memory != null ? Squash(memory.GrudgeRegions(2).Count(), 2f) : 0f;
            v[i++] = memory != null && memory.LastSensedRegion(out float when) != null ? Squash(now - when, 30f) : 1f;

            // ---- its persona, and its last choice ----
            for (int k = 0; k < Persona.Count; k++) v[i++] = persona != null ? persona.Traits[k] : 0.5f;
            if (last.HasValue) v[i + (int)last.Value] = 1f;
            i += EmployeeOptions.Whats;

            if (i != v.Length) throw new System.InvalidOperationException($"employee observation wrote {i} of {v.Length}");
            for (int k = 0; k < v.Length; k++) if (float.IsNaN(v[k]) || float.IsInfinity(v[k])) v[k] = 0f;
            return v;
        }

        static float Squash(float x, float scale) => x <= 0f ? 0f : x / (x + scale);

        static Dictionary<string, object> Obj(Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out object v) ? v as Dictionary<string, object> : null;

        static float Num(Dictionary<string, object> o, string key, float fallback = 0f) =>
            o != null && o.TryGetValue(key, out object v) ? Num(v, fallback) : fallback;

        static float Num(object v, float fallback = 0f)
        {
            switch (v)
            {
                case float f: return f;
                case int n: return n;
                case double d: return (float)d;
                case long l: return l;
                default: return fallback;
            }
        }

        static bool Bool(Dictionary<string, object> o, string key) => o != null && o.TryGetValue(key, out object v) && v is bool b && b;
        static string Str(Dictionary<string, object> o, string key) => o != null && o.TryGetValue(key, out object v) ? v as string : null;
    }
}
