using System.Collections.Generic;
using System.IO;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Replay
{
    // What the shift sounded like, rebuilt from its events: every noise at the place it was
    // made, her warnings, the PA's chime and voice, the breakers. The game's own sounds where it
    // makes them from code (ProceduralAudio), stand-ins where it plays a clip from the art
    // folder. Played live in 3D while the replay runs; mixed into a WAV for a rendered shot,
    // heard from wherever that shot's camera is.
    public static class ReplaySound
    {
        public const int MixRate = 48000;
        const float Near = 2f, Far = 30f;         // OneShotAudio's rolloff

        public struct Sound
        {
            public AudioClip Clip;
            public float Volume;
            public bool Everywhere;               // the PA and the breakers: no place, no distance
        }

        // The sound an event makes, if any. `i` varies the footsteps.
        public static bool For(in ReplayEvent e, int i, out Sound s)
        {
            s = default;
            switch (e.Type)
            {
                case KrecEvent.Noise:
                    s = Noise((NoiseKind)e.Kind, e.Value, i);
                    return s.Clip != null;
                case KrecEvent.Tell:
                    s = new Sound { Clip = ProceduralAudio.Tell((TellKind)e.Kind), Volume = 0.8f };
                    return true;
                case KrecEvent.PaChime:
                    s = new Sound { Clip = ProceduralAudio.PaChime(), Volume = 0.6f, Everywhere = true };
                    return true;
                case KrecEvent.PaSpeech:
                    s = new Sound { Clip = ProceduralAudio.Voice(e.Text ?? ""), Volume = 0.7f, Everywhere = true };
                    return true;
                case KrecEvent.Circuits:
                    s = new Sound { Clip = ProceduralAudio.BreakerThrow(), Volume = 0.45f, Everywhere = true };
                    return true;
            }
            return false;
        }

        static Sound Noise(NoiseKind kind, float loudness, int i)
        {
            float v = Mathf.Clamp01(0.45f + loudness);
            switch (kind)
            {
                case NoiseKind.Footstep: return new Sound { Clip = ProceduralAudio.PlayerStep(i % 3), Volume = 0.35f * v };
                case NoiseKind.CrouchStep: return new Sound { Clip = ProceduralAudio.PlayerStep(i % 3), Volume = 0.12f };
                case NoiseKind.Sprint: return new Sound { Clip = ProceduralAudio.PlayerStep(i % 3), Volume = 0.6f * v };
                case NoiseKind.KarenStep: return new Sound { Clip = ProceduralAudio.KarenStep(), Volume = 0.55f * v };
                case NoiseKind.DroppedItem:
                case NoiseKind.Impact:
                case NoiseKind.KickedItem: return new Sound { Clip = Thud(), Volume = 0.8f * v };
                case NoiseKind.Mopping: return new Sound { Clip = Swish(), Volume = 0.4f };
                case NoiseKind.Stocking: return new Sound { Clip = Clack(), Volume = 0.5f };
                case NoiseKind.TrashRustle: return new Sound { Clip = Rustle(), Volume = 0.5f };
                case NoiseKind.Coffee: return new Sound { Clip = ProceduralAudio.Pour(), Volume = 0.6f };
                case NoiseKind.AutoDoor: return new Sound { Clip = ProceduralAudio.Tell(TellKind.DoorMotor), Volume = 0.5f };
                case NoiseKind.Door: return new Sound { Clip = Creak(), Volume = 0.55f };
                case NoiseKind.TimeClock:
                case NoiseKind.Serve: return new Sound { Clip = Beep(), Volume = 0.5f };
                case NoiseKind.CrateClearing: return new Sound { Clip = ProceduralAudio.Tell(TellKind.Scrape), Volume = 0.7f };
                case NoiseKind.Unplugging: return new Sound { Clip = ProceduralAudio.Unplug(), Volume = 0.6f };
                case NoiseKind.KarenVoice: return new Sound { Clip = ProceduralAudio.Voice("mm hm, I see, of course"), Volume = 0.55f };
                default: return default;   // her tells come as Tell events; the rest has no sound of its own
            }
        }

        // Stand-ins for the clips the game plays from its art folder.
        static AudioClip Thud() => ProceduralAudio.Make("replay_thud", 0.3f, t =>
            Mathf.Exp(-t * 28f) * (ProceduralAudio.White(t) * 0.6f + ProceduralAudio.Sine(95f, t)));
        static AudioClip Swish() => ProceduralAudio.Make("replay_swish", 0.5f, t =>
            ProceduralAudio.Env(t, 0.06f, 0.2f, 0.5f) * ProceduralAudio.White(t) * (0.6f + 0.4f * ProceduralAudio.Sine(6f, t)));
        static AudioClip Clack() => ProceduralAudio.Make("replay_clack", 0.14f, t =>
            Mathf.Exp(-t * 55f) * (ProceduralAudio.Sine(880f, t) * 0.5f + ProceduralAudio.White(t) * 0.5f));
        static AudioClip Rustle() => ProceduralAudio.Make("replay_rustle", 0.6f, t =>
            ProceduralAudio.Env(t, 0.05f, 0.25f, 0.6f) * ProceduralAudio.White(t) * (0.5f + 0.5f * ProceduralAudio.Sine(11f, t)));
        static AudioClip Creak() => ProceduralAudio.Make("replay_creak", 0.5f, t =>
            ProceduralAudio.Env(t, 0.05f, 0.15f, 0.5f) * ProceduralAudio.Sine(170f + 50f * ProceduralAudio.Sine(3f, t), t) * 0.6f);
        static AudioClip Beep() => ProceduralAudio.Make("replay_beep", 0.18f, t =>
            ProceduralAudio.Env(t, 0.005f, 0.03f, 0.18f) * ProceduralAudio.Sine(1250f, t));

        // ---- live ------------------------------------------------------------------------------

        // Plays what happened between `from` and `to` (the replay moving forward at a speed
        // people can follow).
        public static void Play(ReplayData data, float from, float to)
        {
            for (int i = data.FirstEventAt(from); i < data.Events.Count; i++)
            {
                ReplayEvent e = data.Events[i];
                if (e.T >= to) break;
                if (e.T < from || !For(e, i, out Sound s)) continue;
                if (s.Everywhere) OneShotAudio.PlayAt(s.Clip, Vector3.zero, s.Volume, Near, Far, 0f);
                else OneShotAudio.PlayAt(s.Clip, e.Position, s.Volume, Near, Far, 1f);
            }
        }

        // ---- a rendered shot's soundtrack ----------------------------------------------------

        // Stereo at 48 kHz, from `from` to `to`, heard at the camera: `listener(t)` says where it
        // was. Distance fades as OneShotAudio does; left and right by where the sound is.
        public static float[] Mix(ReplayData data, float from, float to, System.Func<float, Pose> listener)
        {
            int frames = Mathf.Max(1, Mathf.CeilToInt((to - from) * MixRate));
            var mix = new float[frames * 2];
            var cache = new Dictionary<AudioClip, float[]>();

            // Sounds that started a little before the shot are still ringing at its start.
            for (int i = data.FirstEventAt(from - 2f); i < data.Events.Count; i++)
            {
                ReplayEvent e = data.Events[i];
                if (e.T >= to) break;
                if (!For(e, i, out Sound s) || !Samples(s.Clip, cache, out float[] clip)) continue;

                float left = s.Volume, right = s.Volume;
                if (!s.Everywhere)
                {
                    Pose ear = listener(Mathf.Clamp(e.T, from, to));
                    Vector3 local = Quaternion.Inverse(ear.rotation) * (e.Position - ear.position);
                    float d = local.magnitude;
                    float fade = d <= Near ? 1f : Mathf.Clamp01(1f - (d - Near) / (Far - Near));
                    if (fade <= 0f) continue;
                    float pan = d > 0.3f ? Mathf.Clamp(local.x / d, -1f, 1f) : 0f;
                    float angle = (pan + 1f) * Mathf.PI * 0.25f;
                    left = s.Volume * fade * Mathf.Cos(angle) * 1.4142f;
                    right = s.Volume * fade * Mathf.Sin(angle) * 1.4142f;
                }

                float step = s.Clip.frequency / (float)MixRate;
                int start = Mathf.RoundToInt((e.T - from) * MixRate);
                int length = Mathf.FloorToInt((clip.Length - 1) / step);
                for (int k = Mathf.Max(0, -start); k < length; k++)
                {
                    int o = start + k;
                    if (o >= frames) break;
                    float p = k * step;
                    int j = (int)p;
                    float x = clip[j] + (clip[j + 1] - clip[j]) * (p - j);
                    mix[o * 2] += x * left;
                    mix[o * 2 + 1] += x * right;
                }
            }

            for (int i = 0; i < mix.Length; i++) mix[i] = (float)System.Math.Tanh(mix[i]);   // soft, never clipped
            return mix;
        }

        static bool Samples(AudioClip clip, Dictionary<AudioClip, float[]> cache, out float[] mono)
        {
            mono = null;
            if (clip == null) return false;
            if (cache.TryGetValue(clip, out mono)) return mono != null;
            if (clip.samples < 2) { cache[clip] = null; return false; }
            var raw = new float[clip.samples * clip.channels];
            if (!clip.GetData(raw, 0)) { cache[clip] = null; return false; }
            mono = new float[clip.samples];
            for (int i = 0; i < clip.samples; i++)
            {
                float sum = 0f;
                for (int c = 0; c < clip.channels; c++) sum += raw[i * clip.channels + c];
                mono[i] = sum / clip.channels;
            }
            cache[clip] = mono;
            return true;
        }

        // 16-bit PCM WAV, stereo.
        public static void WriteWav(string path, float[] stereo, int rate = MixRate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = stereo.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)2);
                w.Write(rate);
                w.Write(rate * 4);
                w.Write((short)4);
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                foreach (float s in stereo) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f));
            }
        }
    }
}
