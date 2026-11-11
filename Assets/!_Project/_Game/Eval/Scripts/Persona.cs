using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Kehai.Eval
{
    // A simulated player's character (0.4.0, build plan §7.2, C2.1): 14 numbers between 0 and 1,
    // set once per career. They set the reflexes (BotTraits) and the weights of the trained mind's
    // reward (EmployeeReward), so one network can play every character.
    //
    // The four scripted profiles are four named points in this space. A named point keeps its
    // profile's own tables and seeded jitter (BotTraits.For) exactly, so a career built from
    // Persona.Of(Reckless) plays as the reckless bot always has. Every other persona is mapped to
    // reflexes by the smooth rules in ToTraits.
    public enum Trait
    {
        Diligence,      // skips small jobs … every job, the queue first
        Caution,        // walks right past her … keeps far away, hides early
        Pace,           // walks … sprints everywhere
        Habit,          // varies routes and spots … the same every time
        Consistency,    // erratic … the same choice in the same situation
        Attention,      // misses her sounds while busy … notices everything, reacts fast
        Curiosity,      // ignores odd things … goes to look
        Grudge,         // forgets where it was caught … avoids that place for shifts
        CalmingDown,    // stays scared for a minute … shrugs it off in seconds
        FearOfDark,     // what scares them: the dark
        FearOfSounds,   //   sounds and the PA
        FearOfHer,      //   seeing her
        FearOfDashes,   //   her dashes (close-ins, vanishes, step-outs)
        FearOfChase     //   being chased
    }

    [System.Serializable]
    public sealed class Persona
    {
        public const int Count = 14;
        public static readonly string[] Names = System.Enum.GetNames(typeof(Trait));

        public readonly float[] Traits = new float[Count];
        public string Name = "persona";
        // Set on the four named points (and only there): their reflexes are the profile's own.
        public PlayerProfile? Scripted;

        public float this[Trait t]
        {
            get => Traits[(int)t];
            set => Traits[(int)t] = Mathf.Clamp01(value);
        }

        public Persona() { }

        public Persona(string name, params float[] traits)
        {
            Name = name;
            for (int i = 0; i < Count && i < traits.Length; i++) Traits[i] = Mathf.Clamp01(traits[i]);
        }

        public Persona Clone()
        {
            var p = new Persona { Name = Name, Scripted = Scripted };
            System.Array.Copy(Traits, p.Traits, Count);
            return p;
        }

        // ---- the named points ------------------------------------------------------------------

        //                                            dil  cau  pace hab  con  att  cur  grd  calm dark snd  her  dash chase
        static readonly float[] EfficientPoint = { 0.9f, 0.4f, 0.6f, 0.5f, 0.8f, 0.5f, 0.2f, 0.3f, 0.7f, 0.3f, 0.3f, 0.5f, 0.5f, 0.7f };
        static readonly float[] SkittishPoint  = { 0.6f, 0.7f, 0.6f, 0.4f, 0.4f, 0.9f, 0.4f, 0.6f, 0.2f, 1.0f, 0.9f, 1.0f, 1.0f, 0.9f };
        static readonly float[] RecklessPoint  = { 0.7f, 0.1f, 1.0f, 0.3f, 0.6f, 0.2f, 0.1f, 0.1f, 0.9f, 0.1f, 0.05f, 0.1f, 0.3f, 0.8f };
        static readonly float[] CautiousPoint  = { 0.7f, 0.9f, 0.2f, 0.7f, 0.8f, 0.8f, 0.2f, 0.8f, 0.4f, 0.5f, 0.5f, 0.9f, 1.0f, 0.8f };

        public static Persona Of(PlayerProfile profile)
        {
            float[] row;
            switch (profile)
            {
                case PlayerProfile.Skittish: row = SkittishPoint; break;
                case PlayerProfile.Reckless: row = RecklessPoint; break;
                case PlayerProfile.Cautious: row = CautiousPoint; break;
                default: row = EfficientPoint; break;
            }
            return new Persona(profile.ToString().ToLowerInvariant(), row) { Scripted = profile };
        }

        // Vital's sparring partner (build plan §7.2, C2.8): all caution and attention; it wins only
        // by clocking out. Not a realistic player, and never one Basic is judged against.
        public static Persona Survivor => new Persona("survivor",
            0.5f, 1f, 0.5f, 0.2f, 0.5f, 1f, 0f, 1f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);

        public bool IsSurvivor => Name == "survivor";

        // The scripted profile nearest this persona, for the heuristic's few profile-shaped choices
        // (who sprints everywhere, who waits longest in hiding).
        public PlayerProfile NearestProfile()
        {
            if (Scripted.HasValue) return Scripted.Value;
            PlayerProfile best = PlayerProfile.Efficient;
            float bestD = float.MaxValue;
            foreach (PlayerProfile p in System.Enum.GetValues(typeof(PlayerProfile)))
            {
                float d = Distance(Of(p));
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        public float Distance(Persona other)
        {
            float s = 0f;
            for (int i = 0; i < Count; i++) s += (Traits[i] - other.Traits[i]) * (Traits[i] - other.Traits[i]);
            return Mathf.Sqrt(s);
        }

        // ---- reflexes ----------------------------------------------------------------------------

        // The reflexes this persona has. Named points: their profile's own (identical to today).
        public BotTraits ToTraits(int seed)
        {
            if (Scripted.HasValue) return BotTraits.For(Scripted.Value, seed);

            var t = new BotTraits();
            float caution = this[Trait.Caution], pace = this[Trait.Pace], attention = this[Trait.Attention];
            float calm = this[Trait.CalmingDown];
            float dark = this[Trait.FearOfDark], sound = this[Trait.FearOfSounds], her = this[Trait.FearOfHer];
            float dash = this[Trait.FearOfDashes], chase = this[Trait.FearOfChase];
            float fearful = (dark + sound + her + dash + chase) / 5f;

            // Columns follow the Stimulus order: SawHer HerSteps Light Sound Pa Hud Social Her Blackout CloseIn Vanish Appeared Chase
            float[] row =
            {
                her, sound, dark, sound, sound, 0.5f * (sound + attention), 0.5f * (her + attention), her,
                dark, dash, dash, dash, chase
            };
            t.Tau = Mathf.Lerp(35f, 6f, calm);
            t.SprintAt = Mathf.Lerp(0.7f, 0f, pace);
            t.AbandonAt = Mathf.Lerp(0.95f, 0.4f, caution);
            t.HideAt = caution < 0.3f ? 2f : Mathf.Lerp(1.0f, 0.5f, caution);
            t.FreezeOnStartle = caution * (1f - calm) > 0.35f;
            t.DropChance = 0.35f * Mathf.Clamp01((fearful - 0.5f) * 2f) * (1f - attention * 0.5f);
            t.AvoidSeconds = Mathf.Lerp(10f, 30f, caution);
            t.AvoidPenalty = Mathf.Lerp(10f, 80f, caution);
            t.LookBase = 0.9f * attention * caution;
            t.LookGain = Mathf.Lerp(0.3f, 0.8f, fearful);
            t.Hurries = pace > 0.35f;
            t.KeepClear = Mathf.Lerp(4f, 9f, caution);
            t.StepsClear = attention * caution > 0.3f ? Mathf.Lerp(0f, 10f, caution) : 0f;
            t.TurnBackAt = Mathf.Lerp(8f, 22f, caution);
            t.DodgeChance = Mathf.Lerp(0.5f, 0.8f, attention);
            t.DodgeReaction = Mathf.Lerp(0.4f, 0.25f, attention);

            // The same per-career jitter as the scripted profiles, so no two careers are the same.
            var random = new System.Random(seed * 7349 + 4 + Hash());
            for (int i = 0; i < row.Length; i++)
                t.Susceptibility[i] = (0.1f + 1.3f * row[i]) * (0.75f + 0.5f * (float)random.NextDouble());
            t.Tau *= 0.85f + 0.3f * (float)random.NextDouble();
            return t;
        }

        int Hash()
        {
            int h = 17;
            foreach (float f in Traits) h = h * 31 + Mathf.RoundToInt(f * 1000f);
            return h & 0x3fffffff;
        }

        // ---- the reward's weights (§7.3) ---------------------------------------------------------

        public EmployeeRewardWeights Weights => EmployeeRewardWeights.For(this);

        // ---- drawing and text ----------------------------------------------------------------------

        // Each trait uniform between its range's ends.
        public static Persona Draw(System.Random random, PersonaRanges ranges, string name = "drawn")
        {
            var p = new Persona { Name = name };
            for (int i = 0; i < Count; i++)
                p.Traits[i] = Mathf.Lerp(ranges.Low[i], ranges.High[i], (float)random.NextDouble());
            return p;
        }

        // "0.9,0.4,…" (14 numbers), or a named point's name.
        public static bool TryParse(string text, out Persona persona)
        {
            persona = null;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (text.ToLowerInvariant() == "survivor") { persona = Survivor; return true; }
            if (System.Enum.TryParse(text, true, out PlayerProfile profile) && !char.IsDigit(text[0])) { persona = Of(profile); return true; }
            string[] parts = text.Split(',');
            if (parts.Length != Count) return false;
            var values = new float[Count];
            for (int i = 0; i < Count; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return false;
            persona = new Persona("persona", values);
            return true;
        }

        public override string ToString() => string.Join(",", Traits.Select(f => f.ToString("0.###", CultureInfo.InvariantCulture)));
    }

    // Where personas are drawn from for training (§7.6): each trait between Low and High. Fitted to
    // real players' ranges by tools/learn/fit_personas.py; the default is all of 0–1.
    public sealed class PersonaRanges
    {
        public readonly float[] Low = new float[Persona.Count];
        public readonly float[] High = Enumerable.Repeat(1f, Persona.Count).ToArray();

        public static PersonaRanges Everything => new PersonaRanges();
    }
}
