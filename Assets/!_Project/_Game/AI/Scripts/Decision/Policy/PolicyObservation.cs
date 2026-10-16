using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // What rung F's policy sees when it picks a tactic: Karen's own state, never the player's.
    // It lives under Decision/, so the source scan in KarenTests holds it to that.
    //
    // A fixed-length vector (this) and a small picture of her belief (BeliefRaster). Both are
    // plain C#, so the tests can check them without the ML-Agents package.
    public static class PolicyObservation
    {
        const int Appraised = 13, ShiftClock = 2, PlayerModel = 11, Spatial = 1;
        static readonly int GoalCount = System.Enum.GetValues(typeof(GoalId)).Length;
        static readonly int PhaseCount = System.Enum.GetValues(typeof(KarenDirector.Phase)).Length;

        public static int Size => Appraised + GoalCount + PhaseCount + ShiftClock + 2 * TacticLibrary.All.Count + PlayerModel + Spatial;

        public static float[] Build(GoalId goal, KarenContext c)
        {
            var v = new float[Size];
            Write(goal, c, v);
            return v;
        }

        public static void Write(GoalId goal, KarenContext c, float[] v)
        {
            System.Array.Clear(v, 0, v.Length);
            int i = 0;

            // The appraisal (Karen.md §6.2): what every other decision reads too.
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

            // The goal this tactic is for, and the Director's phase.
            v[i + (int)goal] = 1f;
            i += GoalCount;
            if (c.Director != null) v[i + (int)c.Director.CurrentPhase] = 1f;
            i += PhaseCount;

            // How far through the shift, and the career.
            float length = c.Shift != null ? c.Shift.shiftDurationSeconds : 300f;
            v[i++] = Mathf.Clamp01(c.ShiftTime / Mathf.Max(1f, length));
            v[i++] = Mathf.Clamp01(c.ShiftNumber / 10f);

            // Every tactic: how much of its cooldown is left, and how often it's been used.
            float now = c.Now;
            foreach (Tactic t in TacticLibrary.All)
            {
                v[i++] = t.Cooldown > 0f ? Mathf.Clamp01((t.Cooldown - (now - t.LastUsed)) / t.Cooldown) : 0f;
                v[i++] = Mathf.Clamp01(t.UsesThisShift / 5f);
            }

            // The Ledger's player model (§7.1): who she thinks this employee is.
            LedgerData d = c.Ledger?.Data;
            if (d != null)
            {
                v[i++] = Mathf.Clamp01(d.sprintFraction);
                v[i++] = Mathf.Clamp01(d.crouchFraction);
                v[i++] = Squash(d.meanSpeed, 4f);
                v[i++] = Squash(d.lookBehindRate, 1f);
                v[i++] = Squash(d.timeToFlee, 3f);
                v[i++] = Squash(d.coffeePerShift, 2f);
                v[i++] = Mathf.Clamp01(d.energyAtClockOut);
                v[i++] = Squash(d.meanShiftSeconds, 300f);
                v[i++] = Squash(d.shiftsWorked, 10f);
                v[i++] = Squash(d.caughtCount, 3f);
                v[i++] = Squash(d.warnings, 3f);
            }
            else i += PlayerModel;

            // How far she is from where she thinks the employee is.
            v[i++] = c.Body != null ? Squash(Vector3.Distance(c.Body.Position, a.PeakPosition), 20f) : 0f;

            if (i != v.Length) throw new System.InvalidOperationException($"policy observation wrote {i} of {v.Length}");
            for (int k = 0; k < v.Length; k++)
                if (float.IsNaN(v[k]) || float.IsInfinity(v[k])) v[k] = 0f;
        }

        // 0 at 0, ½ at `scale`, towards 1 beyond: for counts and lengths with no upper bound.
        static float Squash(float x, float scale) => x <= 0f ? 0f : x / (x + scale);
    }

    // Her belief, drawn as a 32×32 picture over the store's floor plan, so the policy's input
    // keeps its size whatever the map (regions come and go with maze mutation).
    // Channel 0: belief mass, scaled so the likeliest pixel is 1. Channel 1: where she is.
    // Laid out [channel, row, column], rows along z and columns along x — the order
    // ML-Agents' ObservationWriter[channel, height, width] takes.
    public sealed class BeliefRaster
    {
        public const int Size = 32;
        public const int Channels = 2;
        public static int Length => Channels * Size * Size;

        StoreMap map;
        Vector3[] positions;
        int[] pixelOf;

        public void Write(StoreMap map, BeliefGrid belief, Vector3 karen, float[] into)
        {
            System.Array.Clear(into, 0, Length);
            if (map == null || belief == null || map.CellCount == 0) return;
            if (map != this.map || map.CellPosition != positions) Index(map);

            float max = 0f;
            for (int cell = 0; cell < map.CellCount; cell++)
            {
                int p = pixelOf[cell];
                into[p] += belief[cell];
                if (into[p] > max) max = into[p];
            }
            if (max > 0f)
                for (int p = 0; p < Size * Size; p++) into[p] /= max;

            into[Size * Size + Pixel(map.Bounds, karen)] = 1f;
        }

        void Index(StoreMap m)
        {
            map = m;
            positions = m.CellPosition;
            pixelOf = new int[m.CellCount];
            for (int cell = 0; cell < m.CellCount; cell++) pixelOf[cell] = Pixel(m.Bounds, m.CellPosition[cell]);
        }

        public static int Pixel(Bounds bounds, Vector3 p)
        {
            float u = (p.x - bounds.min.x) / Mathf.Max(1e-3f, bounds.size.x);
            float w = (p.z - bounds.min.z) / Mathf.Max(1e-3f, bounds.size.z);
            int column = Mathf.Clamp(Mathf.FloorToInt(u * Size), 0, Size - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(w * Size), 0, Size - 1);
            return row * Size + column;
        }
    }
}
