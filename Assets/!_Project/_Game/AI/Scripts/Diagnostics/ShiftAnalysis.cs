using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Reads a shift recording and says what happened, in numbers and in sentences: how the
    // work went, where you spent your time, how often Karen found you and how close she got,
    // what she tried, and what made noise.
    public sealed class ShiftAnalysis
    {
        public float Length;
        public bool ClockedOut;
        public float Walked, Sprinted, Crouched, Lectured;
        public float SeenSeconds;
        public int Spotted, Chases, Catches, Warnings, Heard;
        public float ClosestDistance = float.MaxValue, ClosestAt = -1f;
        public string ClosestWhere = "";
        public float FirstSpotted = -1f;
        public readonly Dictionary<string, float> TimeIn = new Dictionary<string, float>();
        public readonly Dictionary<string, int> Jobs = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Tricks = new Dictionary<string, int>();
        public readonly Dictionary<string, int> SpottedIn = new Dictionary<string, int>();
        public readonly Dictionary<string, int> YourNoises = new Dictionary<string, int>();
        public int CustomersServed, DirectionsGiven, GaveUpAsking, GaveUpAtTill, AskedForHelp;
        public float LongestTillWait;
        public float LowestEnergy = 1f;
        public readonly List<string> Findings = new List<string>();

        public static ShiftAnalysis Of(ShiftRecording r)
        {
            var a = new ShiftAnalysis { Length = r.Length, ClockedOut = r.ClockedOut };
            a.ReadFrames(r);
            a.ReadEvents(r);
            a.Write();
            return a;
        }

        void ReadFrames(ShiftRecording r)
        {
            for (int i = 0; i < r.Frames.Count; i++)
            {
                ShiftFrame f = r.Frames[i];
                float dt = i + 1 < r.Frames.Count ? r.Frames[i + 1].T - f.T : 0f;
                if (i > 0)
                {
                    float step = Vector2.Distance(r.Frames[i - 1].Player, f.Player);
                    if (step < 5f) Walked += step;   // ignore teleports
                }
                if (f.PlayerMotion == (byte)MotionState.Sprinting) Sprinted += dt;
                if (f.PlayerMotion == (byte)MotionState.Crouching) Crouched += dt;
                if (f.PlayerHeld) Lectured += dt;
                if (f.KarenSees) SeenSeconds += dt;
                LowestEnergy = Mathf.Min(LowestEnergy, f.Energy);

                string area = StoreMap.AreaAt(new Vector3(f.Player.x, 0f, f.Player.y));
                TimeIn.TryGetValue(area, out float t);
                TimeIn[area] = t + dt;

                if (f.KarenPresent)
                {
                    float d = Vector2.Distance(f.Player, f.Karen);
                    if (d < ClosestDistance)
                    {
                        ClosestDistance = d;
                        ClosestAt = f.T;
                        ClosestWhere = KarenNarrator.Place(new Vector3(f.Player.x, 0f, f.Player.y));
                    }
                }
                foreach (PersonState c in f.Customers)
                    if (c.State == CustomerMark.Queueing) LongestTillWait = Mathf.Max(LongestTillWait, c.Wait);
            }
        }

        void ReadEvents(ShiftRecording r)
        {
            foreach (ShiftEvent e in r.Events)
            {
                switch (e.Kind)
                {
                    case "job":
                        Count(Jobs, e.Who);
                        if (e.Who == "served a customer") CustomersServed++;
                        if (e.Who == "gave directions") DirectionsGiven++;
                        break;
                    case "customer":
                        if (e.Who == "asked") AskedForHelp++;
                        else if (e.Who == "gave up asking") GaveUpAsking++;
                        else if (e.Who == "gave up at the till") GaveUpAtTill++;
                        break;
                    case "sound":
                        if (e.Who == "you") Count(YourNoises, e.Text);
                        break;
                    case nameof(StoryKind.Seen):
                        if (e.Text.Contains("spotted"))
                        {
                            Spotted++;
                            if (FirstSpotted < 0f) FirstSpotted = e.T;
                            if (e.HasPlace) Count(SpottedIn, StoreMap.AreaAt(new Vector3(e.At.x, 0f, e.At.y)));
                        }
                        break;
                    case nameof(StoryKind.Chase):
                        if (e.Text.Contains("caught")) Catches++;
                        else if (e.Text.Contains("chasing")) Chases++;
                        break;
                    case nameof(StoryKind.Warning): Warnings++; break;
                    case nameof(StoryKind.Heard): Heard++; break;
                    case nameof(StoryKind.Plan):
                        // "Karen is emptying a shelf you've already filled — Aisle 3." → the middle part.
                        const string plan = GameNames.Antagonist + " is ";
                        string trick = e.Text.StartsWith(plan) ? e.Text.Substring(plan.Length) : e.Text;
                        int dash = trick.IndexOf(" — ", System.StringComparison.Ordinal);
                        if (dash > 0) trick = trick.Substring(0, dash);
                        trick = trick.TrimEnd('.', '!');
                        if (trick != "walking her rounds") Count(Tricks, trick);
                        break;
                }
            }
        }

        static void Count(Dictionary<string, int> d, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            d.TryGetValue(key, out int n);
            d[key] = n + 1;
        }

        void Write()
        {
            string Clock(float s) => KarenNarrator.Clock(s);

            Findings.Add(ClockedOut
                ? $"You clocked out after {Clock(Length)}."
                : $"The shift ended after {Clock(Length)} without you clocking out.");

            int jobs = Jobs.Values.Sum();
            if (jobs > 0)
                Findings.Add($"You finished {jobs} job{(jobs == 1 ? "" : "s")}: " +
                             string.Join(", ", Jobs.OrderByDescending(p => p.Value).Select(p => $"{p.Key} ×{p.Value}")) + ".");

            if (AskedForHelp > 0)
                Findings.Add($"{AskedForHelp} customer{(AskedForHelp == 1 ? "" : "s")} asked where to find something; you walked {DirectionsGiven} of them there" +
                             (GaveUpAsking > 0 ? $" and {GaveUpAsking} gave up." : "."));
            if (GaveUpAtTill > 0)
                Findings.Add($"{GaveUpAtTill} customer{(GaveUpAtTill == 1 ? "" : "s")} gave up waiting at the till.");
            if (LongestTillWait > 30f)
                Findings.Add($"The longest anyone waited at the till was {Clock(LongestTillWait)}.");

            Findings.Add($"You walked {Walked:0} m" + (Sprinted > 5f ? $" and sprinted for {Clock(Sprinted)} — running is loud, and she listens." : "."));
            var top = TimeIn.Where(p => p.Value > 5f).OrderByDescending(p => p.Value).Take(3).ToList();
            if (top.Count > 0 && Length > 0f)
                Findings.Add("Where you spent the shift: " + string.Join(", ", top.Select(p => $"{p.Key} {100f * p.Value / Length:0}%")) + ".");

            if (Spotted == 0) Findings.Add(GameNames.Antagonist + " never spotted you.");
            else
            {
                string where = SpottedIn.Count > 0 ? ", most often " + InArea(SpottedIn.OrderByDescending(p => p.Value).First().Key) : "";
                Findings.Add($"{GameNames.Antagonist} spotted you {Spotted} time{(Spotted == 1 ? "" : "s")}{where}; the first time was at {Clock(FirstSpotted)}. You were in her sight for {Clock(SeenSeconds)} in total.");
            }
            if (ClosestAt >= 0f)
                Findings.Add($"The closest she got was {ClosestDistance:0.0} m, at {Clock(ClosestAt)}" + (ClosestWhere == "somewhere" ? "." : $", near {ClosestWhere}."));
            if (Chases > 0 || Catches > 0)
                Findings.Add($"She chased you {Chases} time{(Chases == 1 ? "" : "s")} and caught you {Catches} time{(Catches == 1 ? "" : "s")}" +
                             (Lectured > 0f ? $" — {Clock(Lectured)} spent being lectured." : "."));
            if (Tricks.Count > 0)
                Findings.Add($"She changed plans {Tricks.Values.Sum()} times; what she did most: " +
                             string.Join(", ", Tricks.OrderByDescending(p => p.Value).Take(4).Select(p => $"{p.Key} ×{p.Value}")) + ".");
            if (Warnings > 0)
                Findings.Add($"There were {Warnings} warning sound{(Warnings == 1 ? "" : "s")} before her tricks — listen for them.");
            if (Heard > 0)
                Findings.Add($"She heard or found evidence of you {Heard} time{(Heard == 1 ? "" : "s")}.");
            if (LowestEnergy < 0.15f)
                Findings.Add($"Your energy dropped to {LowestEnergy * 100f:0}% — coffee earlier would have helped.");
        }

        // "on the Sales floor", "on the Street", "in the Staff room".
        static string InArea(string area) =>
            (area.EndsWith("floor") || area == "Street" ? "on the " : "in the ") + area;

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"findings\":[");
            for (int i = 0; i < Findings.Count; i++) { if (i > 0) sb.Append(','); sb.Append('"').Append(MiniJson.EscapeInner(Findings[i])).Append('"'); }
            sb.Append("],\"numbers\":{");
            var numbers = new (string, float)[]
            {
                ("Shift length (s)", Length), ("Walked (m)", Walked), ("Sprinting (s)", Sprinted), ("Crouching (s)", Crouched),
                ("Times " + GameNames.Antagonist + " spotted you", Spotted), ("Seconds in her sight", SeenSeconds), ("Closest she got (m)", ClosestAt >= 0f ? ClosestDistance : -1f),
                ("Chases", Chases), ("Catches", Catches), ("Warning sounds", Warnings), ("Customers served", CustomersServed),
                ("Directions given", DirectionsGiven), ("Gave up asking", GaveUpAsking), ("Gave up at the till", GaveUpAtTill),
                ("Longest till wait (s)", LongestTillWait), ("Lowest energy (%)", LowestEnergy * 100f)
            };
            for (int i = 0; i < numbers.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(numbers[i].Item1).Append("\":").Append(N(numbers[i].Item2));
            }
            sb.Append('}');
            Table(sb, "areas", TimeIn.OrderByDescending(p => p.Value).Select(p => (p.Key, p.Value)));
            Table(sb, "jobs", Jobs.OrderByDescending(p => p.Value).Select(p => (p.Key, (float)p.Value)));
            Table(sb, "tricks", Tricks.OrderByDescending(p => p.Value).Select(p => (p.Key, (float)p.Value)));
            Table(sb, "noises", YourNoises.OrderByDescending(p => p.Value).Select(p => (p.Key, (float)p.Value)));
            sb.Append('}');
            return sb.ToString();
        }

        static void Table(StringBuilder sb, string name, IEnumerable<(string key, float value)> rows)
        {
            sb.Append(",\"").Append(name).Append("\":[");
            bool first = true;
            foreach (var (key, value) in rows)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("[\"").Append(MiniJson.EscapeInner(key)).Append("\",").Append(N(value)).Append(']');
            }
            sb.Append(']');
        }

        static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "0" : (Mathf.Round(v * 10f) / 10f).ToString(CultureInfo.InvariantCulture);
    }
}
