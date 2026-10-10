using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // Watches one shift for the moments in ClipMarkers.All and writes a marker for each. Plain
    // C# fed by ClipMarkerRecorder — the live picture ten times a second, and whatever Karen,
    // the narrator and the store report — so the rules can be tested without a running shift.
    // Every time is in seconds into the shift; every position is on the floor plan.
    public sealed class ClipWatch
    {
        readonly List<ClipMarker> markers;
        readonly List<Vector2> chaseSpans;

        public ClipWatch(List<ClipMarker> markers, List<Vector2> chaseSpans)
        {
            this.markers = markers;
            this.chaseSpans = chaseSpans;
        }

        public IReadOnlyList<ClipMarker> Markers => markers;

        int Add(string id, float t, Vector2 at, bool hasPlace, string text, float end = float.NaN, float value = float.NaN)
        {
            markers.Add(new ClipMarker
            {
                Id = id, T = t, End = float.IsNaN(end) ? t : Mathf.Max(t, end),
                At = at, HasPlace = hasPlace, Text = text ?? "", Value = value
            });
            return markers.Count - 1;
        }

        void Extend(int index, float end)
        {
            ClipMarker m = markers[index];
            m.End = Mathf.Max(m.End, end);
            markers[index] = m;
        }

        static string Her => GameNames.Antagonist;

        // ---- the live picture --------------------------------------------------------------

        float chaseFrom = -1f, lastCatch = -999f;
        bool escapePending;
        float escapeFrom, escapeTo;
        float nearMissUntil, foundBlindUntil, stuckUntil;
        Vector2 karen;
        bool karenKnown;
        readonly List<(float t, Vector2 at, bool hunt)> path = new List<(float, Vector2, bool)>();
        readonly Dictionary<int, CustomerMark> customers = new Dictionary<int, CustomerMark>();

        public void Frame(ShiftFrame f)
        {
            float t = f.T;

            // Chases, and the ones she didn't finish. Decided a second late, so a catch on the
            // same frame always counts as a catch.
            if (f.Chasing && chaseFrom < 0f) chaseFrom = t;
            else if (!f.Chasing && chaseFrom >= 0f)
            {
                chaseSpans.Add(new Vector2(chaseFrom, t));
                escapePending = true;
                escapeFrom = chaseFrom;
                escapeTo = t;
                chaseFrom = -1f;
            }
            if (escapePending && t >= escapeTo + 1f) ResolveEscape(f.Player);

            if (!f.KarenPresent) return;
            karen = f.Karen;
            karenKnown = true;
            float apart = Vector2.Distance(f.Player, f.Karen);

            if (apart <= ClipMarkers.NearMissMetres && !f.KarenSees && !f.PlayerHeld && t >= nearMissUntil)
            {
                Add("near_miss", t, f.Player, true, $"{Her} came within {apart:0.0} m of you without seeing you.");
                nearMissUntil = t + ClipMarkers.NearMissCooldown;
            }

            if (f.GuessConfidence >= ClipMarkers.FoundBlindConfidence && !f.KarenSees && t >= foundBlindUntil &&
                Vector2.Distance(f.Guess, f.Player) <= ClipMarkers.FoundBlindMetres)
            {
                Add("found_blind", t, f.Player, true, $"{Her} knew where you were without seeing you ({Mathf.RoundToInt(f.GuessConfidence * 100f)}% sure).");
                foundBlindUntil = t + ClipMarkers.FoundBlindCooldown;
            }

            // Hunting but not getting anywhere: usually stuck on something.
            bool hunt = f.KarenMood == (byte)KarenBody.Mood.Hunt;
            path.Add((t, f.Karen, hunt));
            while (path.Count > 0 && path[0].t < t - ClipMarkers.StuckSeconds - 0.5f) path.RemoveAt(0);
            if (hunt && t >= stuckUntil && path.Count > 1 && path[0].t <= t - ClipMarkers.StuckSeconds + 0.05f)
            {
                bool allHunting = true;
                float furthest = 0f;
                foreach (var p in path)
                {
                    if (p.t < t - ClipMarkers.StuckSeconds - 0.05f) continue;
                    allHunting &= p.hunt;
                    furthest = Mathf.Max(furthest, Vector2.Distance(p.at, f.Karen));
                }
                if (allHunting && furthest < ClipMarkers.StuckMetres)
                {
                    Add("karen_stuck", t - ClipMarkers.StuckSeconds, f.Karen, true, $"{Her} was hunting but hardly moved for {ClipMarkers.StuckSeconds:0} seconds.", t);
                    stuckUntil = t + ClipMarkers.StuckCooldown;
                }
            }

            foreach (PersonState c in f.Customers)
            {
                if (c.Id < 0) continue;   // her understudies aren't customers
                bool known = customers.TryGetValue(c.Id, out CustomerMark before);
                customers[c.Id] = c.State;
                if (c.State == CustomerMark.Possessed && (!known || before != CustomerMark.Possessed))
                    Add("possessed", t, c.At, true, $"A customer stopped and turned to face you: {Her} took them over.");
                else if (known && c.State == CustomerMark.LostTheGuide && before != CustomerMark.LostTheGuide)
                    Add("customer_chaos", t, c.At, true, "A customer you were guiding lost you.");
            }

            for (int i = loudChecks.Count - 1; i >= 0; i--)
            {
                LoudCheck check = loudChecks[i];
                if (Vector2.Distance(f.Karen, check.At) <= check.Distance - ClipMarkers.HeadingThereMetres)
                {
                    if (check.NoiseT - lastLoudMistake >= ClipMarkers.LoudMistakeCooldown)
                    {
                        Add("loud_mistake", check.NoiseT, check.At, true, check.Text, check.HeardT);
                        lastLoudMistake = check.NoiseT;
                    }
                    loudChecks.RemoveAt(i);
                }
                else if (t > check.HeardT + ClipMarkers.HeadingThereSeconds) loudChecks.RemoveAt(i);
            }
        }

        void ResolveEscape(Vector2 playerAt)
        {
            escapePending = false;
            if (lastCatch >= escapeTo - 1f) return;
            Add("escape", Mathf.Max(escapeFrom, escapeTo - ClipMarkers.EscapeLead), playerAt, true,
                $"You got away: {Her} gave up a {escapeTo - escapeFrom:0}-second chase.", escapeTo, escapeTo - escapeFrom);
        }

        // ---- the lights ------------------------------------------------------------------

        float darkFrom = -1f;

        public void Lights(float t, bool on)
        {
            if (!on && darkFrom < 0f) darkFrom = t;
            else if (on && darkFrom >= 0f)
            {
                Add("blackout", darkFrom, Vector2.zero, false, $"The lights were out for {t - darkFrom:0} seconds.", t, t - darkFrom);
                darkFrom = -1f;
            }
        }

        // ---- what the narrator says ------------------------------------------------------

        float lastBlink = -999f, lastLearned = -999f, lastPa = -999f, lastLoudMistake = -999f;
        readonly List<(float t, string text)> warnings = new List<(float, string)>();
        readonly List<(float t, Vector2 at, string what)> loudNoises = new List<(float, Vector2, string)>();

        struct LoudCheck
        {
            public float NoiseT, HeardT, Distance;
            public Vector2 At;
            public string Text;
        }

        readonly List<LoudCheck> loudChecks = new List<LoudCheck>();

        public void Story(float t, StoryKind kind, string text, Vector2 at, bool hasPlace)
        {
            switch (kind)
            {
                case StoryKind.Blink:
                    if (t - lastBlink < ClipMarkers.BlinkCooldown) return;
                    lastBlink = t;
                    Add("blink_move", t, at, hasPlace, text);
                    break;
                case StoryKind.Learned:
                    if (t - lastLearned < ClipMarkers.LearnedCooldown) return;
                    lastLearned = t;
                    Add("learned", t, at, hasPlace, text);
                    break;
                case StoryKind.Warning:
                    warnings.RemoveAll(w => t - w.t > ClipMarkers.TellToTrickSeconds);
                    warnings.Add((t, text));
                    break;
                case StoryKind.Heard:
                    loudNoises.RemoveAll(n => t - n.t > ClipMarkers.LoudMistakeSeconds);
                    if (loudNoises.Count == 0 || !karenKnown) return;
                    var noise = loudNoises[loudNoises.Count - 1];
                    loudNoises.Clear();
                    loudChecks.Add(new LoudCheck
                    {
                        NoiseT = noise.t, HeardT = t, At = noise.at, Distance = Vector2.Distance(karen, noise.at),
                        Text = $"You made a noise ({noise.what}) and {Her} came to look."
                    });
                    break;
            }
        }

        public void Noise(float t, NoiseKind kind, NoiseAuthor author, Vector2 at)
        {
            if (author != NoiseAuthor.Player || (kind != NoiseKind.Sprint && kind != NoiseKind.DroppedItem)) return;
            loudNoises.RemoveAll(n => t - n.t > ClipMarkers.LoudMistakeSeconds);
            loudNoises.Add((t, at, kind == NoiseKind.Sprint ? "running" : "something dropped"));
        }

        // ---- her decisions (the thought log) ----------------------------------------------

        int openProp = -1;
        float openPropUntil;
        float lastClockRefused = -999f;

        public void Record(float t, string kind, string chose, string text, Vector2 karenAt)
        {
            switch (kind)
            {
                case "CAUGHT":
                    lastCatch = t;
                    Add("catch", t, karenAt, true, $"{Her} caught you: a lecture and overtime.");
                    break;

                case "PLAN":
                    openProp = -1;
                    if (chose != null && ClipMarkers.PropTricks.Contains(chose))
                    {
                        openProp = Add("prop_trick", t, karenAt, true, $"{Her} is {KarenNarrator.Tactic(chose).TrimEnd('!')}.");
                        openPropUntil = t + ClipMarkers.PropTrickSeconds;
                    }
                    break;

                case "EFFECT":
                    string label = EffectLabel(text);
                    if (label.StartsWith("PA:")) return;   // told by its own chime, not by a warning
                    if (openProp >= 0 && t <= openPropUntil) Extend(openProp, t);

                    warnings.RemoveAll(w => t - w.t > ClipMarkers.TellToTrickSeconds);
                    if (warnings.Count > 0)
                    {
                        var w = warnings[0];
                        warnings.RemoveAt(0);
                        Add("tell_then_trick", w.t, karenAt, true, w.text + " Then: " + label + ".", t, t - w.t);
                    }

                    if (label.StartsWith("kick over a bucket"))
                    {
                        mopped.RemoveAll(m => t - m.t > ClipMarkers.UndoneWorkSeconds);
                        foreach (var m in mopped)
                            if (Vector2.Distance(m.at, karenAt) <= ClipMarkers.UndoneSpillMetres)
                            {
                                Add("undone_work", t, karenAt, true, $"{Her} spilled something where you had just mopped.", t, t - m.t);
                                mopped.Remove(m);
                                break;
                            }
                    }
                    else if (label.StartsWith("clock-out refused")) ClockRefused(t, karenAt, false);
                    break;
            }
        }

        // "[01:23] EFFECT   kick over a bucket at Aisle 3" → "kick over a bucket at Aisle 3".
        public static string EffectLabel(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int at = text.IndexOf("EFFECT", System.StringComparison.Ordinal);
            return (at >= 0 ? text.Substring(at + 6) : text).Trim();
        }

        // ---- the player's work, and her undoing it ------------------------------------------

        readonly Dictionary<int, float> restocked = new Dictionary<int, float>();
        readonly List<(float t, Vector2 at)> mopped = new List<(float, Vector2)>();

        public void Restocked(float t, int shelf) => restocked[shelf] = t;

        public void Swept(float t, int shelf, Vector2 at)
        {
            if (!restocked.TryGetValue(shelf, out float when) || t - when > ClipMarkers.UndoneWorkSeconds) return;
            restocked.Remove(shelf);
            Add("undone_work", t, at, true, $"{Her} emptied a shelf you had filled {t - when:0} seconds earlier.", t, t - when);
        }

        public void Mopped(float t, Vector2 at) => mopped.Add((t, at));

        // ---- the store ---------------------------------------------------------------------

        public void Pa(float t, string text)
        {
            if (t - lastPa < ClipMarkers.PaCooldown) return;
            lastPa = t;
            Add("pa_call", t, Vector2.zero, false, $"{Her} over the speakers: \"{text}\"");
        }

        public void PunchRefused(float t, Vector2 playerAt) => ClockRefused(t, playerAt, true);

        void ClockRefused(float t, Vector2 at, bool byTheClock)
        {
            if (t - lastClockRefused < ClipMarkers.ClockRefusedCooldown) return;
            lastClockRefused = t;
            Add("clock_refused", t, at, true, byTheClock ? "The time clock refused you." : $"{Her} refused your clock-out: two more minutes.");
        }

        public void CustomerGaveUp(float t, Vector2 at) => Add("customer_chaos", t, at, true, "A customer gave up waiting at the till and left.");

        public void Manual(float t, bool bug, Vector2 playerAt) =>
            Add(bug ? "manual_bug" : "manual_good", t, playerAt, true, bug ? "Marked as a bug (Left Shift + F7)." : "Marked for a clip (F7).");

        // At the end of the shift: close what's still open, and mark the review.
        public void Finish(float length, bool clockedOut, Vector2 playerAt)
        {
            if (chaseFrom >= 0f)
            {
                chaseSpans.Add(new Vector2(chaseFrom, length));
                chaseFrom = -1f;
            }
            if (escapePending) ResolveEscape(playerAt);
            if (darkFrom >= 0f)
            {
                Add("blackout", darkFrom, Vector2.zero, false, $"The lights went out and stayed out until the end of the shift ({length - darkFrom:0} s).", length, length - darkFrom);
                darkFrom = -1f;
            }
            if (clockedOut) Add("shift_review", length, playerAt, true, "You clocked out: the Performance Review.");
        }
    }
}
