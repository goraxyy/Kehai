using UnityEngine;

namespace Kehai.Eval
{
    // How frightened a simulated player is (Karen.md §13), from what it can sense — the
    // observation, never Karen's internals. Plain C#, so the tests can drive it.
    //
    // Fear lives in [0, 1]. Each stimulus adds amount × susceptibility × (1 − fear), so it
    // saturates; it drains with the profile's time constant. A big jump in one go is a
    // startle, which is what makes a person freeze, drop things or give up on a job.
    public enum Stimulus
    {
        SawHer,      // in view; scaled by how near, and more when her eye is red
        HerSteps,    // footsteps close by
        TellLight,   // a light whining or flickering
        TellSound,   // a rattle, a clank, a scrape, a hiss — something about to happen
        TellPa,      // the PA chiming or crackling
        TellHud,     // the HUD ticking, the clock refusing
        TellSocial,  // a shopper stopping dead
        TellHer,     // her own warnings: the screech before a chase, her eye flicker
        Blackout,    // the lights going out
        CloseIn,     // she was suddenly much nearer, across a blink
        Vanish,      // she was there, and then she wasn't
        Appeared,    // she stepped out, close
        Chase        // she's coming, red-eyed
    }

    // One profile's temperament, jittered per seed so no two careers' players are the same.
    public sealed class BotTraits
    {
        public readonly float[] Susceptibility = new float[System.Enum.GetValues(typeof(Stimulus)).Length];
        public float Tau = 15f;            // seconds for fear to fall to 37%
        public float StartleJump = 0.25f;
        public float SprintAt = 0.5f;      // sprints while fear is above this
        public float AbandonAt = 0.8f;     // a startle above this drops the job in hand
        public float HideAt = 2f;          // runs and hides above this (2: never)
        public bool FreezeOnStartle;
        public float DropChance;           // on a big startle, drops what it carries
        public float AvoidSeconds = 20f;   // shuns where she was seen or heard for this long
        public float AvoidPenalty = 25f;   // metres added to a job's walk in a shunned region
        public float LookBase, LookGain;   // looking round: base + gain × fear
        public bool Hurries = true;        // takes long walks at a run when energy allows

        public float this[Stimulus s] => Susceptibility[(int)s];

        // Columns follow the Stimulus order:
        // SawHer HerSteps Light Sound Pa Hud Social Her Blackout CloseIn Vanish Appeared Chase
        static readonly float[] Efficient = { 0.6f, 0.3f, 0.3f, 0.2f, 0.4f, 0.6f, 0.3f, 0.4f, 0.4f, 0.6f, 0.4f, 0.7f, 0.8f };
        static readonly float[] Skittish  = { 1.2f, 1.1f, 1.3f, 1.2f, 1.1f, 1.0f, 1.1f, 1.2f, 1.4f, 1.3f, 1.2f, 1.3f, 1.2f };
        static readonly float[] Reckless  = { 0.2f, 0.1f, 0.1f, 0.05f, 0.1f, 0.1f, 0.1f, 0.3f, 0.2f, 0.5f, 0.3f, 0.6f, 1.0f };
        static readonly float[] Cautious  = { 1.2f, 0.8f, 0.6f, 0.5f, 0.6f, 0.5f, 0.6f, 0.9f, 0.7f, 1.3f, 1.3f, 1.2f, 1.0f };

        public static BotTraits For(PlayerProfile profile, int seed)
        {
            var t = new BotTraits();
            float[] row;
            switch (profile)
            {
                case PlayerProfile.Skittish:
                    row = Skittish;
                    t.Tau = 30f; t.SprintAt = 0.3f; t.AbandonAt = 0.4f; t.HideAt = 0.65f; t.FreezeOnStartle = true;
                    t.DropChance = 0.3f; t.AvoidSeconds = 30f; t.AvoidPenalty = 40f; t.LookBase = 0.9f; t.LookGain = 0.8f;
                    break;
                case PlayerProfile.Reckless:
                    row = Reckless;
                    t.Tau = 8f; t.SprintAt = 0f; t.AbandonAt = 0.9f; t.HideAt = 2f;
                    t.AvoidSeconds = 10f; t.AvoidPenalty = 10f; t.LookBase = 0.2f; t.LookGain = 0.3f;
                    break;
                case PlayerProfile.Cautious:
                    row = Cautious;
                    t.Tau = 20f; t.SprintAt = 0.5f; t.AbandonAt = 0.6f; t.HideAt = 0.55f; t.FreezeOnStartle = true;
                    t.AvoidSeconds = 30f; t.AvoidPenalty = 80f; t.LookBase = 0.5f; t.LookGain = 0.6f; t.Hurries = false;
                    break;
                default:
                    row = Efficient;
                    t.Tau = 10f; t.SprintAt = 0.45f; t.AbandonAt = 0.8f; t.HideAt = 0.95f;
                    t.AvoidSeconds = 20f; t.AvoidPenalty = 25f; t.LookBase = 0f; t.LookGain = 0.4f;
                    break;
            }

            var random = new System.Random(seed * 7349 + (int)profile);
            for (int i = 0; i < row.Length; i++)
                t.Susceptibility[i] = row[i] * (0.75f + 0.5f * (float)random.NextDouble());
            t.Tau *= 0.85f + 0.3f * (float)random.NextDouble();
            return t;
        }
    }

    public sealed class BotFear
    {
        public float Fear { get; private set; }
        public int Startles { get; private set; }
        public float Peak { get; private set; }

        float sum, seconds;

        // Base strength of each stimulus, before the player's susceptibility.
        static readonly float[] Amount = { 0.25f, 0.15f, 0.3f, 0.2f, 0.25f, 0.15f, 0.2f, 0.35f, 0.45f, 0.55f, 0.45f, 0.6f, 0.6f };

        // Returns true when this was a startle.
        public bool Feel(Stimulus s, BotTraits traits, float scale = 1f)
        {
            float before = Fear;
            float add = Amount[(int)s] * traits[s] * Mathf.Max(0f, scale);
            Fear = Mathf.Clamp01(Fear + add * (1f - Fear));
            Peak = Mathf.Max(Peak, Fear);
            bool startle = Fear - before >= traits.StartleJump;
            if (startle) Startles++;
            return startle;
        }

        public void Decay(float dt, BotTraits traits)
        {
            if (dt <= 0f) return;
            Fear *= Mathf.Exp(-dt / Mathf.Max(0.1f, traits.Tau));
            sum += Fear * dt;
            seconds += dt;
        }

        public float MeanFear => seconds > 0f ? sum / seconds : 0f;
    }
}
