using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kehai.Eval
{
    // What a simulated player can choose (0.4.0, build plan §7.3, C2.2): three small choices at once,
    // impossible ones masked.
    public enum EmployeeWhat
    {
        ServeTill, HelpShopper, Restock, Mop, Bins, BagOut, AnswerChore, Coffee, ClockOut,
        GoRound, BackOff, Hide, StopAndLook, Wait
    }

    public enum EmployeeHow { Sneak, Walk, Hurry }

    public enum EmployeeWhich { Nearest, Usual, Safest }

    // One choice: what, how, which, and the action it became. Logged with where it leads: the
    // prediction model's labels (§8.5).
    public struct EmployeeChoice
    {
        public EmployeeWhat What;
        public EmployeeHow How;
        public EmployeeWhich Which;
        public string Verb, Target;
        public float Chance;          // the chance the chooser gave it (1 for the heuristic)
        public string By;             // "heuristic", or the mind's name

        public string Key => What + ":" + How + ":" + Which;
    }

    public static class EmployeeOptions
    {
        public static readonly int Whats = System.Enum.GetValues(typeof(EmployeeWhat)).Length;
        public static readonly int Hows = System.Enum.GetValues(typeof(EmployeeHow)).Length;
        public static readonly int Whiches = System.Enum.GetValues(typeof(EmployeeWhich)).Length;

        // The option an action belongs to: how the scripted heuristic's choices are named, so it
        // picks among the same options a trained mind does.
        public static EmployeeWhat WhatOf(EnvAction a, bool holdingBag)
        {
            switch ((a?.verb ?? "").ToLowerInvariant())
            {
                case "serve": return EmployeeWhat.ServeTill;
                case "help": return EmployeeWhat.HelpShopper;
                case "restock": return EmployeeWhat.Restock;
                case "mop": return EmployeeWhat.Mop;
                case "bag_trash": return EmployeeWhat.Bins;
                case "dispose": return EmployeeWhat.BagOut;
                case "drop": return holdingBag ? EmployeeWhat.BagOut : EmployeeWhat.StopAndLook;
                case "pick_up":
                    return a.target == "mop" ? EmployeeWhat.Mop : a.target == "crate" ? EmployeeWhat.Restock : EmployeeWhat.Wait;
                case "flip_breaker":
                case "reset_fog": case "restart_clock": case "find_tool":
                case "unblock_door": case "send_kenji": case "unplug_camera":
                    return EmployeeWhat.AnswerChore;
                case "drink_coffee": return EmployeeWhat.Coffee;
                case "clock_out": return EmployeeWhat.ClockOut;
                case "sidestep": return EmployeeWhat.BackOff;
                case "move_to": return EmployeeWhat.Wait;     // the heuristic's idle: wait by the tills
                default: return EmployeeWhat.Wait;
            }
        }

        public static EmployeeHow HowOf(EnvAction a) => a == null ? EmployeeHow.Walk : a.sprint ? EmployeeHow.Hurry : a.crouch ? EmployeeHow.Sneak : EmployeeHow.Walk;

        // Which options can run now, from the observation alone (masked options can't be picked).
        public static bool[] MaskWhat(Dictionary<string, object> obs)
        {
            var m = new bool[Whats];
            bool open = obs.TryGetValue("store_open", out object so) && so is bool b && b;
            m[(int)EmployeeWhat.ServeTill] = Count(obs, "checkout_queue") > 0;
            m[(int)EmployeeWhat.HelpShopper] = Count(obs, "customers_asking") > 0;
            m[(int)EmployeeWhat.Restock] = Count(obs, "shelves_to_restock") > 0;
            m[(int)EmployeeWhat.Mop] = Count(obs, "spills") > 0;
            m[(int)EmployeeWhat.Bins] = List(obs, "bins").Any(x => x.TryGetValue("fill", out object f) && f is int i && i > 0);
            m[(int)EmployeeWhat.BagOut] = Holding(obs, "trash bag");
            m[(int)EmployeeWhat.AnswerChore] = Count(obs, "karen_chores") > 0 || LightsOut(obs);
            m[(int)EmployeeWhat.Coffee] = true;
            m[(int)EmployeeWhat.ClockOut] = !open;
            m[(int)EmployeeWhat.GoRound] = m[(int)EmployeeWhat.ServeTill] || m[(int)EmployeeWhat.Restock] || m[(int)EmployeeWhat.Mop] || m[(int)EmployeeWhat.Bins];
            m[(int)EmployeeWhat.BackOff] = true;
            m[(int)EmployeeWhat.Hide] = true;
            m[(int)EmployeeWhat.StopAndLook] = true;
            m[(int)EmployeeWhat.Wait] = true;
            return m;
        }

        // Sneaking is a crouch-walk; every pace is always possible.
        public static bool[] MaskHow() => new[] { true, true, true };

        // "My usual" needs a usual; "the safest" needs somewhere she was.
        public static bool[] MaskWhich(BotMemory memory, bool knowsWhereSheWas) =>
            new[] { true, memory != null && memory.HasHabits, knowsWhereSheWas };

        static bool LightsOut(Dictionary<string, object> obs) =>
            obs.TryGetValue("power", out object p) && p is Dictionary<string, object> power && power.TryGetValue("lights_on", out object on) && on is bool lit && !lit;

        public static bool Holding(Dictionary<string, object> obs, string name)
        {
            if (!(obs.TryGetValue("you", out object y) && y is Dictionary<string, object> you)) return false;
            if (you.TryGetValue("holding", out object h) && h as string == name) return true;
            return you.TryGetValue("inventory", out object inv) && inv is List<string> list && list.Contains(name);
        }

        static int Count(Dictionary<string, object> obs, string key) => obs.TryGetValue(key, out object v) && v is List<object> l ? l.Count : 0;

        public static List<Dictionary<string, object>> List(Dictionary<string, object> obs, string key) =>
            obs.TryGetValue(key, out object v) && v is List<object> l ? l.Cast<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>();

        // ---- the log ----------------------------------------------------------------------------

        // Every choice, with where it leads, for the shift record (Karen's ShiftRecorder) and legs.py.
        // "choice" events: text "what|how|which|verb|target|chance|by", at the destination.
        public static string Describe(EmployeeChoice c) =>
            $"{c.What}|{c.How}|{c.Which}|{c.Verb}|{c.Target}|{c.Chance:0.###}|{c.By}";
    }
}
