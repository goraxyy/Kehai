using System;
using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    public enum StoryKind
    {
        Plan,       // what Karen has decided to do
        Warning,    // a tell: something is about to happen
        Seen,       // she saw you / lost sight of you
        Heard,      // she heard something
        Guess,      // where she thinks you are
        Mood,       // the pace of the shift
        Chase,      // a chase, or a catch
        Learned,    // what she's learning about you
        You,        // your work
        Customer,   // shoppers
        Store,      // the building: lights, the PA, the time clock
        Blink       // your eyes
    }

    public struct StoryLine
    {
        public float Time;          // Time.time when it happened
        public StoryKind Kind;
        public string Text;
        public Vector3 At;
        public bool HasPlace;
    }

    // The shift told in plain words, for a player rather than a programmer: "Karen heard a
    // door in the Stockroom", not "SENSE Hearing door conf 0.62". Karen's brain and the
    // shift recorder say things here; the F1 map, the replay and the shift report read them.
    public static class KarenNarrator
    {
        public static event Action<StoryLine> Said;

        static readonly List<StoryLine> recent = new List<StoryLine>();
        public static IReadOnlyList<StoryLine> Recent => recent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Said = null;
            recent.Clear();
        }

        public static void Say(StoryKind kind, string text) => Say(kind, text, Vector3.zero, false);
        public static void Say(StoryKind kind, string text, Vector3 at) => Say(kind, text, at, true);

        static void Say(StoryKind kind, string text, Vector3 at, bool hasPlace)
        {
            var line = new StoryLine { Time = UnityEngine.Time.time, Kind = kind, Text = text, At = at, HasPlace = hasPlace };
            recent.Add(line);
            if (recent.Count > 200) recent.RemoveRange(0, recent.Count - 200);
            Said?.Invoke(line);
        }

        // ---- words ---------------------------------------------------------------------

        // "Aisle 2/B" → "Aisle 2"; "Doors: Lobby - Sales floor (2)" → "the doors between the
        // Lobby and the Sales floor".
        public static string Place(Vector3 p) => Place(StoreMap.Current.NameAt(p));

        public static string Place(string regionName)
        {
            if (string.IsNullOrEmpty(regionName) || regionName == "off the map") return "somewhere";
            if (regionName.StartsWith("Door"))
            {
                int colon = regionName.IndexOf(':');
                string rooms = colon >= 0 ? regionName.Substring(colon + 1).Trim() : regionName;
                int paren = rooms.IndexOf(" (", StringComparison.Ordinal);
                if (paren >= 0) rooms = rooms.Substring(0, paren);
                string[] ends = rooms.Split(new[] { " - " }, StringSplitOptions.None);
                if (ends.Length != 2) return "a doorway";
                string a = Plain(ends[0]), b = Plain(ends[1]);
                return a == b ? $"a door inside the {a}" : $"the door between the {a} and the {b}";
            }
            int slash = regionName.IndexOf('/');
            string name = Plain(slash > 0 ? regionName.Substring(0, slash) : regionName);
            return name.StartsWith("Aisle") || name.StartsWith("Checkout") ? name : "the " + name;
        }

        // "Stockroom 2" is the far half of the Stockroom to the map; to a person it's the Stockroom.
        static string Plain(string name)
        {
            name = name.Trim();
            if (name.StartsWith("Aisle") || name.StartsWith("Checkout")) return name;
            int space = name.LastIndexOf(' ');
            if (space <= 0) return name;
            for (int i = space + 1; i < name.Length; i++) if (!char.IsDigit(name[i])) return name;
            return name.Substring(0, space);
        }

        // The place with the word that fits in front of it: "in the Staff room", "in Aisle 3",
        // "by the Dairy wall", "at the door between the Lobby and the Sales floor".
        public static string In(Vector3 p) => In(StoreMap.Current.NameAt(p));

        public static string In(string regionName)
        {
            string place = Place(regionName);
            if (place == "somewhere") return "somewhere in the store";
            if (place.Contains("door") || place.StartsWith("Checkout")) return "at " + place;
            if (place.EndsWith("wall")) return "by " + place;
            return "in " + place;
        }

        public static string Sureness(float p)
        {
            if (p >= 0.75f) return "she's certain";
            if (p >= 0.5f) return "she's fairly sure";
            if (p >= 0.25f) return "a good guess";
            return "just a hunch";
        }

        public static string Goal(GoalId goal)
        {
            switch (goal)
            {
                case GoalId.Patrol: return "walking her rounds";
                case GoalId.Investigate: return "going to check out something she noticed";
                case GoalId.Sweep: return "searching the store for you";
                case GoalId.Flush: return "trying to scare you out of hiding";
                case GoalId.Deny: return "undoing your work";
                case GoalId.Herd: return "steering you where she wants you";
                case GoalId.Ambush: return "lying in wait";
                case GoalId.Stalk: return "following you at a distance";
                case GoalId.Pursue: return "coming after you";
                case GoalId.Withdraw: return "backing off for a while";
                case GoalId.Assist: return "being... helpful?";
                default: return goal.ToString().ToLowerInvariant();
            }
        }

        public static string Tactic(string id)
        {
            switch (id)
            {
                case "patrol": return "walking her rounds";
                case "investigate": return "going to check out a noise";
                case "sweep": return "searching the aisles one by one";
                case "poll_witnesses": return "asking customers if they've seen you";
                case "withdraw": return "backing off for a while";
                case "blackout": return "cutting the power to the whole store";
                case "mirror_black": return "killing the lights near you";
                case "fog": return "filling an aisle with freezer fog";
                case "camera_bolt_on": return "bolting a camera over a spot you use";
                case "shelf_sweep": return "emptying a shelf you've already filled";
                case "spill": return "spilling something for you to mop up";
                case "bin_tamper": return "tipping rubbish back into a bin";
                case "task_falsification": return "making your task list lie to you";
                case "tool_theft": return "hiding one of your tools";
                case "overtime": return "getting ready to refuse your clock-out";
                case "pa_decoy": return "announcing a fake job over the speakers";
                case "pa_announce_position": return "telling the whole store where you are";
                case "pa_task_readback": return "reading your to-do list over the speakers — slightly wrong";
                case "pa_countdown": return "counting down over the speakers";
                case "pa_footsteps": return "playing her footsteps through a far-away speaker";
                case "phantom_chime": return "making the front doors chime for nobody";
                case "silence": return "going completely quiet";
                case "crate_wall": return "blocking a route with stacked crates";
                case "door_lock": return "locking a door on your route";
                case "shelf_relocation": return "rolling a shelf into your way";
                case "funnel": return "closing off routes to steer you";
                case "mimicry": return "taking over a customer to watch you";
                case "witness": return "sending a customer to look for you";
                case "understudy": return "sending in a fake customer";
                case "stalk": return "following you at a distance";
                case "ambush": return "waiting somewhere you're about to go";
                case "chase": return "chasing you!";
                case "follow": return "walking after you";
                case "favour": return "doing you a favour";
                case "blink_advance": return "moving while your eyes are shut";
                default: return id.Replace('_', ' ');
            }
        }

        public static string Tell(TellKind kind)
        {
            switch (kind)
            {
                case TellKind.BallastWhine: return "the lights whine and flicker";
                case TellKind.Flicker: return "a light flickers";
                case TellKind.ShelfRattle: return "a shelf rattles";
                case TellKind.BucketClank: return "a bucket clanks";
                case TellKind.LidClatter: return "a bin lid clatters";
                case TellKind.MopRattle: return "the mop rattles on its hook";
                case TellKind.CrtTick: return "your task list ticks";
                case TellKind.Drilling: return "someone's drilling";
                case TellKind.Scrape: return "crates scrape across the floor";
                case TellKind.Clunk: return "a lock clunks";
                case TellKind.FreezerHiss: return "a freezer hisses";
                case TellKind.CustomerFreeze: return "a shopper stops dead";
                case TellKind.Screech: return "a screech — she's seen you";
                case TellKind.SilenceFalls: return "her footsteps stop";
                case TellKind.PunchBuzz: return "the time clock buzzes";
                case TellKind.HoldMusic: return "hold music starts";
                case TellKind.EyeFlicker: return "her eye flickers";
                case TellKind.Grinding: return "shelving grinds on its wheels";
                case TellKind.SpeakerCrackle: return "a speaker crackles";
                case TellKind.DoorMotor: return "the front doors' motor whirs";
                case TellKind.PaChime: return "the speakers chime";
                case TellKind.Footsteps: return "footsteps";
                default: return kind.ToString();
            }
        }

        public static string Noise(NoiseKind kind)
        {
            switch (kind)
            {
                case NoiseKind.Footstep: return "footsteps";
                case NoiseKind.CrouchStep: return "soft footsteps";
                case NoiseKind.Sprint: return "running";
                case NoiseKind.DroppedItem: return "something dropped";
                case NoiseKind.Impact: return "a thud";
                case NoiseKind.KickedItem: return "something kicked";
                case NoiseKind.Mopping: return "mopping";
                case NoiseKind.Stocking: return "shelves being stocked";
                case NoiseKind.TrashRustle: return "a rustling bin bag";
                case NoiseKind.Coffee: return "the coffee machine";
                case NoiseKind.AutoDoor: return "the automatic doors";
                case NoiseKind.Door: return "a door";
                case NoiseKind.TimeClock: return "the time clock";
                case NoiseKind.Serve: return "the till";
                case NoiseKind.CrateClearing: return "crates being moved";
                case NoiseKind.Unplugging: return "a camera being unplugged";
                case NoiseKind.KarenStep: return GameNames.Antagonist + "'s footsteps";
                case NoiseKind.KarenVoice: return GameNames.Antagonist + "'s voice";
                case NoiseKind.Tell: return "a warning sound";
                default: return "a noise";
            }
        }

        // An observation's label, as words: "noise(Door, 0.4, 12m)" → "a door".
        public static string Evidence(string label)
        {
            if (string.IsNullOrEmpty(label)) return "something";
            int open = label.IndexOf('('), comma = label.IndexOf(',');
            string head = open > 0 ? label.Substring(0, open) : label;
            string inner = open > 0 ? label.Substring(open + 1, (comma > open ? comma : label.Length - 1) - open - 1).Trim() : "";
            switch (head.Trim())
            {
                case "noise":
                    return Enum.TryParse(inner, out NoiseKind kind) ? Noise(kind) : "a noise";
                case "trace":
                    return Enum.TryParse(inner, out TraceKind trace) ? TraceWords(trace) : "a trace";
                case "testimony": return "a customer's report";
                case "door sensor": return "a door being used";
                case "till": return "the till being used";
                case "touch": return "contact";
                default: return head.Trim();
            }
        }

        static string TraceWords(TraceKind kind)
        {
            string s = kind.ToString();
            var words = new System.Text.StringBuilder();
            foreach (char ch in s)
            {
                if (char.IsUpper(ch) && words.Length > 0) words.Append(' ');
                words.Append(char.ToLowerInvariant(ch));
            }
            return words.ToString();
        }

        public static string Who(NoiseAuthor author)
        {
            switch (author)
            {
                case NoiseAuthor.Player: return "you";
                case NoiseAuthor.Karen: return GameNames.Antagonist;
                case NoiseAuthor.Customer: return "a customer";
                default: return "the store";
            }
        }

        public static string Phase(KarenDirector.Phase phase)
        {
            switch (phase)
            {
                case KarenDirector.Phase.Settle: return "The shift is starting — " + GameNames.Antagonist + " is keeping it calm for now.";
                case KarenDirector.Phase.Build: return "The pressure is building.";
                case KarenDirector.Phase.Spike: return GameNames.Antagonist + " is going all out.";
                case KarenDirector.Phase.Recover: return GameNames.Antagonist + " is giving you a breather.";
                case KarenDirector.Phase.Crunch: return "Closing time — " + GameNames.Antagonist + " is pushing hard.";
                default: return "The shift is over.";
            }
        }

        public static string PhaseShort(KarenDirector.Phase phase)
        {
            switch (phase)
            {
                case KarenDirector.Phase.Settle: return "calm";
                case KarenDirector.Phase.Build: return "building";
                case KarenDirector.Phase.Spike: return "all out";
                case KarenDirector.Phase.Recover: return "breather";
                case KarenDirector.Phase.Crunch: return "closing-time crunch";
                default: return "off shift";
            }
        }

        public static string Clock(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            return $"{Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}";
        }
    }
}
