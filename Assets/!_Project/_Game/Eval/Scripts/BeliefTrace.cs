using System.IO;
using System.IO.Compression;
using System.Text;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Eval
{
    // The belief trace (eval only, -belief-trace <dir>): what Karen's filter did in a shift, as
    // numbers, so tools/belief can replay the filter in PyTorch with any motion model and train
    // through it. One gzipped file per shift:
    //
    //   the map           cells, edges, regions, and the per-map motion features
    //   each believe tick every likelihood exactly as the grid multiplied it (floor + the
    //                     listed cells), the sweep, the Director's hint (kept for the parity
    //                     check only), resets, and the predict step's dt and speed
    //   2 Hz and 2 s      the motion features as they refresh (MotionFeatures), plus a few
    //                     cells' finished features now and then, to check the Python copy
    //   5 Hz              the truth, from the harness side: the player's cell and velocity,
    //                     and the log p each grid gave it — the parity gate's target
    //
    // tools/belief/trace.py reads it; the record layouts below and there must match.
    public sealed class BeliefTrace : IBeliefTap
    {
        public const int Version = 2;
        const int ProbeCells = 16, ProbeEvery = 5;

        enum Record : byte { Map = 1, Shift = 2, Evidence = 3, Bias = 4, Predict = 5, Fast = 6, Slow = 7, Reset = 8, Sample = 9, Probe = 10, End = 11 }

        public static BeliefTrace Current { get; private set; }

        public string Directory { get; }
        public string Path { get; private set; }
        public int FilesWritten { get; private set; }

        readonly KarenBrain brain;
        readonly BeliefShadows shadows;
        BeliefGrid tapped;
        FileStream file;
        GZipStream zip;
        BinaryWriter w;
        int loggedKarenCell = int.MinValue;
        float[] loggedDesire, loggedHabit;
        float[] loggedLight;
        int slowCount;
        readonly float[] probeCell = new float[MotionFeatures.CellCount];
        readonly float[] probeEdge = new float[MotionFeatures.EdgeCount];

        BeliefTrace(KarenBrain brain, BeliefShadows shadows, string directory)
        {
            this.brain = brain;
            this.shadows = shadows;
            Directory = directory;
        }

        public static BeliefTrace Attach(KarenBrain brain, BeliefShadows shadows, string directory)
        {
            if (brain == null || string.IsNullOrEmpty(directory)) return null;
            System.IO.Directory.CreateDirectory(directory);
            var trace = new BeliefTrace(brain, shadows, directory);
            brain.ShiftBegan += trace.Open;
            brain.ShiftEnded += trace.Close;
            brain.BeliefRebuilt += trace.Retap;
            brain.MotionInputs.Refreshed += trace.OnRefreshed;
            Application.quitting += trace.Close;
            trace.Retap();
            if (brain.ShiftActive) trace.Open();
            Current = trace;
            return trace;
        }

        public void Detach()
        {
            Close();
            brain.ShiftBegan -= Open;
            brain.ShiftEnded -= Close;
            brain.BeliefRebuilt -= Retap;
            brain.MotionInputs.Refreshed -= OnRefreshed;
            Application.quitting -= Close;
            tapped?.Taps.Remove(this);
            if (Current == this) Current = null;
        }

        void Retap()
        {
            tapped?.Taps.Remove(this);
            tapped = brain.Belief;
            // After the shadows, so a record follows what every grid did with it.
            tapped?.Taps.Add(this);
        }

        bool Writing => w != null;

        // ---- a shift's file ---------------------------------------------------------------

        void Open()
        {
            Close();
            KehaiEnv env = KehaiEnv.Instance;
            EnvConfig config = env != null ? env.Config : null;
            string rung = config != null ? config.rung : brain.config.rung.ToString().Substring(0, 1);
            string agent = config != null ? config.agent : "player";
            int seed = config != null ? config.seed : brain.config.seed;
            int shift = brain.Stats.Shift;
            Path = System.IO.Path.Combine(Directory,
                $"trace_{rung}_{agent}_{seed}_s{shift:00}_{System.DateTime.Now:yyyyMMdd_HHmmss_fff}.kbt.gz");

            file = new FileStream(Path, FileMode.Create, FileAccess.Write);
            zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest);
            w = new BinaryWriter(new BufferedStream(zip, 1 << 16));
            w.Write(Encoding.ASCII.GetBytes("KBTR"));
            w.Write(Version);

            w.Write((byte)Record.Shift);
            w.Write(Time.time);
            w.Write(shift);
            w.Write(seed);
            Text(rung);
            Text(agent);
            Text(MotionModels.Canonical(brain.config.beliefMotion));
            w.Write(shadows != null ? shadows.Ids.Count : 0);
            if (shadows != null) foreach (string id in shadows.Ids) Text(id);
            w.Write(brain.Shift != null ? brain.Shift.shiftDurationSeconds : 0f);

            loggedKarenCell = int.MinValue;
            loggedDesire = loggedHabit = loggedLight = null;
            WriteMap(brain.MotionInputs);
            WriteSlow(brain.MotionInputs);
            WriteFast(brain.MotionInputs);
            WriteReset(brain.Belief, 3);
            FilesWritten++;
        }

        void Close()
        {
            if (!Writing) return;
            try
            {
                w.Write((byte)Record.End);
                w.Write(Time.time);
                w.Write(brain.Belief != null ? brain.Belief.Collapses : 0);
                w.Flush();
                w.Dispose();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Belief trace: couldn't finish " + Path + ": " + e.Message);
            }
            w = null;
            zip = null;
            file = null;
        }

        void Text(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }

        // ---- the map and the features ------------------------------------------------------

        void WriteMap(MotionFeatures f)
        {
            StoreMap map = f.Map;
            int n = map.CellCount, edges = map.EdgeTo.Length;
            w.Write((byte)Record.Map);
            w.Write(f.MapVersion);
            w.Write(n);
            w.Write(edges);
            w.Write(map.Regions.Count);
            for (int c = 0; c < n; c++) w.Write(map.CellPosition[c].x);
            for (int c = 0; c < n; c++) w.Write(map.CellPosition[c].z);
            for (int c = 0; c <= n; c++) w.Write(map.EdgeStart[c]);
            for (int e = 0; e < edges; e++) w.Write(map.EdgeTo[e]);
            for (int e = 0; e < edges; e++) w.Write(map.EdgeLength[e]);
            for (int c = 0; c < n; c++) w.Write(map.CellRegion[c]);
            for (int r = 0; r < map.Regions.Count; r++) w.Write((byte)(StoreMap.IsOutdoors(map.Regions[r].Area) ? 1 : 0));
            for (int c = 0; c < n; c++) w.Write(f.Open3[c]);
            for (int c = 0; c < n; c++) w.Write(f.Area[c]);
            for (int c = 0; c < n; c++) w.Write((byte)(f.Door[c] ? 1 : 0));
            for (int c = 0; c < n; c++) w.Write((byte)(f.Choke[c] ? 1 : 0));
            for (int c = 0; c < n; c++) w.Write(f.ClockDistance[c]);
            for (int c = 0; c < n; c++) w.Write(f.ExitDistance[c]);
            for (int e = 0; e < edges; e++) w.Write(f.EdgeSlot[e]);
        }

        void OnRefreshed(MotionFeatures f, MotionFeatures.Refresh kind)
        {
            if (!Writing) return;
            switch (kind)
            {
                case MotionFeatures.Refresh.Map:
                    loggedKarenCell = int.MinValue;
                    loggedDesire = loggedHabit = loggedLight = null;
                    WriteMap(f);
                    break;
                case MotionFeatures.Refresh.Slow:
                    WriteSlow(f);
                    if (++slowCount % ProbeEvery == 0) WriteProbe(f);
                    break;
                case MotionFeatures.Refresh.Fast:
                    WriteFast(f);
                    break;
            }
        }

        void WriteSlow(MotionFeatures f)
        {
            w.Write((byte)Record.Slow);
            w.Write(Time.time);
            w.Write(f.SlowVersion);
            w.Write(f.JobsLeft);
            w.Write((byte)(f.AllDone ? 1 : 0));
            w.Write(f.ShiftFraction);
            w.Write((byte)(f.Overtime ? 1 : 0));
            w.Write((byte)(f.Power ? 1 : 0));
            Field(f.Light, ref loggedLight, true);
            Field(f.Desire, ref loggedDesire, false);
            Field(f.Habit, ref loggedHabit, false);
        }

        // A per-cell field, only when it has changed since it was last written. Light goes as
        // bytes (it is kept in 1/255 steps), the rest as floats.
        void Field(float[] field, ref float[] logged, bool asBytes)
        {
            bool changed = logged == null || logged.Length != field.Length;
            if (!changed)
                for (int c = 0; c < field.Length; c++)
                    if (!field[c].Equals(logged[c])) { changed = true; break; }
            w.Write((byte)(changed ? 1 : 0));
            if (!changed) return;
            if (logged == null || logged.Length != field.Length) logged = new float[field.Length];
            System.Array.Copy(field, logged, field.Length);
            if (asBytes) for (int c = 0; c < field.Length; c++) w.Write((byte)Mathf.RoundToInt(field[c] * 255f));
            else for (int c = 0; c < field.Length; c++) w.Write(field[c]);
        }

        void WriteFast(MotionFeatures f)
        {
            w.Write((byte)Record.Fast);
            w.Write(Time.time);
            w.Write(f.FastVersion);
            w.Write(f.KarenCell);
            w.Write((byte)(f.Chasing ? 1 : 0));
            w.Write(f.SinceFix);
            w.Write(f.Speed);
            bool moved = f.KarenCell != loggedKarenCell;
            w.Write((byte)(moved ? 1 : 0));
            if (moved)
            {
                loggedKarenCell = f.KarenCell;
                for (int c = 0; c < f.KarenDistance.Length; c++) w.Write(MotionFeatures.Centimetres(f.KarenDistance[c] >= MotionFeatures.Far ? float.PositiveInfinity : f.KarenDistance[c]));
            }
            w.Write(f.TrailCells.Count);
            foreach (int c in f.TrailCells) w.Write((ushort)c);
            foreach (int c in f.TrailCells) w.Write(f.Trail[c]);
        }

        // A few cells' finished features, as C# computed them, for tools/belief to check its own.
        void WriteProbe(MotionFeatures f)
        {
            int n = f.Map.CellCount;
            w.Write((byte)Record.Probe);
            w.Write(ProbeCells);
            for (int k = 0; k < ProbeCells; k++)
            {
                int c = (int)(((long)k * n / ProbeCells + slowCount * 7L) % n);
                w.Write(c);
                f.CellFeatures(c, probeCell);
                foreach (float v in probeCell) w.Write(v);
                int start = f.Map.EdgeStart[c], end = f.Map.EdgeStart[c + 1];
                w.Write((byte)(end - start));
                for (int e = start; e < end; e++)
                {
                    f.EdgeFeatures(c, e, probeEdge);
                    foreach (float v in probeEdge) w.Write(v);
                }
            }

            // And what each learned shadow's kernel makes of those cells, for the same features.
            int learned = 0;
            if (shadows != null) foreach (BeliefGrid g in shadows.Grids) if (g.Motion is LearnedMotion) learned++;
            w.Write((byte)learned);
            if (shadows == null) return;
            for (int i = 0; i < shadows.Grids.Count; i++)
            {
                if (!(shadows.Grids[i].Motion is LearnedMotion lm)) continue;
                lm.EnsureBuilt(f.Map);
                Text(shadows.Ids[i]);
                int hc = lm.Headings;
                w.Write((byte)hc);
                for (int k = 0; k < ProbeCells; k++)
                {
                    int c = (int)(((long)k * n / ProbeCells + slowCount * 7L) % n);
                    for (int h = 0; h < hc; h++) w.Write(lm.Rates[c * hc + h]);
                    for (int e = f.Map.EdgeStart[c]; e < f.Map.EdgeStart[c + 1]; e++)
                        for (int h = 0; h < hc; h++) w.Write(lm.EdgeShares[e * hc + h]);
                }
            }
        }

        // ---- the filter --------------------------------------------------------------------

        public void OnEvidence(BeliefGrid grid, Likelihood l, bool collapsed)
        {
            if (!Writing) return;
            w.Write((byte)Record.Evidence);
            w.Write((byte)l.Channel);
            w.Write((byte)((l.Strong ? 1 : 0) | (collapsed ? 2 : 0)));
            w.Write(l.Floor);
            w.Write(l.Count);
            for (int i = 0; i < l.Count; i++) w.Write((ushort)l.Cells[i]);
            for (int i = 0; i < l.Count; i++) w.Write((float)l.Values[i]);
        }

        public void OnBias(BeliefGrid grid, int region, float amount)
        {
            if (!Writing) return;
            w.Write((byte)Record.Bias);
            w.Write(region);
            w.Write(amount);
        }

        public void OnPredict(BeliefGrid grid, float dt, float speed)
        {
            if (!Writing) return;
            w.Write((byte)Record.Predict);
            w.Write(Time.time);
            w.Write(dt);
            w.Write(speed);
        }

        public void OnReset(BeliefGrid grid)
        {
            if (Writing) WriteReset(grid, 0);
        }

        // The grid as it is after a reset: its belief over cells and its prior.
        void WriteReset(BeliefGrid grid, byte kind)
        {
            w.Write((byte)Record.Reset);
            w.Write(Time.time);
            w.Write(kind);
            int n = grid.CellCount;
            w.Write(n);
            for (int c = 0; c < n; c++) w.Write(grid[c]);
            for (int c = 0; c < n; c++) w.Write(grid.Prior[c]);
        }

        // ---- the truth (from BeliefAccuracy) ------------------------------------------------

        public void Sample(int truth, bool inView, float sinceFix, Vector2 velocity, float ownLogP, float[] shadowLogP, float lastSeenLogP)
        {
            if (!Writing) return;
            w.Write((byte)Record.Sample);
            w.Write(Time.time);
            w.Write(truth);
            w.Write((byte)(inView ? 1 : 0));
            w.Write(sinceFix);
            w.Write(velocity.x);
            w.Write(velocity.y);
            w.Write(ownLogP);
            w.Write(lastSeenLogP);
            int count = shadowLogP != null ? shadowLogP.Length : 0;
            w.Write((byte)count);
            for (int i = 0; i < count; i++) w.Write(shadowLogP[i]);
            w.Write(brain.Belief != null ? brain.Belief.Collapses : 0);
        }
    }
}
