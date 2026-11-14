using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // The prediction model's numbers (0.4.0, build plan §8.3–8.5): "everyone" for where people go.
    // Hand-set until C4.10 fits them (tools/belief, stage 1 from legs, stage 2 through the filter);
    // then PredictionWeights.g.cs (Kehai.Karen.Generated.PredictionWeightsTable) and the model pack
    // carry the fitted ones. Read-only while you play.
    public sealed class PredictionWeights
    {
        public string Version = "hand";
        public bool Trained;

        // P1: today's gains (the fit moves them), and her presence felt over DangerRadius metres.
        public HandMotion.Gains P1Gains = new HandMotion.Gains { MovingProbability = 0.65f, DesireGain = 0.9f, HabitGain = 1.2f, DangerAvoidance = 0.6f, SpeedScale = 1f };
        public float DangerRadius = 6f;

        // P2's choice: logit = Kind[kind] + Distance·ln(1 + metres) + Wait·waited/(waited + 30)
        //   + Between·(she stands between) + Heading·cos(your last heading, the way there)
        //   + ClockLate·(clock, as the shift ends) + (P3) LedgerAnswer·(you answer this chore fast)
        //   + (P3) LedgerHide·(one of your hiding spots)
        public readonly float[] Kind = { 0.4f, 0.8f, 0.6f, 1.2f, -0.6f, 0.2f, -1.5f, -0.4f, 0.9f, -1.2f };   // DestinationKind order
        public float Distance = -0.9f, Wait = 0.6f, Between = -1.2f, Heading = 0.6f, ClockLate = 2.0f;
        public float LedgerAnswer = 0.8f, LedgerHide = 1.0f;

        // The walk: shares of sneaking, walking and sprinting (2.5, 4 and 6.5 m/s), and how hard
        // the way there bends away from where you think she is.
        public readonly float[] SpeedShare = { 0.15f, 0.6f, 0.25f };
        public static readonly float[] Speeds = { 2.5f, 4f, 6.5f };
        public float Avoid = 0.6f;

        public float MeanSpeed
        {
            get
            {
                float s = 0f, w = 0f;
                for (int i = 0; i < Speeds.Length; i++) { s += SpeedShare[i] * Speeds[i]; w += SpeedShare[i]; }
                return w > 0f ? s / w : 4f;
            }
        }

        public void Set(string name, float value)
        {
            switch (name)
            {
                case "distance": Distance = value; break;
                case "wait": Wait = value; break;
                case "between": Between = value; break;
                case "heading": Heading = value; break;
                case "clock_late": ClockLate = value; break;
                case "ledger_answer": LedgerAnswer = value; break;
                case "ledger_hide": LedgerHide = value; break;
                case "avoid": Avoid = value; break;
                case "danger_radius": DangerRadius = value; break;
                case "p1_moving": P1Gains.MovingProbability = value; break;
                case "p1_desire": P1Gains.DesireGain = value; break;
                case "p1_habit": P1Gains.HabitGain = value; break;
                case "p1_danger": P1Gains.DangerAvoidance = value; break;
                case "p1_speed": P1Gains.SpeedScale = value; break;
                default:
                    if (name.StartsWith("kind_") && System.Enum.TryParse(name.Substring(5), true, out DestinationKind k)) Kind[(int)k] = value;
                    else if (name.StartsWith("speed_") && int.TryParse(name.Substring(6), out int i) && i >= 0 && i < SpeedShare.Length) SpeedShare[i] = value;
                    break;
            }
        }

        public static PredictionWeights Hand() => new PredictionWeights();

        static PredictionWeights cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cached = null;

        public static void Forget() => cached = null;

        // The pack's, else the build's generated numbers, else the hand-set ones.
        public static PredictionWeights Current
        {
            get
            {
                IReadOnlyDictionary<string, float> fromPack = ModelPack.Current?.Weights("PredictionWeights");
                if (fromPack != null)
                {
                    var p = new PredictionWeights { Version = "pack", Trained = true };
                    foreach (var kv in fromPack) p.Set(kv.Key, kv.Value);
                    return p;
                }
                if (cached != null) return cached;
                System.Type type = typeof(PredictionWeights).Assembly.GetType("Kehai.Karen.Generated.PredictionWeightsTable");
                cached = type?.GetMethod("Build", System.Type.EmptyTypes)?.Invoke(null, null) as PredictionWeights ?? Hand();
                return cached;
            }
        }
    }
}
