using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Where you think she is (0.4.0, build plan §8.8, D17, C4.8): a second, small belief map over
    // the same cells, turned round. Opponent modelling: she keeps a guess at what you know of her.
    //
    //   correct   whenever she thinks you sensed her: you were in her sight (so likely she in
    //             yours), or she made a noise near where she believes you are (her run, a door, a
    //             trick's own sound). A phantom chime counts too: it's a false cue she gave you,
    //             and your picture of her moves to where it sounded.
    //   predict   your picture of her spreads at her own speeds while you can't sense her.
    //
    //   K0  where she is, always (today: no mirror)        K1  where you last probably sensed her
    //   K2  the mirror map: that, spreading with time
    //
    // Only what she knows: every chance is weighted by her belief about where you are, never by
    // where you are. It sits under the source scan and the "moved player" test.
    public sealed class MirrorBelief
    {
        readonly StoreMap map;
        readonly float[] m, scratch;
        public MirrorMode Mode;
        public MirrorWeights W = MirrorWeights.Current;

        public Vector3 LastSensedAt { get; private set; }
        public float LastSensedTime { get; private set; } = -999f;
        public float SensedChance { get; private set; }          // the last correct's chance you noticed her
        public int PeakCell { get; private set; } = -1;

        public MirrorBelief(StoreMap map, MirrorMode mode)
        {
            this.map = map;
            Mode = mode;
            m = new float[map.CellCount];
            scratch = new float[map.CellCount];
        }

        public float this[int cell] => m[cell];

        // You clocked in at the clock; she starts the shift where you'd expect her (her start).
        public void Reset(Vector3 her, float now)
        {
            System.Array.Clear(m, 0, m.Length);
            int c = map.CellAt(her);
            if (c >= 0) m[c] = 1f;
            PeakCell = c;
            LastSensedAt = her;
            LastSensedTime = now;
        }

        // She was in your sight, as far as she can tell: she sees you (her awareness), at `metres`;
        // you're probably facing her way when you're coming at her or standing still.
        public void SawEachOther(Vector3 her, float metres, float now)
        {
            float p = W.SightNotice(metres);
            Notice(her, p, now);
        }

        // She made a noise at `at` that carries `carry` metres. Your chance of hearing it: her belief
        // mass within its carry (by walking distance), times how noticeable it is.
        public void MadeNoise(Vector3 at, float carry, BeliefGrid belief, float now, bool falseCue = false)
        {
            int source = map.CellAt(at);
            if (source < 0 || belief == null) return;
            float[] d = map.Distances(source, carry);
            float mass = 0f;
            for (int c = 0; c < d.Length; c++) if (!float.IsInfinity(d[c]) && d[c] <= carry) mass += belief[c];
            float p = Mathf.Clamp01(mass * W.SoundNotice);
            Notice(at, p, now, falseCue);
        }

        void Notice(Vector3 where, float p, float now, bool falseCue = false)
        {
            if (p <= 0.01f) return;
            SensedChance = p;
            int c = map.CellAt(where);
            if (c < 0) return;
            for (int i = 0; i < m.Length; i++) m[i] *= 1f - p;
            m[c] += p;
            // "Last sensed" moves when you most likely noticed.
            if (p >= 0.5f || falseCue)
            {
                LastSensedAt = map.CellPosition[c];
                LastSensedTime = now;
            }
            Refresh();
        }

        // Your picture of her spreads at her walking speed while you can't sense her (K2 only).
        public void Predict(float dt)
        {
            if (Mode != MirrorMode.K2) return;
            float mobility = Mathf.Clamp(W.SpreadSpeed * dt / StoreMap.CellSize, 0f, 0.8f);
            System.Array.Clear(scratch, 0, scratch.Length);
            for (int c = 0; c < m.Length; c++)
            {
                float mass = m[c];
                if (mass <= 0f) continue;
                int start = map.EdgeStart[c], end = map.EdgeStart[c + 1];
                if (start == end) { scratch[c] += mass; continue; }
                float leaving = mass * mobility;
                scratch[c] += mass - leaving;
                float share = leaving / (end - start);
                for (int e = start; e < end; e++) scratch[map.EdgeTo[e]] += share;
            }
            System.Array.Copy(scratch, m, m.Length);
            Refresh();
        }

        void Refresh()
        {
            float best = -1f, sum = 0f;
            for (int c = 0; c < m.Length; c++)
            {
                sum += m[c];
                if (m[c] > best) { best = m[c]; PeakCell = c; }
            }
            if (sum > 1e-9f && Mathf.Abs(sum - 1f) > 1e-4f)
                for (int c = 0; c < m.Length; c++) m[c] /= sum;
        }

        // Where you think she is: K1 the last place you probably sensed her; K2 the mirror's mean
        // (its peak when the mass is split far apart); K0 where she is.
        public Vector3 Thought(Vector3 herReal)
        {
            switch (Mode)
            {
                case MirrorMode.K1: return LastSensedAt;
                case MirrorMode.K2:
                    Vector3 sum = Vector3.zero;
                    float w = 0f;
                    for (int c = 0; c < m.Length; c++) { sum += map.CellPosition[c] * m[c]; w += m[c]; }
                    if (w <= 1e-6f) return LastSensedAt;
                    Vector3 mean = sum / w;
                    int at = map.CellAt(mean);
                    return at >= 0 ? mean : PeakCell >= 0 ? map.CellPosition[PeakCell] : LastSensedAt;
                default: return herReal;
            }
        }

        public float Stale(float now) => Mode == MirrorMode.K0 ? 0f : now - LastSensedTime;

        // The area she thinks you'd name for her (the mirror's likeliest region).
        public int ThoughtRegion(Vector3 herReal) => map.RegionAt(Thought(herReal));

        public float Mass(int cell) => m[cell];
    }

    // The mirror map's few numbers (§8.8): how likely you are to notice her, by sense and distance,
    // and how fast your picture of her spreads. Hand-set until C4.10 fits them (MirrorWeights.g.cs).
    public sealed class MirrorWeights
    {
        public string Version = "hand";
        public bool Trained;
        public float SightBias = 2.5f, SightPerMetre = 0.15f;    // notice ≈ σ(bias − perMetre × metres)
        public float SoundNotice = 0.8f;
        public float SpreadSpeed = 2.5f;                          // her walk, as you imagine it

        public float SightNotice(float metres) => 1f / (1f + Mathf.Exp(-(SightBias - SightPerMetre * metres)));

        public void Set(string name, float v)
        {
            switch (name)
            {
                case "sight_bias": SightBias = v; break;
                case "sight_per_metre": SightPerMetre = v; break;
                case "sound_notice": SoundNotice = v; break;
                case "spread_speed": SpreadSpeed = v; break;
            }
        }

        static MirrorWeights cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cached = null;

        public static void Forget() => cached = null;

        public static MirrorWeights Current
        {
            get
            {
                IReadOnlyDictionary<string, float> fromPack = ModelPack.Current?.Weights("MirrorWeights");
                if (fromPack != null)
                {
                    var p = new MirrorWeights { Version = "pack", Trained = true };
                    foreach (var kv in fromPack) p.Set(kv.Key, kv.Value);
                    return p;
                }
                if (cached != null) return cached;
                System.Type type = typeof(MirrorWeights).Assembly.GetType("Kehai.Karen.Generated.MirrorWeightsTable");
                cached = type?.GetMethod("Build", System.Type.EmptyTypes)?.Invoke(null, null) as MirrorWeights ?? new MirrorWeights();
                return cached;
            }
        }
    }
}
