using UnityEngine;

namespace Kehai.Eval
{
    public enum Split { Legacy, Training, Validation, Test }

    // Seeds and who is held out, in code (0.4.0, build plan §10.3, C1.5).
    //
    //   Training      seeds 10000–19999   the training runs, the prediction model's recordings
    //   Validation    seeds 20000–29999   choosing checkpoints and models, turning the catch dial,
    //                                     the scorecard's bands
    //   Test          seeds 30000–39999   the results in §11, run once
    //
    // Never seen by any part of Karen in training or tuning: the scripted cautious profile, the
    // careful corner's personas (caution ≥ 0.75 and pace ≤ 0.3, where the cautious profile lives),
    // and the test seeds. The bots' own network does train on the careful corner (so those bots
    // behave sensibly), but never on the cautious profile or a test seed.
    //
    // Seeds below 10000 are the ablations' own (AblationRunner's 1000 and up), from before 0.4.0.
    public static class EvalSplits
    {
        public const int TrainingFrom = 10000, ValidationFrom = 20000, TestFrom = 30000, Span = 10000;
        public const float CornerCaution = 0.75f, CornerPace = 0.3f;

        // The test runs' exact sizes (written into the plan under C1.5, before any run): eight
        // groups of players, each 8 careers of 6 shifts, 150 s shifts. 384 shifts an arm.
        public const int TestCareersPerGroup = 8, TestShiftsPerCareer = 6;
        public const float TestShiftSeconds = 150f;
        public static readonly string[] TestGroups =
            { "efficient", "skittish", "reckless", "cautious", "trained-a", "trained-b", "corner-a", "corner-b" };

        public static Split Of(int seed)
        {
            if (seed >= TrainingFrom && seed < TrainingFrom + Span) return Split.Training;
            if (seed >= ValidationFrom && seed < ValidationFrom + Span) return Split.Validation;
            if (seed >= TestFrom && seed < TestFrom + Span) return Split.Test;
            return Split.Legacy;
        }

        public static int SeedFor(Split split, int index)
        {
            if (index < 0 || index >= Span) throw new System.ArgumentOutOfRangeException(nameof(index), $"a {split} seed's index is 0–{Span - 1}");
            switch (split)
            {
                case Split.Training: return TrainingFrom + index;
                case Split.Validation: return ValidationFrom + index;
                case Split.Test: return TestFrom + index;
                default: throw new System.ArgumentException("legacy seeds aren't drawn by index");
            }
        }

        public static bool InCarefulCorner(Persona p) =>
            p != null && p[Trait.Caution] >= CornerCaution && p[Trait.Pace] <= CornerPace;

        public static bool HeldOutProfile(PlayerProfile p) => p == PlayerProfile.Cautious;

        // May any part of Karen (the brains, the prediction model, her senses, the mirror map, the
        // starting Ledger) train or tune on this career?
        public static bool KarenMayTrainOn(int seed, Persona persona, Split purpose = Split.Training)
        {
            if (purpose == Split.Test) return false;
            if (Of(seed) != purpose) return false;
            if (persona == null) return false;
            if (persona.Scripted.HasValue && HeldOutProfile(persona.Scripted.Value)) return false;
            return !InCarefulCorner(persona);
        }

        // May the bots' own network train on it? The careful corner yes; the cautious profile, no.
        public static bool PlayersMayTrainOn(int seed, Persona persona)
        {
            if (Of(seed) != Split.Training) return false;
            if (persona == null) return false;
            return !(persona.Scripted.HasValue && HeldOutProfile(persona.Scripted.Value));
        }

        // A training persona for Karen's side: drawn from the ranges, never from the careful corner.
        public static Persona DrawForKaren(System.Random random, PersonaRanges ranges, string name = "trained")
        {
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                Persona p = Persona.Draw(random, ranges, name);
                if (!InCarefulCorner(p)) return p;
            }
            throw new System.InvalidOperationException("the persona ranges lie inside the careful corner: nothing to train Karen on");
        }

        // A careful-corner persona (the bots' training, and the test's two corner groups).
        public static Persona DrawCorner(System.Random random, string name = "corner")
        {
            var ranges = new PersonaRanges();
            ranges.Low[(int)Trait.Caution] = CornerCaution;
            ranges.High[(int)Trait.Pace] = CornerPace;
            return Persona.Draw(random, ranges, name);
        }

        // Throws if a run meant for training would meet something held out. Called by every
        // training entry point (the league, the recordings), so a mistake stops the run.
        public static void AssertKarenTraining(int seed, Persona persona)
        {
            if (!KarenMayTrainOn(seed, persona))
                throw new System.InvalidOperationException(
                    $"held out: seed {seed} ({Of(seed)}), persona {persona?.Name ?? "none"}" +
                    (persona != null && InCarefulCorner(persona) ? " is in the careful corner" : "") +
                    (persona?.Scripted == PlayerProfile.Cautious ? " is the cautious profile" : ""));
        }

        public static string Describe(int seed, Persona persona) =>
            $"{Of(seed)} seed {seed}, {persona?.Name ?? "?"}{(InCarefulCorner(persona) ? " (careful corner)" : "")}";

    }
}
