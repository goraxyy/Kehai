using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kehai.Karen
{
    // The starting Ledger: "everyone" (0.4.0, build plan §6.1, C3.2). What each trick and each kind
    // of trick does to people on average, by the Director's phase; how soon people answer each of
    // her chores; where people hide; how much they sprint. Your Ledger is blended with it when she
    // reads it (KarenLedger.Reading), so with nothing seen of you she plays everyone's Karen.
    //
    // Where it comes from, first that exists:
    //   the model pack in use (ModelPack.Current)
    //   the build's generated table (Kehai.Karen.Generated.LedgerStartTable, from tools/learn/fit_start.py)
    //   the hand-set guesses: each trick's authored PanicPrior, and its kind's mean
    // It's read-only while you play: nothing in the game writes it (C3.5's test).
    public sealed class LedgerStart
    {
        public string Version = "hand";
        public bool Fitted;
        public float SprintShare = 0.2f;
        public float DefaultAnswerSeconds = 45f;

        // Per key: [all phases, then one per KarenDirector.Phase]; NaN where a phase has no number.
        readonly Dictionary<string, float[]> tricks = new Dictionary<string, float[]>();
        readonly Dictionary<string, float[]> kinds = new Dictionary<string, float[]>();
        readonly Dictionary<string, float> answers = new Dictionary<string, float>();
        public readonly List<KeyValuePair<string, float>> Hides = new List<KeyValuePair<string, float>>();     // region → share of people's hides
        public readonly List<KeyValuePair<string, float>> Routes = new List<KeyValuePair<string, float>>();    // "from>to|path" → share

        public static readonly int Phases = System.Enum.GetValues(typeof(KarenDirector.Phase)).Length;

        public IEnumerable<string> TrickKeys => tricks.Keys;
        public IEnumerable<string> KindKeys => kinds.Keys;
        public IEnumerable<string> AnswerKeys => answers.Keys;

        public void SetTrick(string id, int phase, float value) => Set(tricks, id, phase, value);
        public void SetKind(string kind, int phase, float value) => Set(kinds, kind, phase, value);
        public void SetAnswer(string kind, float seconds) => answers[kind] = seconds;

        static void Set(Dictionary<string, float[]> d, string key, int phase, float value)
        {
            if (!d.TryGetValue(key, out float[] row))
            {
                row = Enumerable.Repeat(float.NaN, Phases + 1).ToArray();
                d[key] = row;
            }
            row[Mathf.Clamp(phase + 1, 0, Phases)] = value;
        }

        public float[] TrickRow(string id) => tricks.TryGetValue(id, out float[] r) ? r : null;
        public float[] KindRow(string kind) => kinds.TryGetValue(kind, out float[] r) ? r : null;

        // Everyone's Δpanic for a trick in a phase (-1: any phase).
        public float Trick(string id, int phase = -1)
        {
            if (tricks.TryGetValue(id, out float[] row)) return Read(row, phase);
            Tactic t = TacticLibrary.Get(id);
            return t != null ? t.PanicPrior : 0f;   // a new trick starts from its authored guess
        }

        public float Kind(string kind, int phase = -1)
        {
            if (kinds.TryGetValue(kind, out float[] row)) return Read(row, phase);
            var same = TacticLibrary.All.Where(t => KarenLedger.KindOf(t) == kind).ToList();
            return same.Count > 0 ? same.Average(t => t.PanicPrior) : 0f;
        }

        public float AnswerSeconds(string kind) => answers.TryGetValue(kind, out float s) ? s : DefaultAnswerSeconds;

        static float Read(float[] row, int phase)
        {
            int i = phase + 1;
            if (phase >= 0 && i < row.Length && !float.IsNaN(row[i])) return row[i];
            return float.IsNaN(row[0]) ? 0f : row[0];
        }

        // As a model pack's part (the same format tools/learn/fit_start.py writes).
        public string ToPartJson()
        {
            string R(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"kind\":\"start\",\"name\":\"LedgerStart\",\"card\":{\"version\":").Append(MiniJson.Escape(Version))
              .Append(",\"trained\":").Append(Fitted ? "true" : "false").Append("},\"sprint_share\":").Append(R(SprintShare));
            void Rows(string key, Dictionary<string, float[]> d)
            {
                sb.Append(",\"").Append(key).Append("\":{");
                bool first = true;
                foreach (var kv in d.OrderBy(k => k.Key, System.StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(MiniJson.Escape(kv.Key)).Append(":[");
                    for (int i = 0; i < kv.Value.Length; i++) { if (i > 0) sb.Append(','); sb.Append(float.IsNaN(kv.Value[i]) ? "null" : R(kv.Value[i])); }
                    sb.Append(']');
                }
                sb.Append('}');
            }
            Rows("tricks", tricks);
            Rows("kinds", kinds);
            sb.Append(",\"answers\":{").Append(string.Join(",", answers.Select(a => MiniJson.Escape(a.Key) + ":" + R(a.Value)))).Append("}");
            sb.Append(",\"hides\":[").Append(string.Join(",", Hides.Select(h => "[" + MiniJson.Escape(h.Key) + "," + R(h.Value) + "]"))).Append("]}");
            return sb.ToString();
        }

        // The hand-set guesses: every trick's PanicPrior.
        public static LedgerStart Hand()
        {
            var s = new LedgerStart { Version = "hand", Fitted = false };
            foreach (Tactic t in TacticLibrary.All) s.SetTrick(t.Id, -1, t.PanicPrior);
            foreach (var g in TacticLibrary.All.GroupBy(KarenLedger.KindOf)) s.SetKind(g.Key, -1, g.Average(t => t.PanicPrior));
            return s;
        }

        static LedgerStart cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cached = null;

        public static void Forget() => cached = null;

        public static LedgerStart Current
        {
            get
            {
                LedgerStart fromPack = ModelPack.Current?.Start;
                if (fromPack != null) return fromPack;
                if (cached != null) return cached;
                System.Type type = typeof(LedgerStart).Assembly.GetType("Kehai.Karen.Generated.LedgerStartTable");
                cached = type?.GetMethod("Build", System.Type.EmptyTypes)?.Invoke(null, null) as LedgerStart ?? Hand();
                return cached;
            }
        }
    }
}
