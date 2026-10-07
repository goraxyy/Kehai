using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Aiko
{
    // Every warning a tactic gives (AIKO.md §9.3). Each is a distinct sound, so a player
    // who learns them can read what is about to happen.
    public enum TellKind
    {
        BallastWhine,     // blackout: a rising whine and a flicker
        Flicker,          // a single light about to die
        ShelfRattle,      // a bay about to be stripped
        BucketClank,      // a spill about to be kicked over
        LidClatter,       // a bin being tampered with
        MopRattle,        // the mop being lifted off its rack
        CrtTick,          // the HUD about to lie
        Drilling,         // a camera being bolted on
        Scrape,           // crates being dragged
        Clunk,            // a door lock engaging
        FreezerHiss,      // a freezer venting fog
        CustomerFreeze,   // a shopper stopping dead
        Screech,          // the chase
        SilenceFalls,     // her footsteps stopping
        PunchBuzz,        // the time clock refusing
        HoldMusic,        // withdrawal
        EyeFlicker,       // she has started watching your blinks
        Grinding,         // shelving on castors
        SpeakerCrackle,   // the PA about to play something that isn't speech
        DoorMotor,        // automatic doors about to cycle
        PaChime,          // an announcement
        Footsteps         // her own steps, from somewhere else
    }

    // All of Aiko's sounds, synthesised at load. The project keeps audio clips out of the
    // repository, and every one of these is short and simple enough to build from sines
    // and noise — which also makes them unmistakably *hers*: nothing else in the store
    // sounds like this.
    public static class ProceduralAudio
    {
        const int Rate = 44100;
        const float Loudness = 0.85f;   // peak level of every generated clip
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cache.Clear();

        // Also used by the replay for the sounds it has to stand in for.
        internal static AudioClip Make(string name, float seconds, System.Func<float, float> sample)
        {
            if (cache.TryGetValue(name, out AudioClip clip) && clip != null) return clip;

            int n = Mathf.Max(1, Mathf.CeilToInt(seconds * Rate));
            var data = new float[n];
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                data[i] = Mathf.Clamp(sample((float)i / Rate), -1f, 1f);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }

            // Every clip leaves here at the same peak level, so how loud a sound is in the
            // game is decided by where it's played and at what volume — not by whichever
            // constant happened to be in its formula. (Several peaked at 0.2 and were lost.)
            if (peak > 1e-4f)
            {
                float gain = Loudness / peak;
                for (int i = 0; i < n; i++) data[i] *= gain;
            }

            clip = AudioClip.Create("Aiko_" + name, n, 1, Rate, false);
            clip.SetData(data, 0);
            cache[name] = clip;
            return clip;
        }

        // Deterministic noise so the clips are identical every run.
        internal static float Hash(int i) { unchecked { uint x = (uint)i * 747796405u + 2891336453u; x = ((x >> ((int)(x >> 28) + 4)) ^ x) * 277803737u; return ((x >> 22) ^ x) / (float)uint.MaxValue * 2f - 1f; } }
        internal static float White(float t) => Hash((int)(t * Rate));
        internal static float Env(float t, float attack, float release, float length) =>
            Mathf.Clamp01(t / Mathf.Max(0.0001f, attack)) * Mathf.Clamp01((length - t) / Mathf.Max(0.0001f, release));
        internal static float Sine(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);

        public static AudioClip Tell(TellKind kind)
        {
            switch (kind)
            {
                case TellKind.BallastWhine:
                    return Make("ballast", 1.3f, t => 0.35f * Env(t, 0.1f, 0.1f, 1.3f) *
                        (Sine(Mathf.Lerp(6000f, 9000f, t / 1.3f), t) * 0.4f + Sine(120f, t) * 0.8f + Sine(240f, t) * 0.3f)
                        * (Mathf.Repeat(t * 11f, 1f) < 0.8f ? 1f : 0.2f));
                case TellKind.Flicker:
                    return Make("flicker", 0.9f, t => 0.3f * Env(t, 0.01f, 0.1f, 0.9f) * Sine(120f, t) * (Mathf.Repeat(t * 17f, 1f) < 0.5f ? 1f : 0f));
                case TellKind.ShelfRattle:
                    return Make("rattle", 1.1f, t => 0.45f * Env(t, 0.05f, 0.2f, 1.1f) * White(t) * (0.5f + 0.5f * Sine(23f, t)));
                case TellKind.BucketClank:
                    return Make("clank", 1f, t => 0.6f * Mathf.Exp(-t * 6f) * (Sine(620f, t) + Sine(931f, t) * 0.6f + Sine(1440f, t) * 0.3f));
                case TellKind.LidClatter:
                    return Make("lid", 0.9f, t => 0.5f * Mathf.Exp(-Mathf.Repeat(t, 0.22f) * 18f) * (Sine(880f, t) * 0.5f + White(t) * 0.5f));
                case TellKind.MopRattle:
                    return Make("mop", 0.9f, t => 0.4f * Env(t, 0.02f, 0.2f, 0.9f) * White(t) * Mathf.Abs(Sine(9f, t)));
                case TellKind.CrtTick:
                    return Make("crt", 0.85f, t => 0.25f * (Sine(15700f, t) * Env(t, 0.01f, 0.1f, 0.85f) * 0.3f + (t < 0.012f ? White(t) : 0f) * 2f));
                case TellKind.Drilling:
                    return Make("drill", 1.6f, t => 0.35f * Env(t, 0.1f, 0.15f, 1.6f) * (Sine(340f + 40f * Sine(7f, t), t) * 0.6f + White(t) * 0.4f));
                case TellKind.Scrape:
                    return Make("scrape", 1.2f, t => 0.45f * Env(t, 0.1f, 0.2f, 1.2f) * White(t) * (0.6f + 0.4f * Sine(3f, t)));
                case TellKind.Clunk:
                    return Make("clunk", 0.9f, t => 0.7f * Mathf.Exp(-t * 12f) * (Sine(90f, t) + Sine(180f, t) * 0.5f));
                case TellKind.FreezerHiss:
                    return Make("hiss", 1.2f, t => 0.35f * Env(t, 0.2f, 0.2f, 1.2f) * White(t));
                case TellKind.CustomerFreeze:
                    return Make("freeze", 1f, t => 0.2f * Env(t, 0.3f, 0.3f, 1f) * Sine(55f, t));
                case TellKind.Screech:
                    return Make("screech", 1.1f, t => 0.5f * Env(t, 0.05f, 0.2f, 1.1f) * (Sine(Mathf.Lerp(900f, 1400f, t), t) * 0.5f + Sine(Mathf.Lerp(930f, 1470f, t), t) * 0.5f));
                case TellKind.SilenceFalls:
                    return Make("lastStep", 0.9f, t => 0.5f * Mathf.Exp(-t * 20f) * (Sine(70f, t) + White(t) * 0.3f));
                case TellKind.PunchBuzz:
                    return Make("buzz", 0.9f, t => 0.35f * Env(t, 0.01f, 0.05f, 0.9f) * Mathf.Sign(Sine(110f, t)));
                case TellKind.HoldMusic:
                    return HoldMusic();
                case TellKind.EyeFlicker:
                    return Make("eye", 0.9f, t => 0.15f * Env(t, 0.05f, 0.3f, 0.9f) * Sine(2200f + 300f * Sine(5f, t), t));
                case TellKind.Grinding:
                    return Make("grind", 2f, t => 0.4f * Env(t, 0.2f, 0.3f, 2f) * (White(t) * 0.6f + Sine(48f, t) * 0.4f));
                case TellKind.SpeakerCrackle:
                    return Make("crackle", 0.9f, t => 0.3f * Env(t, 0.01f, 0.1f, 0.9f) * (Mathf.Abs(White(t)) > 0.92f ? White(t) : 0f) * 3f);
                case TellKind.DoorMotor:
                    return Make("motor", 0.9f, t => 0.3f * Env(t, 0.1f, 0.1f, 0.9f) * Sine(180f + 20f * Sine(3f, t), t));
                case TellKind.PaChime:
                    return PaChime();
                case TellKind.Footsteps:
                    return Make("farSteps", 3f, t => Footstep(Mathf.Repeat(t, 0.6f)) * 0.8f);
            }
            return PaChime();
        }

        // Two-tone store chime before every announcement.
        public static AudioClip PaChime() => Make("chime", 1.4f, t =>
        {
            float first = t < 0.6f ? Sine(659.25f, t) * Mathf.Exp(-t * 3f) : 0f;
            float second = t >= 0.55f ? Sine(523.25f, t) * Mathf.Exp(-(t - 0.55f) * 3f) : 0f;
            return 0.4f * (first + second);
        });

        static float Footstep(float t) => Mathf.Exp(-t * 35f) * (Sine(80f, t) * 0.7f + White(t) * 0.5f);

        public static AudioClip AikoStep() => Make("step", 0.25f, t => 0.8f * Footstep(t));

        // The employee's own steps: softer and higher than hers, so the two never get confused.
        public static AudioClip PlayerStep(int variant) => Make("pstep" + variant, 0.18f, t =>
            Mathf.Exp(-t * 45f) * (Sine(140f + variant * 17f, t) * 0.5f + Hash((int)(t * Rate) + variant * 9973) * 0.6f));
        public static AudioClip Hum(int pitch) => Make("hum" + pitch, 1.2f, t => 0.35f * Env(t, 0.1f, 0.2f, 1.2f) * (Sine(90f + pitch * 45f, t) + Sine((90f + pitch * 45f) * 2f, t) * 0.3f));
        public static AudioClip BreakerThrow() => Make("breaker", 0.4f, t => 0.8f * Mathf.Exp(-t * 25f) * (White(t) * 0.6f + Sine(140f, t)));
        public static AudioClip ErrorBuzz() => Make("error", 0.6f, t => 0.35f * Env(t, 0.01f, 0.05f, 0.6f) * Mathf.Sign(Sine(95f, t)));
        public static AudioClip Unplug() => Make("unplug", 0.35f, t => 0.6f * Mathf.Exp(-t * 18f) * (Sine(300f, t) + White(t) * 0.3f));
        public static AudioClip Pour() => Make("pour", 1.2f, t => 0.25f * Env(t, 0.1f, 0.3f, 1.2f) * White(t) * (0.5f + 0.5f * Sine(4f, t)));

        static AudioClip HoldMusic() => Make("hold", 8f, t =>
        {
            // A limp major arpeggio, deliberately cheerful.
            float[] notes = { 523.25f, 659.25f, 783.99f, 659.25f, 587.33f, 698.46f, 880f, 698.46f };
            int i = Mathf.FloorToInt(t / 0.5f) % notes.Length;
            float local = Mathf.Repeat(t, 0.5f);
            return 0.18f * Mathf.Exp(-local * 4f) * (Sine(notes[i], t) + Sine(notes[i] * 0.5f, t) * 0.5f);
        });

        // Aiko's PA "voice": no text-to-speech, but a clipped, syllabic murmur the length of
        // the line — a tannoy you can't quite make out, with the words in the subtitle.
        public static AudioClip Voice(string text)
        {
            int syllables = Mathf.Clamp(text.Length / 3, 4, 90);
            float seconds = syllables * 0.13f + 0.3f;
            string key = "voice" + syllables;
            return Make(key, seconds, t =>
            {
                int s = Mathf.FloorToInt(t / 0.13f);
                float local = Mathf.Repeat(t, 0.13f);
                float pitch = 170f + 40f * Hash(s * 7 + syllables);
                float formant = 700f + 500f * Mathf.Abs(Hash(s * 13 + 5));
                float env = Mathf.Sin(Mathf.Clamp01(local / 0.11f) * Mathf.PI);
                bool pause = Hash(s * 31) > 0.72f;
                if (pause || t > seconds - 0.3f) return 0f;
                float glottal = Mathf.Repeat(t * pitch, 1f) * 2f - 1f;
                return 0.22f * env * (glottal * 0.5f + Sine(formant, t) * 0.35f * glottal);
            });
        }
    }
}
