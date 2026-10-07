using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Kehai.Aiko
{
    // One scored alternative in a decision.
    public struct ThoughtOption
    {
        public string Name;
        public float Utility;
        public string Why;
    }

    // One structured record per decision (IDEAS.md §4). Structured on purpose — sentences
    // can't be plotted. `Because` is the one free-text field, and it names the mechanism.
    public sealed class ThoughtRecord : IJsonWritable
    {
        public float T;
        public int Shift;
        public string Kind;              // BELIEF GOAL PLAN SENSE DIRECTOR CHECK TELL EFFECT CAUGHT ...
        public string Text;              // the human-readable line (Aiko.md §6.6)

        public string Peak;
        public float Confidence;
        public float Entropy;
        public float Stale;
        public List<string> RuledOut;

        public List<ThoughtOption> Options;
        public string Chose;
        public string Because;

        public float Panic;
        public float Target;
        public float Tension;
        public float Pressure;

        // Belief per region at the moment of the decision, for the replay scrubber. Kept
        // in memory only; the JSONL file stays small.
        public float[] BeliefSnapshot;
        public Vector3 BodyPosition;
        public Vector3 PlayerPosition;   // debug only — the Director's truth, never Aiko's

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject();
            w.Field("t", T);
            w.Field("shift", Shift);
            w.Field("kind", Kind);
            if (Peak != null)
            {
                w.Key("belief").BeginObject();
                w.Field("player_most_likely", Peak);
                w.Field("confidence", Confidence);
                w.Field("entropy", Entropy);
                w.Field("stale", Stale);
                if (RuledOut != null) w.Field("ruled_out", RuledOut);
                w.EndObject();
            }
            if (Options != null && Options.Count > 0)
            {
                w.Key("options").BeginArray();
                foreach (ThoughtOption o in Options)
                    w.BeginObject().Field("option", o.Name).Field("utility", o.Utility).Field("why", o.Why ?? string.Empty).EndObject();
                w.EndArray();
            }
            if (Chose != null) w.Field("chose", Chose);
            if (Because != null) w.Field("because", Because);
            w.Field("panic_index", Panic);
            w.Field("target", Target);
            w.Field("tension", Tension);
            w.Field("pressure", Pressure);
            w.Field("text", Text);
            w.EndObject();
        }
    }

    // Aiko's running account of her own reasoning (Aiko.md §6.6). A ring buffer in memory,
    // flushed to JSONL at the end of each shift; the debug overlay tails it, the replay
    // scrubber scrubs it, and the post-shift performance review quotes it back at you.
    public sealed class ThoughtLog
    {
        public const int Capacity = 4096;

        readonly ThoughtRecord[] ring = new ThoughtRecord[Capacity];
        int count;
        int head;

        public int Count => count;
        public bool EchoToConsole;
        public event System.Action<ThoughtRecord> Written;

        // The records of the current shift, oldest first.
        public ThoughtRecord this[int index] => ring[(head - count + index + Capacity * 2) % Capacity];

        public void Clear()
        {
            count = 0;
            head = 0;
        }

        public ThoughtRecord Write(ThoughtRecord record)
        {
            ring[head] = record;
            head = (head + 1) % Capacity;
            count = Mathf.Min(count + 1, Capacity);
            if (EchoToConsole) Debug.Log("[" + GameNames.Antagonist + "] " + record.Text);
            Written?.Invoke(record);
            return record;
        }

        public IEnumerable<ThoughtRecord> Records
        {
            get { for (int i = 0; i < count; i++) yield return this[i]; }
        }

        public IEnumerable<ThoughtRecord> Latest(int n)
        {
            for (int i = Mathf.Max(0, count - n); i < count; i++) yield return this[i];
        }

        // Writes this shift's records to <persistent>/aiko_logs/shift_<n>_<seed>.jsonl.
        public string Flush(int shift, int seed)
        {
            string folder = Path.Combine(Application.persistentDataPath, "aiko_logs");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"shift_{shift:D3}_{seed}.jsonl");

            var sb = new StringBuilder(count * 256);
            foreach (ThoughtRecord record in Records)
                sb.AppendLine(MiniJson.Serialize(record));
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        public static string Stamp(float t) => "[" + t.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(6) + "]";
    }
}
