using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Resolution, full screen and quality (Esc → Settings). Unity's player remembers the
// resolution and the full-screen mode itself; the quality is kept in PlayerPrefs.
//
// Quality is three presets over the project's own pipeline settings, not Unity's quality
// levels: the other level has the GPU Resident Drawer off, so the shelves would cost more to
// draw on "Low" than on "High". High is the project's settings as they are. Medium and Low
// render fewer pixels with less anti-aliasing and a coarser sun shadow, set on the pipeline
// asset in use, in place. Swapping in a different asset (a tuned copy, as this used to) makes
// Unity tear the whole pipeline down and build it again, from inside the settings page's
// drawing: the aisle signs lost their Japanese and stayed that way. The shadows' reach is
// left alone: the walls only stop the room lights where they cast shadows, so shorter
// shadows let light through them. In the editor the asset's own values go back when Play
// stops, so it's never saved changed.
public static class DisplaySettings
{
    public enum Quality { Low, Medium, High }

    const string QualityKey = "Kehai.Quality";

    // The asset in use and the values it had, captured the first time quality is set.
    static UniversalRenderPipelineAsset asset;
    static float renderScale;
    static int msaa, sunShadow;
    static UpscalingFilterSelection upscaling;
    static Vector2Int appliedFor;

    public static Quality Current { get; private set; } = Quality.High;

    public static void SetQuality(Quality quality)
    {
        Current = quality;
        PlayerPrefs.SetInt(QualityKey, (int)quality);
        Apply();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Load()
    {
        // Tests, bot shifts and renders keep the project's settings, so their numbers compare.
        if (Application.isBatchMode) return;
        Current = (Quality)Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, (int)Quality.High), 0, 2);
        Apply();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= PutBack;
        UnityEditor.EditorApplication.playModeStateChanged += PutBack;
#endif
    }

    static void Apply()
    {
        if (asset == null)
        {
            asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            renderScale = asset.renderScale;
            msaa = asset.msaaSampleCount;
            sunShadow = asset.mainLightShadowmapResolution;
            upscaling = asset.upscalingFilter;
        }

        bool low = Current == Quality.Low, high = Current == Quality.High;
        float scale = renderScale * Mathf.Min(high ? 1f : low ? 0.7f : 0.85f, PixelCapScale(Screen.width, Screen.height, Current));
        appliedFor = new Vector2Int(Screen.width, Screen.height);
        asset.renderScale = scale;
        // Drawn smaller than the screen: AMD's FSR scales it up sharper than plain stretching.
        asset.upscalingFilter = scale < 0.99f ? UpscalingFilterSelection.FSR : upscaling;
        asset.msaaSampleCount = high ? msaa : low ? 1 : Mathf.Min(msaa, 2);
        asset.mainLightShadowmapResolution = high ? sunShadow : Mathf.Min(sunShadow, low ? 1024 : 2048);
    }

    // The most pixels each quality draws a frame (28 November). A Retina screen (2880 × 1688 on
    // the owner's Mac) is 2.3 times a 1080p one, and the frame's GPU time grows with it; past
    // these the game draws fewer pixels and scales up, so High looks like a sharp 1440p and holds
    // its frame rate. 1 when the screen is smaller.
    public static readonly float[] MaxPixels = { 1.3e6f, 1.9e6f, 2.6e6f };   // Low, Medium, High

    public static float PixelCapScale(int width, int height, Quality quality)
    {
        float pixels = Mathf.Max(1f, (float)width * height);
        return Mathf.Min(1f, Mathf.Sqrt(MaxPixels[(int)quality] / pixels));
    }

    // Again when the window or screen size changes (ScreenExtras asks each frame; cheap).
    public static void Refresh()
    {
        if (Application.isBatchMode || asset == null) return;
        if (appliedFor.x != Screen.width || appliedFor.y != Screen.height) Apply();
    }

#if UNITY_EDITOR
    // The asset's own values go back before the editor could save it.
    static void PutBack(UnityEditor.PlayModeStateChange change)
    {
        if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode || asset == null) return;
        asset.renderScale = renderScale;
        asset.msaaSampleCount = msaa;
        asset.mainLightShadowmapResolution = sunShadow;
        asset.upscalingFilter = upscaling;
        asset = null;
    }
#endif

    // ---- the screen ---------------------------------------------------------------------------

    // The sizes the screen offers, smallest first, each once.
    public static List<Vector2Int> Resolutions()
    {
        var sizes = new List<Vector2Int>();
        foreach (Resolution r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!sizes.Contains(size)) sizes.Add(size);
        }
        var now = new Vector2Int(Screen.width, Screen.height);
        if (!sizes.Contains(now)) sizes.Add(now);
        sizes.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return sizes;
    }

    public static Vector2Int Resolution => new Vector2Int(Screen.width, Screen.height);

    public static void SetResolution(Vector2Int size) => Screen.SetResolution(size.x, size.y, Screen.fullScreenMode);

    public static bool FullScreen
    {
        get => Screen.fullScreenMode != FullScreenMode.Windowed;
        set => Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }
}
