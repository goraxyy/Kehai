using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Kehai.Karen
{
    // Learning records (0.4.0, build plan C1.1): one line per decision she makes, for judging a new
    // brain on old shifts and for telling "what she'd do for anyone" from "what she did because of
    // your Ledger" (§6.4 rule 4).
    //
    // Each line says what she knew and what followed:
    //   her inputs, exactly as the brain sees them (PolicyObservation, under Decision/, so the
    //   source scan holds them to what she may know), and which version of them;
    //   the legal picks, her pick and its chance;
    //   which brain picked, and its version;
    //   the Panic Index then, and its change 10 s and 20 s later; a catch within 30 s;
    //   whether the Director's bounded hint toward your area fired from 10 s before to 20 s after
    //   (Karen.md §9.2): those windows are flagged so a fit can leave them out.
    //
    // Version 1 records a pick of a trick for the action utility chose (today's planner). C5.1's
    // seam writes version 2: picks are (action, trick) pairs such as "flush:blackout".
    public sealed class LearningRecord
    {
        public const int Version = 1;

        public int Shift;
        public int Seed;
        public string Player = "";
        public string Rung = "";
        public float T;                      // seconds into the shift
        public float Now;                    // Time.time, for the windows
        public string Brain = "planner";     // planner, basic or vital
        public string BrainVersion = "";     // the model card's version, or "hand" for the planner
        public string Goal = "";             // the action this pick is for
        public int Phase = -1;               // the Director's phase (KarenDirector.Phase), for fits by phase
        public int InputsVersion;
        public float[] Inputs;
        public string[] Legal;
        public string Pick = "";
        public float Chance = 1f;            // the chance she gave her pick; 1 for the planner's argmax
        public float Panic;
        public float Panic10 = float.NaN, Panic20 = float.NaN;
        public bool Caught30;
        public bool Hint;
        public float BiasAt;                 // the Director's total hint when she decided

        public bool Settled(float now) => now - Now >= 30f;

        public string ToJson()
        {
            var sb = new StringBuilder(256 + (Inputs?.Length ?? 0) * 8);
            sb.Append("{\"v\":").Append(Version)
              .Append(",\"shift\":").Append(Shift)
              .Append(",\"seed\":").Append(Seed)
              .Append(",\"player\":").Append(MiniJson.Escape(Player))
              .Append(",\"rung\":").Append(MiniJson.Escape(Rung))
              .Append(",\"t\":").Append(N(T))
              .Append(",\"brain\":").Append(MiniJson.Escape(Brain))
              .Append(",\"brain_version\":").Append(MiniJson.Escape(BrainVersion))
              .Append(",\"goal\":").Append(MiniJson.Escape(Goal))
              .Append(",\"phase\":").Append(Phase)
              .Append(",\"inputs_version\":").Append(InputsVersion)
              .Append(",\"inputs\":[");
            if (Inputs != null)
                for (int i = 0; i < Inputs.Length; i++) { if (i > 0) sb.Append(','); sb.Append(N(Inputs[i])); }
            sb.Append("],\"legal\":[");
            if (Legal != null)
                for (int i = 0; i < Legal.Length; i++) { if (i > 0) sb.Append(','); sb.Append(MiniJson.Escape(Legal[i])); }
            sb.Append("],\"pick\":").Append(MiniJson.Escape(Pick))
              .Append(",\"chance\":").Append(N(Chance))
              .Append(",\"panic\":").Append(N(Panic))
              .Append(",\"d_panic_10\":").Append(float.IsNaN(Panic10) ? "null" : N(Panic10 - Panic))
              .Append(",\"d_panic_20\":").Append(float.IsNaN(Panic20) ? "null" : N(Panic20 - Panic))
              .Append(",\"caught_30\":").Append(Caught30 ? "true" : "false")
              .Append(",\"hint\":").Append(Hint ? "true" : "false")
              .Append('}');
            return sb.ToString();
        }

        static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "0" : v.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    // Keeps a shift's records open until their windows have passed, then writes them. Plain C#:
    // the brain tells it what happened, it reads nothing itself.
    public sealed class LearningRecorder
    {
        public bool Enabled;
        public string FilePath;              // null: kept in memory only (tests read Written)
        public readonly List<LearningRecord> Written = new List<LearningRecord>();
        public int WrittenThisShift { get; private set; }

        readonly List<LearningRecord> open = new List<LearningRecord>();
        float lastHintAt = -999f;

        public static string DefaultPath(string stamp) =>
            Path.Combine(Application.persistentDataPath, "learning", $"learning_{stamp}.jsonl");

        public void BeginShift()
        {
            open.Clear();
            WrittenThisShift = 0;
            lastHintAt = -999f;
        }

        public void Add(LearningRecord r)
        {
            if (!Enabled || r == null) return;
            r.Hint = r.Now - lastHintAt <= 10f;
            open.Add(r);
        }

        // The Director's hint moved her belief at `now`.
        public void HintFired(float now)
        {
            lastHintAt = now;
            foreach (LearningRecord r in open)
                if (now - r.Now <= 20f) r.Hint = true;
        }

        // At the Director's rate: the Panic Index now, for the 10 s and 20 s windows.
        public void Tick(float now, float panic)
        {
            if (!Enabled) return;
            for (int i = open.Count - 1; i >= 0; i--)
            {
                LearningRecord r = open[i];
                float age = now - r.Now;
                if (float.IsNaN(r.Panic10) && age >= 10f) r.Panic10 = panic;
                if (float.IsNaN(r.Panic20) && age >= 20f) r.Panic20 = panic;
                if (r.Settled(now))
                {
                    open.RemoveAt(i);
                    Write(r);
                }
            }
        }

        // She caught you: every record from the last 30 s led to a catch.
        public void Caught(float now)
        {
            foreach (LearningRecord r in open)
                if (now - r.Now <= 30f) r.Caught30 = true;
        }

        // The shift is over: what's still open is written with what's known (a window the shift
        // didn't reach stays null).
        public void EndShift()
        {
            open.Sort((a, b) => a.Now.CompareTo(b.Now));
            foreach (LearningRecord r in open) Write(r);
            open.Clear();
        }

        void Write(LearningRecord r)
        {
            Written.Add(r);
            WrittenThisShift++;
            if (Written.Count > 4096) Written.RemoveRange(0, 2048);   // a long session stays small
            if (string.IsNullOrEmpty(FilePath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.AppendAllText(FilePath, r.ToJson() + "\n");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{GameNames.Antagonist}: learning record not written ({e.Message}); recording stops");
                FilePath = null;
            }
        }
    }
}
