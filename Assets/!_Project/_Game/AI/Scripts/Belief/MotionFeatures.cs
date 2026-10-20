using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // What a motion model may read about the store and the moment (Karen.md §4.2), refreshed
    // at three rates. Everything here is something Karen knows: the map, the jobs she set, her
    // own body and mood, the lights, her own belief's staleness and her guess at your energy.
    // Nothing reads the player (KarenTests' omniscience check scans this folder).
    //
    //   once per map   the shape of the floor: degree, open space, area, doors, chokepoints,
    //                  walking distance to the time clock and to the front doors
    //   every 2 s      unfinished work (the desire field), habit, jobs left, the shift clock,
    //                  overtime, light and power
    //   2 Hz           walking distance from her, where she has walked in the last 30 s,
    //                  whether she's chasing, time since her last strong fix, believed speed
    //
    // Models read features of a cell and of each of its edges, never cell ids: the maze
    // changes before shift 7 and the map is rebuilt with new ids. The same features are
    // computed from a belief trace by tools/belief/features.py; the two must agree.
    public sealed class MotionFeatures
    {
        public const int CellCount = 24;      // features per cell
        public const int EdgeCount = 9;       // features per edge
        public const int Slots = 8;           // directions a cell can be left by
        public const int MlpInputs = CellCount + 3 * Slots;
        public const float FastPeriod = 0.5f;
        public const float TrailSeconds = 30f;
        public const float Far = 999f;        // metres, for "can't get there"

        public enum Refresh { Map, Slow, Fast }
        public event System.Action<MotionFeatures, Refresh> Refreshed;

        public StoreMap Map { get; private set; }

        // Nothing is refreshed until something reads it: the shipped step (m0) doesn't, so the
        // shipped game pays nothing for this. Set by MotionModels.Create, the shadows and the trace.
        public bool Wanted;

        public int MapVersion { get; private set; }
        public int SlowVersion { get; private set; }
        public int FastVersion { get; private set; }

        // ---- once per map ----
        public float[] Open3;          // share of a 3 m disc that is floor
        public byte[] Area;            // 0 sales floor, 1 stock, 2 staff/backstreet, 3 lobby, 4 outside, 5 doorway
        public bool[] Door, Choke;
        public float[] ClockDistance, ExitDistance;
        public byte[] EdgeSlot;        // per edge: which of the eight directions (LinkCells' order)

        // ---- every 2 s ----
        public float[] Desire, Habit;
        public float[] Light;          // 0..1, in 1/255 steps
        public int JobsLeft;
        public bool AllDone, Overtime, Power = true;
        public float ShiftFraction;

        // ---- 2 Hz ----
        public int KarenCell = -1;
        public float[] KarenDistance;  // walking metres, in centimetre steps
        public float[] Trail;          // 0..1: 1 where she is now, fading over 30 s
        public bool Chasing;
        public float SinceFix, Speed = 3.1f;

        // The features themselves, cells × CellCount and edges × EdgeCount, rebuilt at each
        // refresh and shared by every model that reads them.
        public float[] X, Y;

        public readonly List<int> TrailCells = new List<int>();          // non-zero Trail, for the trace
        readonly List<(float t, int cell)> walked = new List<(float, int)>();
        float[] distanceScratch;
        int lightSignature = int.MinValue;

        // ---- refreshing ---------------------------------------------------------------------

        public void Bind(StoreMap map)
        {
            Map = map;
            int n = map.CellCount;
            Open3 = new float[n];
            Area = new byte[n];
            Door = new bool[n];
            Choke = new bool[n];
            Desire = new float[n];
            Habit = new float[n];
            Light = new float[n];
            KarenDistance = new float[n];
            Trail = new float[n];
            for (int c = 0; c < n; c++) { Desire[c] = float.PositiveInfinity; KarenDistance[c] = Far; Light[c] = 1f; }

            var within = new List<int>();
            for (int c = 0; c < n; c++)
            {
                map.CellsWithin(map.CellPosition[c], 3f, within);
                Open3[c] = Mathf.Min(1f, within.Count / 13f);
                Region region = map.Regions[map.CellRegion[c]];
                Area[c] = AreaCode(region);
                Door[c] = region.IsDoor;
                Choke[c] = region.IsChokepoint;
            }

            ClockDistance = FieldFrom(map, LandmarkKind.TimeClock);
            ExitDistance = FieldFrom(map, LandmarkKind.AutoDoor);

            EdgeSlot = EdgeSlots(map);

            walked.Clear();
            TrailCells.Clear();
            KarenCell = -1;
            lightSignature = int.MinValue;
            MapVersion++;
            Refreshed?.Invoke(this, Refresh.Map);
        }

        static byte AreaCode(Region region)
        {
            if (region.IsDoor) return 5;
            if (StoreMap.IsOutdoors(region.Area)) return 4;
            switch (region.Area)
            {
                case "Stockroom":
                case "Cold storage": return 1;
                case "Staff room":
                case "Backstreet": return 2;
                case "Lobby": return 3;
                default: return 0;
            }
        }

        static float[] FieldFrom(StoreMap map, LandmarkKind kind)
        {
            var sources = new List<int>();
            foreach (Landmark l in map.Landmarks)
                if (l.Kind == kind && l.Cell >= 0) sources.Add(l.Cell);
            var field = new float[map.CellCount];
            if (sources.Count == 0)
            {
                for (int c = 0; c < field.Length; c++) field[c] = Far;
                return field;
            }
            float[] d = map.DistancesFrom(sources);
            for (int c = 0; c < field.Length; c++) field[c] = float.IsInfinity(d[c]) ? Far : d[c];
            return field;
        }

        // Each edge's direction, as the map linked it. Not from the cells' positions: they sit
        // where the NavMesh put them, a little off the grid, and two edges of one cell could then
        // read as the same direction.
        static byte[] EdgeSlots(StoreMap map)
        {
            if (map.EdgeDirection != null && map.EdgeDirection.Length == map.EdgeTo.Length) return (byte[])map.EdgeDirection.Clone();
            var slots = new byte[map.EdgeTo.Length];
            for (int c = 0; c < map.CellCount; c++)
                for (int e = map.EdgeStart[c]; e < map.EdgeStart[c + 1]; e++)
                    slots[e] = SlotOf(map.CellPosition[map.EdgeTo[e]] - map.CellPosition[c]);
            return slots;
        }

        // The eight directions in StoreMap.LinkCells' order: +x, -x, +z, -z, then the diagonals.
        public static byte SlotOf(Vector3 d)
        {
            int dx = d.x > 0.1f ? 1 : d.x < -0.1f ? -1 : 0;
            int dz = d.z > 0.1f ? 1 : d.z < -0.1f ? -1 : 0;
            if (dz == 0) return (byte)(dx > 0 ? 0 : 1);
            if (dx == 0) return (byte)(dz > 0 ? 2 : 3);
            if (dx > 0) return (byte)(dz > 0 ? 4 : 5);
            return (byte)(dz > 0 ? 6 : 7);
        }

        public static bool Diagonal(int slot) => slot >= 4;

        // Every 2 s, after the brain has refreshed the desire field.
        public void RefreshSlow(BeliefGrid grid, int jobsLeft, bool allDone, float shiftFraction, bool overtime, bool power)
        {
            if (!Wanted || Map == null || grid.CellCount != Desire.Length) return;
            System.Array.Copy(grid.DesireDistance, Desire, Desire.Length);
            System.Array.Copy(grid.HabitPrior, Habit, Habit.Length);
            JobsLeft = jobsLeft;
            AllDone = allDone;
            ShiftFraction = shiftFraction;
            Overtime = overtime;
            Power = power;
            RefreshLight();
            SlowVersion++;
            Refreshed?.Invoke(this, Refresh.Slow);
        }

        // The lights change rarely (a blackout, a killed lamp); the field is rebuilt only then.
        void RefreshLight()
        {
            int signature = PowerSystem.PowerOn ? 1 : 0;
            IReadOnlyList<Light> lamps = LightProbe.CeilingLights;
            for (int i = 0; i < lamps.Count; i++)
                signature = signature * 31 + (lamps[i] != null && lamps[i].enabled && lamps[i].gameObject.activeInHierarchy ? 1 : 0);
            if (signature == lightSignature) return;
            lightSignature = signature;
            for (int c = 0; c < Light.Length; c++)
                Light[c] = Mathf.RoundToInt(LightProbe.LevelAt(Map.CellPosition[c]) * 255f) / 255f;
        }

        // 2 Hz: where she is, where she's been, and what she believes about you.
        public void RefreshFast(Vector3 karen, bool chasing, float sinceFix, float speed, float now)
        {
            if (!Wanted || Map == null) return;
            int cell = Map.CellAt(karen);
            if (cell != KarenCell)
            {
                KarenCell = cell;
                if (cell >= 0)
                {
                    distanceScratch = Map.Distances(cell, float.MaxValue, distanceScratch);
                    for (int c = 0; c < KarenDistance.Length; c++) KarenDistance[c] = Metres(Centimetres(distanceScratch[c]));
                }
                else
                {
                    for (int c = 0; c < KarenDistance.Length; c++) KarenDistance[c] = Far;
                }
            }

            if (cell >= 0) walked.Add((now, cell));
            while (walked.Count > 0 && now - walked[0].t > TrailSeconds) walked.RemoveAt(0);
            foreach (int c in TrailCells) Trail[c] = 0f;
            TrailCells.Clear();
            foreach ((float t, int at) in walked)
            {
                float v = 1f - (now - t) / TrailSeconds;
                Mark(at, v);
                for (int e = Map.EdgeStart[at]; e < Map.EdgeStart[at + 1]; e++) Mark(Map.EdgeTo[e], v);
            }

            Chasing = chasing;
            SinceFix = Mathf.Min(Far, Mathf.Max(0f, sinceFix));
            Speed = speed;
            FastVersion++;
            Refreshed?.Invoke(this, Refresh.Fast);
        }

        void Mark(int cell, float v)
        {
            if (Trail[cell] <= 0f) TrailCells.Add(cell);
            if (v > Trail[cell]) Trail[cell] = v;
        }

        // Walking distance in whole centimetres, as the trace stores it; 65535 = can't get there.
        public static ushort Centimetres(float metres)
        {
            if (float.IsInfinity(metres) || float.IsNaN(metres) || metres >= 655.34f) return 65535;
            return (ushort)Mathf.RoundToInt(Mathf.Max(0f, metres) * 100f);
        }

        public static float Metres(ushort centimetres) => centimetres == 65535 ? Far : centimetres / 100f;

        // ---- the features -------------------------------------------------------------------

        // X and Y for the fields as they are now, built once per refresh for every model.
        int tabledMap = -1, tabledSlow = -1, tabledFast = -1;

        public void Tabulate()
        {
            if (tabledMap == MapVersion && tabledSlow == SlowVersion && tabledFast == FastVersion && X != null) return;
            tabledMap = MapVersion;
            tabledSlow = SlowVersion;
            tabledFast = FastVersion;
            StoreMap map = Map;
            int n = map.CellCount, edges = map.EdgeTo.Length;
            if (X == null || X.Length != n * CellCount) X = new float[n * CellCount];
            if (Y == null || Y.Length != edges * EdgeCount) Y = new float[edges * EdgeCount];
            for (int c = 0; c < n; c++)
            {
                CellFeatures(c, X, c * CellCount);
                for (int e = map.EdgeStart[c]; e < map.EdgeStart[c + 1]; e++) EdgeFeatures(c, e, Y, e * EdgeCount);
            }
        }

        // Features of cell c, into x[at..at+CellCount).
        public void CellFeatures(int c, float[] x, int at = 0)
        {
            StoreMap map = Map;
            x[at + 0] = (map.EdgeStart[c + 1] - map.EdgeStart[c]) / 8f;
            x[at + 1] = Open3[c];
            for (int a = 0; a < 6; a++) x[at + 2 + a] = Area[c] == a ? 1f : 0f;
            x[at + 8] = Choke[c] ? 1f : 0f;
            x[at + 9] = Mathf.Exp(-ClockDistance[c] / 20f);
            x[at + 10] = Mathf.Exp(-ExitDistance[c] / 20f);
            x[at + 11] = float.IsInfinity(Desire[c]) ? 0f : Mathf.Exp(-Desire[c] / 8f);
            x[at + 12] = Habit[c];
            x[at + 13] = Mathf.Min(JobsLeft, 10) / 10f;
            x[at + 14] = AllDone ? 1f : 0f;
            x[at + 15] = Mathf.Clamp(ShiftFraction, 0f, 1.5f);
            x[at + 16] = Overtime ? 1f : 0f;
            x[at + 17] = Light[c];
            x[at + 18] = Power ? 1f : 0f;
            x[at + 19] = Mathf.Exp(-KarenDistance[c] / 10f);
            x[at + 20] = Trail[c];
            x[at + 21] = Chasing ? 1f : 0f;
            x[at + 22] = Mathf.Min(Mathf.Log(1f + SinceFix) / 5f, 1f);
            x[at + 23] = Speed / 3.1f;
        }

        // Features of edge e, from c to n, into y[at..at+EdgeCount).
        public void EdgeFeatures(int c, int e, float[] y, int at = 0)
        {
            int n = Map.EdgeTo[e];
            y[at + 0] = float.IsInfinity(Desire[c]) || float.IsInfinity(Desire[n]) ? 0f : Step(Desire[c] - Desire[n]);
            y[at + 1] = Habit[n];
            y[at + 2] = Step(KarenDistance[n] - KarenDistance[c]);
            y[at + 3] = Trail[n];
            y[at + 4] = Step(ClockDistance[c] - ClockDistance[n]);
            y[at + 5] = Step(ExitDistance[c] - ExitDistance[n]);
            y[at + 6] = Diagonal(EdgeSlot[e]) ? 1f : 0f;
            y[at + 7] = Door[n] ? 1f : 0f;
            y[at + 8] = Open3[n];
        }

        static float Step(float metres) => Mathf.Clamp(metres / StoreMap.CellSize, -1f, 1f);

        // The MLP's input for cell c: its features, then per direction the change in distance to
        // work and from her (0 where there's no edge), then which directions exist.
        public void MlpInput(int c, float[] u)
        {
            System.Array.Copy(X, c * CellCount, u, 0, CellCount);
            for (int k = 0; k < 3 * Slots; k++) u[CellCount + k] = 0f;
            for (int e = Map.EdgeStart[c]; e < Map.EdgeStart[c + 1]; e++)
            {
                int k = EdgeSlot[e];
                u[CellCount + k] = Y[e * EdgeCount + 0];
                u[CellCount + Slots + k] = Y[e * EdgeCount + 2];
                u[CellCount + 2 * Slots + k] = 1f;
            }
        }

        // ---- a state with no store behind it ------------------------------------------------

        // Fills every field from a fixed formula, for the parity check against tools/belief
        // (make_fixture.py writes the same numbers). Not a plausible store: just fixed numbers.
        public void FillSynthetic(StoreMap map)
        {
            Map = map;
            int n = map.CellCount;
            Open3 = new float[n]; Area = new byte[n]; Door = new bool[n]; Choke = new bool[n];
            ClockDistance = new float[n]; ExitDistance = new float[n];
            Desire = new float[n]; Habit = new float[n]; Light = new float[n];
            KarenDistance = new float[n]; Trail = new float[n];
            for (int c = 0; c < n; c++)
            {
                Open3[c] = Synthetic(c, 1) * 0.5f + 0.5f;
                Area[c] = (byte)(c % 6);
                Door[c] = c % 7 == 3;
                Choke[c] = c % 5 == 1;
                ClockDistance[c] = 20f + 15f * Synthetic(c, 2);
                ExitDistance[c] = 30f + 25f * Synthetic(c, 3);
                Desire[c] = c % 9 == 4 ? float.PositiveInfinity : 10f + 8f * Synthetic(c, 4);
                Habit[c] = 0.5f + 0.5f * Synthetic(c, 5);
                Light[c] = Mathf.RoundToInt((0.5f + 0.5f * Synthetic(c, 6)) * 255f) / 255f;
                KarenDistance[c] = Metres(Centimetres(12f + 10f * Synthetic(c, 7)));
                Trail[c] = c % 4 == 0 ? 0.5f + 0.5f * Synthetic(c, 8) : 0f;
            }
            EdgeSlot = EdgeSlots(map);
            JobsLeft = 4; AllDone = false; ShiftFraction = 0.6f; Overtime = false; Power = true;
            KarenCell = 0; Chasing = true; SinceFix = 12.5f; Speed = 3.1f;
            MapVersion++; SlowVersion++; FastVersion++;
            Wanted = true;
        }

        // sin of a fixed mix of the two numbers, in float: the same in C# and numpy.
        public static float Synthetic(int i, int j) => (float)System.Math.Sin(1.7 * i + 0.31 * j + 0.05 * i * j);
    }
}
