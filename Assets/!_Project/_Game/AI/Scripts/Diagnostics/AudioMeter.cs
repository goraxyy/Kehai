using UnityEngine;

namespace Kehai.Karen
{
    // How loud the game's own output is, measured at the listener — shown on the F1 map so
    // "I can't hear anything" can be told apart: if this moves, the game is making sound and
    // the problem is the speakers or system volume; if it's flat, nothing is playing.
    [RequireComponent(typeof(AudioListener))]
    public sealed class AudioMeter : MonoBehaviour
    {
        public static AudioMeter Instance { get; private set; }

        volatile float blockPeak;
        public float Level { get; private set; }            // 0..1, smoothed peak
        public float LastSoundAt { get; private set; } = -99f;

        public static AudioMeter Ensure()
        {
            if (Instance != null) return Instance;
            AudioListener ear = FindAnyObjectByType<AudioListener>();
            return ear == null ? null : ear.gameObject.GetOrAdd<AudioMeter>();
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float peak = blockPeak;
            blockPeak = 0f;
            Level = peak > Level ? peak : Mathf.MoveTowards(Level, peak, Time.unscaledDeltaTime * 1.5f);
            if (peak > 0.004f) LastSoundAt = Time.unscaledTime;
        }

        // Runs on the audio thread with the finished mix; only reads it.
        void OnAudioFilterRead(float[] data, int channels)
        {
            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i] < 0f ? -data[i] : data[i];
                if (v > peak) peak = v;
            }
            if (peak > blockPeak) blockPeak = peak;
        }
    }
}
