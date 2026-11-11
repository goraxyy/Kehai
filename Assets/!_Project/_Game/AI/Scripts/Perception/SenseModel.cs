using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // How far she trusts each sense (0.4.0, build plan §8.7, D16, C4.7): the hand-set numbers of
    // Sensorium.cs and CustomerMemory.cs in one table. A sensor model: how far to trust a reading,
    // and how wide to draw the circle round it.
    //
    //   S0  today's hand-set numbers, in today's formulas (the same arithmetic, so the same belief)
    //   S1  the same formulas, their numbers fitted to recorded shifts through the filter
    //   S2  S1, plus two inputs she already has: walls between (a noise's walk against its straight
    //       line) and lights off. With their gains at 0 it is S1.
    //
    // It adds no sense. The fit (tools/belief) uses your true position, offline; the game never does.
    public sealed class SenseModel
    {
        public string Id = "s0";
        public string Version = "hand";
        public bool Trained;

        // Sight: σ from far to near as she grows surer (Lerp by awareness).
        public float SightSigmaFar = 3f, SightSigmaNear = 0.8f;
        // Hearing: confidence = √(1 − d/carry) × HearConfidence; σ = HearSigma0 + d × HearSigmaPerMetre.
        public float HearConfidence = 0.9f, HearSigma0 = 1f, HearSigmaPerMetre = 0.12f;
        // S2 only: σ × (1 + WallGain × (walk/straight − 1)) and × (1 + DarkGain) with the lights out.
        public float WallGain, DarkGain;
        // Testimony: σ = TestimonySigma0 + age × TestimonySigmaPerSecond.
        public float TestimonySigma0 = 2.5f, TestimonySigmaPerSecond = 0.25f;
        // Absence: a swept cell's detection chance is her eye's own, × SweepScale.
        public float SweepScale = 0.85f;
        // Traces, by TraceKind: trust (× freshness where it fades), σ0, σ per second of age.
        public readonly Dictionary<TraceKind, (float trust, float sigma0, float sigmaPerSecond)> Traces = new Dictionary<TraceKind, (float, float, float)>
        {
            [TraceKind.WetFootprint] = (0.55f, 3f, 0f),
            [TraceKind.UndoneSabotage] = (0.6f, 6f, 0.1f),
            [TraceKind.DroppedItem] = (0.45f, 5f, 0.1f),
            [TraceKind.DoorOpened] = (0.5f, 4f, 0f),
            [TraceKind.MopAway] = (0.4f, 6f, 0f),
            [TraceKind.BaggedBin] = (0.35f, 8f, 0f),
        };
        public (float trust, float sigma0, float sigmaPerSecond) OtherTrace = (0.3f, 7f, 0.1f);

        // ---- the formulas (Sensorium and CustomerMemory call these) ----------------------------------

        public float SightSigma(float awareness) => Mathf.Lerp(SightSigmaFar, SightSigmaNear, awareness);

        public float HearingConfidence(float d, float carry) => Mathf.Sqrt(1f - d / carry) * HearConfidence;

        public float HearingSigma(float d, float straight, bool lightsOut)
        {
            float sigma = HearSigma0 + d * HearSigmaPerMetre;
            if (Id != "s2") return sigma;
            float detour = straight > 0.5f ? Mathf.Max(0f, d / straight - 1f) : 0f;
            return sigma * (1f + WallGain * detour) * (lightsOut ? 1f + DarkGain : 1f);
        }

        public float TestimonySigma(float age) => TestimonySigma0 + age * TestimonySigmaPerSecond;

        // A trace's trust and σ; `fades` for the kinds whose trust is × freshness.
        public void Trace(TraceKind kind, float freshness, float age, out float confidence, out float sigma)
        {
            switch (kind)
            {
                case TraceKind.WetFootprint: { var t = Traces[kind]; confidence = t.trust * freshness; sigma = t.sigma0; break; }
                case TraceKind.UndoneSabotage: { var t = Traces[kind]; confidence = t.trust; sigma = t.sigma0 + age * t.sigmaPerSecond; break; }
                case TraceKind.DroppedItem: { var t = Traces[kind]; confidence = t.trust; sigma = t.sigma0 + age * t.sigmaPerSecond; break; }
                case TraceKind.DoorOpened: { var t = Traces[kind]; confidence = t.trust * freshness; sigma = t.sigma0; break; }
                case TraceKind.MopAway: { var t = Traces[kind]; confidence = t.trust; sigma = t.sigma0; break; }
                case TraceKind.BaggedBin: { var t = Traces[kind]; confidence = t.trust * freshness; sigma = t.sigma0; break; }
                default: confidence = OtherTrace.trust; sigma = OtherTrace.sigma0 + age * OtherTrace.sigmaPerSecond; break;
            }
        }

        // ---- which numbers -----------------------------------------------------------------------

        public static SenseModel Hand() => new SenseModel();

        public void Set(string name, float v)
        {
            switch (name)
            {
                case "sight_sigma_far": SightSigmaFar = v; break;
                case "sight_sigma_near": SightSigmaNear = v; break;
                case "hear_confidence": HearConfidence = v; break;
                case "hear_sigma0": HearSigma0 = v; break;
                case "hear_sigma_per_metre": HearSigmaPerMetre = v; break;
                case "wall_gain": WallGain = v; break;
                case "dark_gain": DarkGain = v; break;
                case "testimony_sigma0": TestimonySigma0 = v; break;
                case "testimony_sigma_per_second": TestimonySigmaPerSecond = v; break;
                case "sweep_scale": SweepScale = v; break;
                default:
                    // "trace_WetFootprint_trust", "_sigma0", "_sigma_per_second"
                    if (!name.StartsWith("trace_")) break;
                    string rest = name.Substring(6);
                    int cut = rest.IndexOf('_');
                    if (cut <= 0 || !System.Enum.TryParse(rest.Substring(0, cut), out TraceKind kind) || !Traces.ContainsKey(kind)) break;
                    var t = Traces[kind];
                    switch (rest.Substring(cut + 1))
                    {
                        case "trust": t.trust = v; break;
                        case "sigma0": t.sigma0 = v; break;
                        case "sigma_per_second": t.sigmaPerSecond = v; break;
                    }
                    Traces[kind] = t;
                    break;
            }
        }

        static readonly Dictionary<string, SenseModel> cache = new Dictionary<string, SenseModel>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cache.Clear();

        public static void Forget() => cache.Clear();

        // s0: today's. s1, s2: the pack's (SenseWeights_s1 / _s2), else the build's generated
        // (Kehai.Karen.Generated.SenseWeightsTable.Build("s1")), else today's numbers, said so.
        public static SenseModel For(string id)
        {
            id = string.IsNullOrEmpty(id) ? "s0" : id.Trim().ToLowerInvariant();
            if (id == "s0") return Hand();
            IReadOnlyDictionary<string, float> fromPack = ModelPack.Current?.Weights("SenseWeights_" + id);
            if (fromPack != null)
            {
                var p = new SenseModel { Id = id, Version = "pack", Trained = true };
                foreach (var kv in fromPack) p.Set(kv.Key, kv.Value);
                return p;
            }
            if (cache.TryGetValue(id, out SenseModel s)) return s;
            System.Type type = typeof(SenseModel).Assembly.GetType("Kehai.Karen.Generated.SenseWeightsTable");
            s = type?.GetMethod("Build", new[] { typeof(string) })?.Invoke(null, new object[] { id }) as SenseModel;
            if (s == null)
            {
                s = Hand();
                s.Id = id;
                s.Version = "hand (no fitted " + id + " yet)";
            }
            cache[id] = s;
            return s;
        }
    }
}
