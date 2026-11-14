using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // What the two Karens see, version 2 (0.4.0, build plan §5.3, D7, C5.2). Only what she may know:
    // it lives under Decision/, where the source scan forbids reading the player, and the "moved
    // player" test checks none of it moves when you do unseen.
    //
    //   her appraisal      how sure, how stale, panic, the target, tension, task load, …    (today's)
    //   the shift          the Director's phase, time left, doors open, chores open, shift number
    //   each trick         cooldown left, uses this shift                                    (today's)
    //   her belief         mass per area of the building (not the 32×32 picture: D7)
    //   where you're going her three best guesses: kind, metres, chance (MotionFeatures)
    //   what you know      how long since you probably sensed her, how far she is from where
    //                      you think she is (the mirror map; K0: 0 and 0)
    //   your Ledger        Basic only (zeros for Vital): per trick and per kind, her blended guess
    //                      for you and how much of it is you; your habits (sprint share, hiding,
    //                      routes); how fast you answer her chores
    //
    // Version 1 (PolicyObservation, rung F) had four inputs nothing ever wrote (lookBehindRate,
    // timeToFlee, coffeePerShift, warnings); version 2 drops them.
    public static class BrainObservation
    {
        public const int Version = 2;

        // Her part of the building, by the names StoreMap.AreaAt gives; the rest is "other".
        public static readonly string[] Areas = { "Sales floor", "Stockroom", "Staff room", "Office", "WC", "Lobby", "Cold room", "Backstreet", "Pavement" };
        public static readonly string[] Kinds = { "sight", "work", "sound", "social", "space", "you", "coverage", "nothing" };
        public static readonly string[] Answers = { "fog_reset", "clock_restarted", "understudy_sent_away", "camera_unplugged", "box_moved", "mop_returned" };
        const int Appraised = 13, ShiftPart = 4, Guesses = 3, GuessWidth = 12, MirrorPart = 2, Habits = 3;
        static readonly int Phases = System.Enum.GetValues(typeof(KarenDirector.Phase)).Length;
        static readonly int DestinationKinds = System.Enum.GetValues(typeof(DestinationKind)).Length;

        static int Tricks => TacticLibrary.All.Count;

        public static int Size =>
            Appraised + Phases + ShiftPart + 2 * Tricks + Areas.Length + 1 + Guesses * GuessWidth + MirrorPart
            + 2 * Tricks + 2 * Kinds.Length + Habits + Answers.Length;

        public static float[] Build(KarenContext c, bool readLedger)
        {
            var v = new float[Size];
            int i = 0;

            Appraisal a = c.A;
            v[i++] = Mathf.Clamp01(a.Confidence);
            v[i++] = Mathf.Clamp01(a.EntropyNorm);
            v[i++] = Mathf.Clamp01(a.Staleness / 60f);
            v[i++] = Mathf.Clamp01(a.Panic);
            v[i++] = Mathf.Clamp01(a.Setpoint);
            v[i++] = Mathf.Clamp(a.Pressure, -1f, 1f);
            v[i++] = Mathf.Clamp01(a.Tension);
            v[i++] = Mathf.Clamp01(a.TaskLoad);
            v[i++] = Mathf.Clamp01(a.EnergyBelief);
            v[i++] = Mathf.Clamp01(a.Awareness);
            v[i++] = Mathf.Clamp01(a.Containment);
            v[i++] = a.InRecovery ? 1f : 0f;
            v[i++] = a.Blinking ? 1f : 0f;

            // The shift.
            if (c.Director != null) v[i + (int)c.Director.CurrentPhase] = 1f;
            i += Phases;
            float length = c.Shift != null ? Mathf.Max(1f, c.Shift.shiftDurationSeconds) : 300f;
            v[i++] = c.Shift != null ? Mathf.Clamp01(c.Shift.TimeRemaining / length) : 1f;
            v[i++] = c.Shift == null || c.Shift.CustomersAllowed ? 1f : 0f;
            v[i++] = Squash(TacticLibrary.Live.Count(t => t.LeavesChore && Running(t, c)), 2f);
            v[i++] = Mathf.Clamp01(c.ShiftNumber / 10f);

            // Each trick: cooldown left, uses this shift.
            float now = c.Now;
            foreach (Tactic t in TacticLibrary.All)
            {
                v[i++] = t.Cooldown > 0f ? Mathf.Clamp01((t.Cooldown - (now - t.LastUsed)) / t.Cooldown) : 0f;
                v[i++] = Mathf.Clamp01(t.UsesThisShift / 5f);
            }

            // Her belief: mass per area, and outside.
            if (c.Belief != null && c.Map != null)
            {
                for (int r = 0; r < c.Map.Regions.Count; r++)
                {
                    int k = System.Array.IndexOf(Areas, c.Map.Regions[r].Area);
                    if (k >= 0) v[i + k] += c.Belief.RegionMass[r];
                }
                v[i + Areas.Length] = c.Belief.OutsideMass;
            }
            i += Areas.Length + 1;

            // Where you're going: her three best guesses (kind one-hot, metres, chance).
            MotionFeatures f = c.Brain != null ? c.Brain.Features : null;
            var top = f != null && f.Count > 0 && c.Belief != null ? f.Top(c.Belief, Guesses) : new List<(Destination, float, float)>();
            for (int g = 0; g < Guesses; g++)
            {
                if (g < top.Count)
                {
                    var (place, metres, chance) = top[g];
                    v[i + (int)place.Kind] = 1f;
                    v[i + DestinationKinds] = Squash(metres, 15f);
                    v[i + DestinationKinds + 1] = Mathf.Clamp01(chance);
                }
                i += GuessWidth;
            }

            // What you know of her.
            MirrorBelief m = c.Brain != null ? c.Brain.Mirror : null;
            if (m != null && c.Config != null && c.Config.mirror != MirrorMode.K0 && c.Body != null)
            {
                v[i] = Squash(m.Stale(now), 30f);
                v[i + 1] = Squash(Vector3.Distance(m.Thought(c.Body.Position), c.Body.Position), 15f);
            }
            i += MirrorPart;

            // Your Ledger (Basic only).
            if (readLedger && c.Ledger != null)
            {
                LedgerStart everyone = LedgerStart.Current;
                foreach (Tactic t in TacticLibrary.All)
                {
                    LedgerReading r = c.Ledger.Reading(t, everyone);
                    v[i++] = Mathf.Clamp(r.Guess * 4f, -1f, 1f);
                    v[i++] = r.YourShare;
                }
                foreach (string kind in Kinds)
                {
                    LedgerReading r = c.Ledger.KindReading(kind, everyone);
                    v[i++] = Mathf.Clamp(r.Guess * 4f, -1f, 1f);
                    v[i++] = r.YourShare;
                }
                v[i++] = Mathf.Clamp01(c.Ledger.SprintReading(everyone).Guess);
                v[i++] = c.Map != null && c.Ledger.ConcealmentRegions(c, 1).Count > 0 ? 1f : 0f;
                v[i++] = Squash(c.Ledger.Data.routes.Count, 5f);
                foreach (string kind in Answers) v[i++] = Squash(c.Ledger.AnswerReading(kind, everyone).Guess, 60f);
            }
            else i += 2 * Tricks + 2 * Kinds.Length + Habits + Answers.Length;

            if (i != v.Length) throw new System.InvalidOperationException($"brain observation wrote {i} of {v.Length}");
            for (int k = 0; k < v.Length; k++) if (float.IsNaN(v[k]) || float.IsInfinity(v[k])) v[k] = 0f;
            return v;
        }

        static bool Running(Tactic t, KarenContext c)
        {
            try { return t.EffectRunning(c); }
            catch (System.NullReferenceException) { return false; }
        }

        static float Squash(float x, float scale) => x <= 0f ? 0f : x / (x + scale);
    }
}
