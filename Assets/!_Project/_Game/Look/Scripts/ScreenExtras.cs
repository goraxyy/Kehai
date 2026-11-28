using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kehai.Look
{
    // Three things on the player's screen (28 November), set from code so no project settings or
    // scene change is needed:
    //   anti-aliasing on the player's camera, always on: SMAA, FXAA on Low quality (it was off,
    //     and every edge showed its steps);
    //   the film look (Settings → Display), StylePresets' fluorescent preset: grain, a light
    //     vignette, a little bloom and colour;
    //   a frame counter in the top right corner (Settings → Display).
    // Tests, bot shifts and renders keep the project's settings, as DisplaySettings does.
    public sealed class ScreenExtras : MonoBehaviour
    {
        const string FpsKey = "Kehai.ShowFps", FilmKey = "Kehai.FilmLook";

        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt(FpsKey, 0) == 1;
            set => PlayerPrefs.SetInt(FpsKey, value ? 1 : 0);
        }

        public static bool FilmLook
        {
            get => PlayerPrefs.GetInt(FilmKey, 0) == 1;
            set { PlayerPrefs.SetInt(FilmKey, value ? 1 : 0); if (instance != null) instance.ApplyFilm(); }
        }

        // The anti-aliasing for a quality setting: SMAA at the setting's own level, FXAA on Low.
        public static AntialiasingMode AntialiasingFor(DisplaySettings.Quality quality) =>
            quality == DisplaySettings.Quality.Low ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        public static AntialiasingQuality SmaaQualityFor(DisplaySettings.Quality quality) =>
            quality == DisplaySettings.Quality.High ? AntialiasingQuality.High
            : quality == DisplaySettings.Quality.Medium ? AntialiasingQuality.Medium : AntialiasingQuality.Low;

        // Under the scene's own volume, so the burnout's vignette and the stun's blur still win.
        public const float FilmPriority = -10f;

        static ScreenExtras instance;
        Volume film;
        VolumeProfile filmProfile;
        float fps, counted, since;
        GUIStyle style;

        public static bool IsFilmVolume(Volume volume) => instance != null && volume != null && volume == instance.film;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Application.isBatchMode || !Edition.ShowUI || instance != null) return;
            var go = new GameObject("Kehai screen extras");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ScreenExtras>();
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            ApplyFilm();
        }

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= BeforeCamera;

        void OnDestroy()
        {
            if (filmProfile != null) Destroy(filmProfile);
            if (instance == this) instance = null;
        }

        static void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || camera != Camera.main) return;
            if (!camera.TryGetComponent(out UniversalAdditionalCameraData data)) return;
            AntialiasingMode mode = AntialiasingFor(DisplaySettings.Current);
            if (data.antialiasing != mode) data.antialiasing = mode;
            AntialiasingQuality quality = SmaaQualityFor(DisplaySettings.Current);
            if (data.antialiasingQuality != quality) data.antialiasingQuality = quality;
        }

        void ApplyFilm()
        {
            bool on = FilmLook;
            if (on && film == null)
            {
                filmProfile = StylePresets.Create(StylePresets.Style.FluorescentRealism, true);
                film = gameObject.AddComponent<Volume>();
                film.isGlobal = true;
                film.priority = FilmPriority;
                film.sharedProfile = filmProfile;
            }
            if (film != null) film.enabled = on;
        }

        void Update()
        {
            counted++;
            since += Time.unscaledDeltaTime;
            if (since < 0.5f) return;
            fps = counted / since;
            counted = 0;
            since = 0f;
        }

        void OnGUI()
        {
            if (!ShowFps || Event.current.type != EventType.Repaint) return;
            int size = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 50f));
            if (style == null || style.fontSize != size)
                style = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
            GUI.depth = -1000;
            string label = $"{fps:0} FPS  {(fps > 0f ? 1000f / fps : 0f):0.0} ms";
            var r = new Rect(Screen.width - size * 14f - 12f, 8f, size * 14f, size * 1.6f);
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), label, style);
            style.normal.textColor = fps >= 50f ? new Color(0.7f, 1f, 0.7f) : fps >= 30f ? new Color(1f, 0.9f, 0.5f) : new Color(1f, 0.55f, 0.5f);
            GUI.Label(r, label, style);
        }
    }
}
