using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Kehai.Aiko
{
    public sealed class PaAnnouncement
    {
        public string Text;
        public AudioClip Clip;          // null = Aiko's voice
        public bool Done;
        public bool Jammed;
        public float Started = -1f;
    }

    // Everything Aiko puts on your screen that isn't the world: PA subtitles, written
    // warnings, the endings. One overlay canvas built in code, under the eyelids.
    public sealed class AikoScreen : MonoBehaviour
    {
        public static AikoScreen Instance { get; private set; }

        TextMeshProUGUI subtitle;
        TextMeshProUGUI banner;
        Image fade;
        float subtitleUntil, bannerUntil;

        public static AikoScreen Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("~AikoScreen");
            Instance = go.AddComponent<AikoScreen>();
            Instance.Build();
            return Instance;
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;          // under the eyelids (999), over the HUD
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            fade = MakeImage("Fade", new Color(0f, 0f, 0f, 0f));
            fade.rectTransform.anchorMin = Vector2.zero;
            fade.rectTransform.anchorMax = Vector2.one;

            subtitle = MakeText("Subtitle", 22f, TextAlignmentOptions.Bottom);
            subtitle.rectTransform.anchorMin = new Vector2(0.15f, 0.12f);
            subtitle.rectTransform.anchorMax = new Vector2(0.85f, 0.28f);

            banner = MakeText("Banner", 34f, TextAlignmentOptions.Center);
            banner.rectTransform.anchorMin = new Vector2(0.1f, 0.4f);
            banner.rectTransform.anchorMax = new Vector2(0.9f, 0.62f);
        }

        Image MakeImage(string name, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return image;
        }

        TextMeshProUGUI MakeText(string name, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            text.text = string.Empty;
            var rt = (RectTransform)go.transform;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return text;
        }

        public void Subtitle(string text, float seconds)
        {
            subtitle.text = text;
            subtitleUntil = Time.unscaledTime + seconds;
        }

        public void Banner(string text, float seconds)
        {
            banner.font = GameFonts.ForText(text, TMP_Settings.defaultFontAsset);
            banner.text = text;
            bannerUntil = Time.unscaledTime + seconds;
        }

        public void SetFade(float alpha) => fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));

        void Update()
        {
            if (subtitle.text.Length > 0 && Time.unscaledTime > subtitleUntil) subtitle.text = string.Empty;
            if (banner.text.Length > 0 && Time.unscaledTime > bannerUntil) banner.text = string.Empty;
        }
    }

    // The tannoy (aiko.md §8.3). Aiko owns it and is not obliged to be truthful. Plays
    // through the store's own ceiling speakers — the ones nearest you — with a chime first,
    // ducks the radio underneath, and puts the words on screen. Jam it at the breaker box
    // and she has no voice.
    public sealed class PaSystem : MonoBehaviour
    {
        public float volume = 0.9f;

        readonly Queue<PaAnnouncement> queue = new Queue<PaAnnouncement>();
        readonly List<AudioSource> voices = new List<AudioSource>();
        readonly List<Transform> speakers = new List<Transform>();
        PaAnnouncement current;
        float phaseEnds;
        int phase;                 // 0 idle, 1 chime, 2 speech
        MusicBox radio;
        float radioVolume = -1f;

        public const float ChimeSeconds = 1.3f;
        public event System.Action<PaAnnouncement> ChimeStarted;    // the tell
        public event System.Action<PaAnnouncement> SpeechStarted;   // the effect

        public bool Jammed => BreakerPanel.Instance != null && BreakerPanel.Instance.PaJammed;
        public bool Busy => current != null;
        public string CurrentText => current != null ? current.Text : string.Empty;
        public int Announcements { get; private set; }

        void Awake()
        {
            GameObject root = GameObject.Find("MallSpeakers");
            if (root != null) foreach (Transform t in root.transform) speakers.Add(t);
            radio = FindAnyObjectByType<MusicBox>();

            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("PaVoice" + i);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0.55f;   // everywhere, but from somewhere
                source.rolloffMode = AudioRolloffMode.Linear;
                source.maxDistance = 40f;
                voices.Add(source);
            }
        }

        public PaAnnouncement Announce(string text, AudioClip clip = null)
        {
            var a = new PaAnnouncement { Text = text, Clip = clip };
            if (Jammed)
            {
                a.Jammed = true;
                a.Done = true;
                return a;
            }
            queue.Enqueue(a);
            return a;
        }

        // A sound through the speaker nearest a point — her footsteps from the wrong side of
        // the store, a crackle, hold music.
        public void PlayNear(Vector3 point, AudioClip clip, float gain = 1f)
        {
            if (Jammed || clip == null) return;
            Transform speaker = NearestSpeaker(point);
            OneShotAudio.PlayAt(clip, speaker != null ? speaker.position : point, gain, SoundKind.Voice);
        }

        void Update()
        {
            if (current == null)
            {
                if (queue.Count == 0) return;
                current = queue.Dequeue();
                if (Jammed) { current.Jammed = true; current.Done = true; current = null; return; }

                current.Started = Time.time;
                Announcements++;
                PositionVoices();
                Duck(true);
                Play(ProceduralAudio.PaChime());
                phase = 1;
                phaseEnds = Time.time + ChimeSeconds;
                ChimeStarted?.Invoke(current);
                AikoScreen.Ensure().Subtitle("<color=#FF6F61>" + GameNames.Antagonist + "</color> <size=70%>(store PA)</size>\n" + current.Text,
                                              1.3f + current.Text.Length * 0.06f + 1.5f);
                return;
            }

            if (Time.time < phaseEnds) return;

            if (phase == 1)
            {
                AudioClip clip = current.Clip != null ? current.Clip : ProceduralAudio.Voice(current.Text);
                Play(clip);
                phase = 2;
                SpeechStarted?.Invoke(current);
                phaseEnds = Time.time + clip.length;
                return;
            }

            current.Done = true;
            current = null;
            phase = 0;
            Duck(false);
        }

        void Play(AudioClip clip)
        {
            foreach (AudioSource v in voices)
            {
                v.volume = volume * SoundSettings.Get(SoundKind.Voice);
                v.PlayOneShot(clip);
            }
        }

        void PositionVoices()
        {
            AudioListener ear = FindAnyObjectByType<AudioListener>();
            Vector3 at = ear != null ? ear.transform.position : Vector3.zero;

            // The three speakers closest to the listener carry the announcement.
            speakers.Sort((a, b) => (a.position - at).sqrMagnitude.CompareTo((b.position - at).sqrMagnitude));
            for (int i = 0; i < voices.Count; i++)
                voices[i].transform.position = i < speakers.Count ? speakers[i].position : at;
        }

        Transform NearestSpeaker(Vector3 point)
        {
            Transform best = null;
            float bestSqr = float.MaxValue;
            foreach (Transform s in speakers)
            {
                float d = (s.position - point).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = s; }
            }
            return best;
        }

        void Duck(bool on)
        {
            if (radio == null) return;
            if (on)
            {
                if (radioVolume < 0f) radioVolume = radio.volume;
                radio.volume = radioVolume * 0.25f;
            }
            else if (radioVolume >= 0f)
            {
                radio.volume = radioVolume;
                radioVolume = -1f;
            }
        }

        public void Clear()
        {
            queue.Clear();
            if (current != null) current.Done = true;
            current = null;
            phase = 0;
            Duck(false);
        }
    }
}
